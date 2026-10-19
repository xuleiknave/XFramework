using UnityEngine;
using UnityEngine.UIElements;

namespace Starline.Client
{
    /// <summary>
    /// Chooses a font that can show Chinese. A font file named StarlineUIFont in any
    /// Resources folder wins (ship an open-licence font such as Noto Sans SC this way);
    /// otherwise a CJK font installed on the player's system is used.
    /// </summary>
    public static class UiFont
    {
        public const string Resource = "StarlineUIFont";

        private static readonly string[] SystemFonts =
        {
            "Microsoft YaHei UI", "Microsoft YaHei", "PingFang SC", "Hiragino Sans GB",
            "Noto Sans CJK SC", "Noto Sans SC", "Source Han Sans SC", "WenQuanYi Micro Hei", "SimHei",
        };

        private static Font _font;

        public static void Apply(VisualElement root)
        {
            Font font = Find();
            if (font != null)
            {
                root.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(font));
            }
        }

        private static Font Find()
        {
            if (_font != null)
            {
                return _font;
            }
            _font = Resources.Load<Font>(Resource);
            if (_font == null)
            {
                _font = Font.CreateDynamicFontFromOSFont(SystemFonts, 32);
            }
            if (_font == null)
            {
                Debug.LogWarning("Starline：没有找到中文字体。把 Noto Sans SC 等字体文件命名为 " + Resource + " 放进任意 Resources 文件夹即可。");
            }
            return _font;
        }
    }
}
