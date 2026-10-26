using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.UIElements;
using Starline.Battle;

namespace Starline.Client
{
    /// <summary>
    /// Player-facing text through Unity Localization. Four string tables, all imported from
    /// Localization/*.csv by the editor (Starline &gt; 本地化): UI for the interface, Game for
    /// the core's <see cref="LocText"/> (unit names, act story, battle messages), Characters for
    /// heroes (names, titles, skills, bios) and Story for the main campaign. Placeholders are
    /// {0}, {1}…; a language without a translation falls back to Simplified Chinese.
    /// </summary>
    public static class Loc
    {
        public const string UiTable = "UI";
        public const string GameTable = "Game";
        public const string CharactersTable = "Characters";
        public const string StoryTable = "Story";
        public const string SourceLanguage = "zh-Hans";
        /// <summary>In UXML, text="#key" (or tooltip="#key") is replaced with the UI table entry when the layout loads.</summary>
        public const char KeyPrefix = '#';

        private static bool _warned;

        /// <summary>Raised after the language changes; screens built before it keep their old text until rebuilt.</summary>
        public static event Action LanguageChanged;

        public static bool IsReady => LocalizationSettings.HasSettings && LocalizationSettings.InitializationOperation.IsDone;

        /// <summary>Code of the current language, such as zh-Hans or en.</summary>
        public static string Language => IsReady && LocalizationSettings.SelectedLocale != null
            ? LocalizationSettings.SelectedLocale.Identifier.Code
            : SourceLanguage;

        /// <summary>
        /// Waits for the locales and the preloaded tables. On first launch the language follows
        /// the system (Localization Settings → Locale Selectors), falling back to Simplified Chinese.
        /// </summary>
        public static IEnumerator Initialize()
        {
            if (!LocalizationSettings.HasSettings)
            {
                Warn();
                yield break;
            }
            yield return LocalizationSettings.InitializationOperation;
            LocalizationSettings.SelectedLocaleChanged += _ => LanguageChanged?.Invoke();
        }

        /// <summary>Switches language by code; unknown codes are ignored. Returns whether the language exists.</summary>
        public static bool SetLanguage(string code)
        {
            if (!IsReady || string.IsNullOrEmpty(code))
            {
                return false;
            }
            Locale locale = LocalizationSettings.AvailableLocales.GetLocale(code);
            if (locale == null)
            {
                return false;
            }
            LocalizationSettings.SelectedLocale = locale;
            return true;
        }

        /// <summary>Interface text from the UI table.</summary>
        public static string Ui(string key, params object[] args) => Get(UiTable, key, args);

        /// <summary>Hero text from the Characters table, such as bio.gavin.1.</summary>
        public static string Hero(string key, params object[] args) => Get(CharactersTable, key, args);

        /// <summary>Main campaign text from the Story table, such as story.chapter.1.title.</summary>
        public static string Story(string key, params object[] args) => Get(StoryTable, key, args);

        /// <summary>Text produced by the core, from the Game table.</summary>
        public static string T(LocText text) => text.IsEmpty ? string.Empty : Get(GameTable, text.Key, Resolve(text.Args));

        /// <summary>For optional text such as enemy descriptions: false when the key has no entry.</summary>
        public static bool Has(LocText text)
        {
            if (text.IsEmpty || !IsReady)
            {
                return false;
            }
            var result = LocalizationSettings.StringDatabase.GetTableEntry((TableReference)GameTable, (TableEntryReference)text.Key);
            return result.Entry != null && !string.IsNullOrEmpty(result.Entry.Value);
        }

        /// <summary>Replaces "#key" texts and tooltips under <paramref name="root"/> with UI table entries.</summary>
        public static void Localize(VisualElement root)
        {
            root.Query<VisualElement>().ForEach(element =>
            {
                if (element is TextElement text && IsKey(text.text))
                {
                    text.text = Ui(text.text.Substring(1));
                }
                if (IsKey(element.tooltip))
                {
                    element.tooltip = Ui(element.tooltip.Substring(1));
                }
            });
        }

        private static bool IsKey(string text) => !string.IsNullOrEmpty(text) && text.Length > 1 && text[0] == KeyPrefix;

        private static object[] Resolve(object[] args)
        {
            var resolved = new object[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                resolved[i] = args[i] is LocText inner ? T(inner) : args[i];
            }
            return resolved;
        }

        private static string Get(string table, string key, object[] args)
        {
            if (!LocalizationSettings.HasSettings)
            {
                Warn();
                return args.Length == 0 ? key : key + "(" + string.Join(", ", args) + ")";
            }
            return LocalizationSettings.StringDatabase.GetLocalizedString((TableReference)table, (TableEntryReference)key, args);
        }

        private static void Warn()
        {
            if (!_warned)
            {
                _warned = true;
                Debug.LogWarning("Starline：还没有本地化配置，界面会显示 key。请点菜单 Starline > 初始化工程。");
            }
        }
    }
}
