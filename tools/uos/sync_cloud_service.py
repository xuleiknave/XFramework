#!/usr/bin/env python3
"""Copy the game server sources into the Unity project for the UOS Func Stateless plugin.

UOS deploys C# cloud functions from one folder of the Unity project (default
Assets/Scripts/CloudService; "所有的函数代码需要存放在同一目录下", UOS docs "开发指南(C#)"), so the
server code under server/src is copied there, flat. Each copied file starts with a marker line and
is wrapped in `#if STARLINE_UOS_FUNC`, so the project still compiles without the SDK. Files without
the marker (the plugin's own samples, NuGetForUnity's Packages folder) are never touched.

The folder also gets a NuGetForUnity packages.config with the libraries the server needs (missing
entries are added, existing ones are kept). Point NuGetForUnity at this folder as the UOS docs say:
NuGet > Preferences, Packages Config Path = the cloud function folder, Packages Install Path =
its Packages subfolder; then NuGet > Restore Packages.

    python3 tools/uos/sync_cloud_service.py                  # copy
    python3 tools/uos/sync_cloud_service.py --with-protocol  # also copy the shared message types
    python3 tools/uos/sync_cloud_service.py --clean          # remove the copies
    python3 tools/uos/sync_cloud_service.py --target Assets/Scripts/MyCloud   # another folder

--with-protocol: the message types (Runtime/Battle/Online) are part of the Starline.Battle assembly,
which the editor's local mode sees. If the upload fails because it cannot find Starline.Online types,
copy them in too (the editor then warns CS0436 about the duplicate types, which is harmless).
"""
import argparse
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
PROJECT = ROOT / "Starline"
DEFAULT_TARGET = "Assets/Scripts/CloudService"
SOURCES = [
    "server/src/Starline.Server.Uos",
    "server/src/Starline.Server.Game",
    "server/src/Starline.Server.Data",
    "server/src/Starline.Server.Data.CrudSave",
]
PROTOCOL = "Starline/Packages/com.starline.game/Runtime/Battle/Online"
MARKER = "// Copied by tools/uos/sync_cloud_service.py"
HEADER = MARKER + " from {0}; edit that file instead.\n#if STARLINE_UOS_FUNC\n"
FOOTER = "\n#endif\n"
# Libraries of server/src/Starline.Server.Data.CrudSave (UOS.FuncStateless.MongoDB 1.1.8 needs
# MongoDB.Driver 2.28.0). Newtonsoft.Json comes with the UOS runtime and with Tuanjie.
PACKAGES = [("UOS.FuncStateless.MongoDB", "1.1.8"), ("MongoDB.Driver", "2.28.0")]


def remove_copies(target):
    removed = 0
    for path in target.glob("*.cs"):
        with path.open(encoding="utf-8") as f:
            first = f.readline()
        if first.startswith(MARKER):
            path.unlink()
            meta = path.with_name(path.name + ".meta")
            if meta.exists():
                meta.unlink()
            removed += 1
    return removed


def write_packages_config(target):
    path = target / "packages.config"
    text = path.read_text(encoding="utf-8") if path.exists() else (
        '<?xml version="1.0" encoding="utf-8"?>\n<packages>\n</packages>\n')
    added = []
    for package_id, version in PACKAGES:
        if re.search(r'id="{0}"'.format(re.escape(package_id)), text, re.IGNORECASE):
            continue
        line = '  <package id="{0}" version="{1}" manuallyInstalled="true" />\n'.format(package_id, version)
        text = text.replace("</packages>", line + "</packages>")
        added.append(package_id + " " + version)
    path.write_text(text, encoding="utf-8")
    return added


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--target", default=DEFAULT_TARGET, help="cloud function folder, relative to Starline/ (default %(default)s)")
    parser.add_argument("--with-protocol", action="store_true", help="also copy Runtime/Battle/Online")
    parser.add_argument("--clean", action="store_true", help="remove the copied files")
    args = parser.parse_args()

    target = PROJECT / args.target
    target.mkdir(parents=True, exist_ok=True)
    removed = remove_copies(target)
    if args.clean:
        print("已删除 {0} 个复制的文件（{1}）".format(removed, target.relative_to(ROOT)))
        return 0

    folders = SOURCES + ([PROTOCOL] if args.with_protocol else [])
    seen = {}
    for folder in folders:
        for path in sorted((ROOT / folder).glob("*.cs")):
            relative = path.relative_to(ROOT).as_posix()
            if path.name in seen:
                print("错误：{0} 和 {1} 同名，放进同一个目录会冲突".format(relative, seen[path.name]), file=sys.stderr)
                return 1
            seen[path.name] = relative
            dest = target / path.name
            if dest.exists():
                print("错误：{0} 已经存在且不是脚本复制的，先移走它".format(dest.relative_to(ROOT)), file=sys.stderr)
                return 1
            dest.write_text(HEADER.format(relative) + path.read_text(encoding="utf-8") + FOOTER, encoding="utf-8")
    added = write_packages_config(target)

    print("已复制 {0} 个文件到 {1}".format(len(seen), target.relative_to(ROOT)))
    if added:
        print("packages.config 新加了：" + "，".join(added) + "（在团结里用 NuGet > Restore Packages 安装）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
