#!/usr/bin/env python3
"""Copy the game server sources into the Unity project for the UOS Func Stateless plugin.

UOS deploys C# cloud functions from a folder of the Unity project (default Assets/Scripts/CloudService,
UOS docs "开发指南(C#)"), so the server code under server/src is copied there. Each copied file is
wrapped in `#if STARLINE_UOS_FUNC`, so the project still compiles without the SDK. The target folder is
generated: it is in .gitignore, edit the files under server/src instead and run this script again.

    python3 tools/uos/sync_cloud_service.py                  # copy
    python3 tools/uos/sync_cloud_service.py --with-protocol  # also copy the shared message types
    python3 tools/uos/sync_cloud_service.py --clean          # remove the copies

--with-protocol: the message types (Runtime/Battle/Online) are part of the Starline.Battle assembly,
which the editor's local mode sees. If the upload fails because it cannot find Starline.Online types,
copy them in too (the editor then warns CS0436 about the duplicate types, which is harmless).
"""
import argparse
import pathlib
import shutil
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
TARGET = ROOT / "Starline/Assets/Scripts/CloudService/Starline"
SOURCES = [
    "server/src/Starline.Server.Uos",
    "server/src/Starline.Server.Game",
    "server/src/Starline.Server.Data",
    "server/src/Starline.Server.Data.CrudSave",
]
PROTOCOL = "Starline/Packages/com.starline.game/Runtime/Battle/Online"
HEADER = "// Copied by tools/uos/sync_cloud_service.py from {0}; edit that file instead.\n#if STARLINE_UOS_FUNC\n"
FOOTER = "\n#endif\n"


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--with-protocol", action="store_true", help="also copy Runtime/Battle/Online")
    parser.add_argument("--clean", action="store_true", help="remove the copied files")
    args = parser.parse_args()

    if TARGET.exists():
        shutil.rmtree(TARGET)
        meta = TARGET.with_name(TARGET.name + ".meta")
        if meta.exists():
            meta.unlink()
    if args.clean:
        print("已删除", TARGET.relative_to(ROOT))
        return 0

    folders = SOURCES + ([PROTOCOL] if args.with_protocol else [])
    count = 0
    for folder in folders:
        source = ROOT / folder
        for path in sorted(source.glob("*.cs")):
            relative = path.relative_to(ROOT).as_posix()
            # All cloud function code in one folder (UOS docs); file names are unique across the sources.
            dest = TARGET / path.name
            dest.parent.mkdir(parents=True, exist_ok=True)
            dest.write_text(HEADER.format(relative) + path.read_text(encoding="utf-8") + FOOTER, encoding="utf-8")
            count += 1
    print("已复制 {0} 个文件到 {1}".format(count, TARGET.relative_to(ROOT)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
