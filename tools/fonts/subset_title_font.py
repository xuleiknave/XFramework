"""
裁剪标题字体：把思源宋体（Noto Serif SC Bold）裁到语言表和配置表里实际用到的字，
输出到 Unity 包的 Resources/StarlineTitleFont.ttf（完整字体 14 MB，裁剪后约 0.5 MB）。

语言表新增文字后重新运行一次，否则新字在标题里会缺字（正文字体不受影响）：
    pip install fonttools
    python3 tools/fonts/subset_title_font.py <NotoSerifSC-Bold.ttf>

字体下载：https://fonts.google.com/noto/specimen/Noto+Serif+SC（SIL OFL 1.1）。
"""

import glob
import os
import sys

from fontTools import subset

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
PACKAGE = os.path.join(REPO, "Starline", "Packages", "com.starline.game")
OUT = os.path.join(PACKAGE, "Runtime", "Resources", "StarlineTitleFont.ttf")

FAMILY = "Starline Title"

# 语言表以外也可能出现在标题里的字：ASCII、常用中文标点、星级和箭头
EXTRA = "".join(chr(c) for c in range(0x20, 0x7F)) + "·—…“”‘’《》「」『』【】（）、，。：；！？★☆◆◇←→↑↓×％"


def used_characters():
    chars = set(EXTRA)
    sources = glob.glob(os.path.join(PACKAGE, "Localization", "*.csv"))
    sources += glob.glob(os.path.join(PACKAGE, "Runtime", "Resources", "StarlineData", "*.json"))
    for path in sources:
        with open(path, encoding="utf-8") as f:
            chars |= set(f.read())
    return "".join(sorted(c for c in chars if c not in "\r\n\t"))


def main():
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    text = used_characters()
    options = subset.Options()
    options.layout_features = ["*"]
    options.name_IDs = ["*"]
    options.notdef_outline = True
    font = subset.load_font(sys.argv[1], options)
    subsetter = subset.Subsetter(options)
    subsetter.populate(text=text)
    subsetter.subset(font)
    # OFL：改过的字体不能再用保留名称（Source），也不沿用 Noto 的名字
    for record in font["name"].names:
        if record.nameID in (1, 16):
            record.string = FAMILY
        elif record.nameID in (3, 4):
            record.string = FAMILY + " Bold"
        elif record.nameID == 6:
            record.string = FAMILY.replace(" ", "") + "-Bold"
    subset.save_font(font, OUT, options)
    print("%d 个字 → %s（%d KB）" % (len(text), OUT, os.path.getsize(OUT) // 1024))


if __name__ == "__main__":
    main()
