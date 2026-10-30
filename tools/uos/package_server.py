#!/usr/bin/env python3
"""Build the zip for a UOS server program image (服务器程序镜像, dotnet-runtime:9.0).

Publishes server/src/Starline.Server.Host the way UOS's Func Stateful guide does for C#
(`dotnet publish -c Release -r linux-x64 --no-self-contained`) and zips every published file at the
root of the archive. In the UOS console:

  - 添加服务器程序: upload the zip
  - 需要增加执行权限的文件: starline-server
  - 入口程序启动命令: ./starline-server   (add --dev-login for the development app; see server/README.md)
  - 端口: 8899

    python3 tools/uos/package_server.py                 # -> dist/starline-server-<version>.zip
    python3 tools/uos/package_server.py --out some.zip
    python3 tools/uos/package_server.py --windows       # -> dist/starline-server-<version>-win-x64.zip

--windows builds for the Hong Kong Windows server instead (decision 71): self-contained win-x64, so the
server needs no .NET runtime, plus server/deploy/windows/install.ps1, which installs MongoDB and starts
the server at boot (server/README.md, "部署到自己的 Windows 服务器").
"""
import argparse
import pathlib
import re
import shutil
import stat
import subprocess
import sys
import tempfile
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[2]
PROJECT = ROOT / "server/src/Starline.Server.Host/Starline.Server.Host.csproj"
EXECUTABLE = "starline-server"
WINDOWS_INSTALLER = ROOT / "server/deploy/windows/install.ps1"


def version():
    text = (ROOT / "server/src/Starline.Server.Game/GameServer.cs").read_text(encoding="utf-8")
    match = re.search(r'Version = "([^"]+)"', text)
    raw = match.group(1) if match else "dev"
    # Lowercase letters, digits, '.' and '-' only, like UOS image tags.
    return re.sub(r"[^a-z0-9.]+", "-", raw.lower()).strip("-.")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--out", help="zip path (default dist/starline-server-<version>.zip)")
    parser.add_argument("--windows", action="store_true", help="self-contained win-x64 build with install.ps1")
    args = parser.parse_args()
    suffix = "-win-x64" if args.windows else ""
    out = pathlib.Path(args.out) if args.out else ROOT / "dist" / "starline-server-{0}{1}.zip".format(version(), suffix)
    out.parent.mkdir(parents=True, exist_ok=True)

    with tempfile.TemporaryDirectory() as tmp:
        publish = pathlib.Path(tmp) / "publish"
        runtime = ["-r", "win-x64", "--self-contained"] if args.windows else ["-r", "linux-x64", "--no-self-contained"]
        subprocess.run(["dotnet", "publish", str(PROJECT), "-c", "Release"] + runtime
                       + ["-o", str(publish), "-nologo", "-v", "q", "-p:DebugType=none"], check=True)
        if args.windows:
            shutil.copy(WINDOWS_INSTALLER, publish / WINDOWS_INSTALLER.name)
        exe = publish / (EXECUTABLE + (".exe" if args.windows else ""))
        if not exe.exists():
            print("错误：发布结果里没有 " + EXECUTABLE, file=sys.stderr)
            return 1
        if out.exists():
            out.unlink()
        with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
            for path in sorted(publish.rglob("*")):
                if path.is_dir():
                    continue
                name = path.relative_to(publish).as_posix()
                info = zipfile.ZipInfo(name)
                info.compress_type = zipfile.ZIP_DEFLATED
                mode = 0o755 if name == EXECUTABLE else 0o644
                info.external_attr = (stat.S_IFREG | mode) << 16
                z.writestr(info, path.read_bytes())
    size = out.stat().st_size / 1024 / 1024
    print("已生成 {0}（{1:.1f} MB）".format(out.relative_to(ROOT) if out.is_relative_to(ROOT) else out, size))
    if args.windows:
        print("Windows：解压后在管理员 PowerShell 里运行 powershell -ExecutionPolicy Bypass -File .\\install.ps1 -DevLogin")
    else:
        print("UOS：需要增加执行权限的文件填 {0}，入口程序启动命令填 ./{0}，端口 8899".format(EXECUTABLE))
    return 0


if __name__ == "__main__":
    sys.exit(main())
