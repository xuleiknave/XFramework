using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Metadata;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using Starline.Battle;
using Starline.Client;

namespace Starline.Editor
{
    /// <summary>
    /// Unity Localization setup. The CSV files in Packages/com.starline.game/Localization are
    /// the source of the text (they can be edited and reviewed without Unity); this class
    /// imports them into the string table collections under Assets/Localization, creating the
    /// Localization Settings, one locale per CSV column and the Addressables settings on the way.
    /// Editing a CSV re-imports it automatically. After editing a table inside Unity, use
    /// "导出语言表到 CSV" so the change reaches the CSV (otherwise the next import overwrites it).
    /// </summary>
    internal static class LocalizationSetup
    {
        public const string CsvFolder = "Packages/com.starline.game/Localization";
        private const string AssetFolder = "Assets/Localization";
        private const string LocaleFolder = AssetFolder + "/Locales";
        private const string TableFolder = AssetFolder + "/Tables";
        private static readonly string[] TableNames = { Loc.UiTable, Loc.GameTable, Loc.CharactersTable, Loc.StoryTable };

        [MenuItem("Starline/本地化/从 CSV 导入语言表", priority = 20)]
        public static void Import()
        {
            var tables = new List<(string name, LocCsv csv)>();
            foreach (string name in TableNames)
            {
                tables.Add((name, LocCsv.Parse(File.ReadAllText(CsvPath(name)))));
            }
            EnsureFolder(LocaleFolder);
            EnsureFolder(TableFolder);
            EnsureAddressables();
            LocalizationSettings settings = EnsureSettings();
            var languages = new List<string>();
            foreach ((string _, LocCsv csv) in tables)
            {
                languages.AddRange(csv.Languages.FindAll(l => !languages.Contains(l)));
            }
            EnsureLocales(languages);
            foreach ((string name, LocCsv csv) in tables)
            {
                ImportTable(name, csv);
            }
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log("Starline：已从 " + CsvFolder + " 导入语言表（" + string.Join("、", languages) + "）");
        }

        [MenuItem("Starline/本地化/导出语言表到 CSV", priority = 21)]
        public static void Export()
        {
            foreach (string name in TableNames)
            {
                StringTableCollection collection = LocalizationEditorSettings.GetStringTableCollection(name);
                if (collection == null)
                {
                    Debug.LogWarning("Starline：还没有字符串表 " + name + "，请先导入。");
                    continue;
                }
                string path = CsvPath(name);
                LocCsv old = File.Exists(path) ? LocCsv.Parse(File.ReadAllText(path)) : new LocCsv();
                var csv = new LocCsv();
                csv.Languages.AddRange(old.Languages);
                foreach (Locale locale in LocalizationEditorSettings.GetLocales())
                {
                    if (!csv.Languages.Contains(locale.Identifier.Code))
                    {
                        csv.Languages.Add(locale.Identifier.Code);
                    }
                }
                // Keep the CSV's row order and notes; keys added inside Unity go to the end.
                var keys = new List<string>();
                old.Rows.ForEach(r => keys.Add(r.Key));
                foreach (SharedTableData.SharedTableEntry entry in collection.SharedData.Entries)
                {
                    if (!keys.Contains(entry.Key))
                    {
                        keys.Add(entry.Key);
                    }
                }
                foreach (string key in keys)
                {
                    if (!collection.SharedData.Contains(key))
                    {
                        continue;
                    }
                    LocCsv.Row previous = old.Rows.Find(r => r.Key == key);
                    var row = new LocCsv.Row { Key = key, Note = previous?.Note ?? "" };
                    foreach (string language in csv.Languages)
                    {
                        var table = collection.GetTable(language) as StringTable;
                        StringTableEntry entry = table != null ? table.GetEntry(key) : null;
                        row.Text[language] = entry?.Value ?? "";
                    }
                    csv.Rows.Add(row);
                }
                File.WriteAllText(path, csv.Write());
            }
            AssetDatabase.Refresh();
            Debug.Log("Starline：已把字符串表导出到 " + CsvFolder);
        }

        private static string CsvPath(string table) => Path.GetFullPath(CsvFolder + "/" + table + ".csv");

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        /// <summary>The string tables are Addressables; build their content together with the player.</summary>
        private static void EnsureAddressables()
        {
            AddressableAssetSettings addressables = AddressableAssetSettingsDefaultObject.GetSettings(true);
            if (addressables.BuildAddressablesWithPlayerBuild != AddressableAssetSettings.PlayerBuildOption.BuildWithPlayer)
            {
                addressables.BuildAddressablesWithPlayerBuild = AddressableAssetSettings.PlayerBuildOption.BuildWithPlayer;
                EditorUtility.SetDirty(addressables);
            }
        }

        /// <summary>
        /// Startup language: command line (-language=en), then the system language, then
        /// Simplified Chinese. A choice saved in the settings is applied afterwards with Loc.SetLanguage.
        /// </summary>
        private static LocalizationSettings EnsureSettings()
        {
            LocalizationSettings settings = LocalizationEditorSettings.ActiveLocalizationSettings;
            if (settings != null)
            {
                return settings;
            }
            settings = ScriptableObject.CreateInstance<LocalizationSettings>();
            AssetDatabase.CreateAsset(settings, AssetFolder + "/Localization Settings.asset");
            LocalizationEditorSettings.ActiveLocalizationSettings = settings;
            List<IStartupLocaleSelector> selectors = settings.GetStartupLocaleSelectors();
            selectors.Clear();
            selectors.Add(new CommandLineLocaleSelector());
            selectors.Add(new SystemLocaleSelector());
            selectors.Add(new SpecificLocaleSelector { LocaleId = new LocaleIdentifier(Loc.SourceLanguage) });
            return settings;
        }

        private static void EnsureLocales(List<string> languages)
        {
            Locale source = null;
            for (int i = 0; i < languages.Count; i++)
            {
                string code = languages[i];
                Locale locale = LocalizationEditorSettings.GetLocale(code);
                if (locale == null)
                {
                    locale = Locale.CreateLocale(new LocaleIdentifier(code));
                    AssetDatabase.CreateAsset(locale, LocaleFolder + "/" + code + ".asset");
                    LocalizationEditorSettings.AddLocale(locale);
                }
                locale.SortOrder = (ushort)i;
                if (code == Loc.SourceLanguage)
                {
                    source = locale;
                }
                EditorUtility.SetDirty(locale);
            }
            if (source == null)
            {
                throw new System.InvalidOperationException("语言表缺少 " + Loc.SourceLanguage + " 列");
            }
            // Untranslated entries are left out of a table, so they fall back to Simplified Chinese.
            foreach (string code in languages)
            {
                Locale locale = LocalizationEditorSettings.GetLocale(code);
                if (locale != source && locale.Metadata.GetMetadata<FallbackLocale>() == null)
                {
                    locale.Metadata.AddMetadata(new FallbackLocale { Locale = source });
                    EditorUtility.SetDirty(locale);
                }
            }
        }

        private static void ImportTable(string name, LocCsv csv)
        {
            StringTableCollection collection = LocalizationEditorSettings.GetStringTableCollection(name)
                ?? LocalizationEditorSettings.CreateStringTableCollection(name, TableFolder);
            SharedTableData shared = collection.SharedData;
            var tables = new List<(string language, StringTable table)>();
            foreach (string language in csv.Languages)
            {
                var table = collection.GetTable(language) as StringTable ?? collection.AddNewTable(language) as StringTable;
                collection.SetPreloadTableFlag(table, true);
                tables.Add((language, table));
            }

            var keep = new HashSet<string>();
            foreach (LocCsv.Row row in csv.Rows)
            {
                keep.Add(row.Key);
                if (!shared.Contains(row.Key))
                {
                    shared.AddKey(row.Key);
                }
                long id = shared.GetId(row.Key);
                foreach ((string language, StringTable table) in tables)
                {
                    string text = row.Get(language);
                    StringTableEntry entry = table.GetEntry(id);
                    if (text.Length == 0)
                    {
                        table.RemoveEntry(id);
                    }
                    else if (entry == null)
                    {
                        table.AddEntry(id, text);
                    }
                    else
                    {
                        entry.Value = text;
                    }
                }
            }
            foreach (SharedTableData.SharedTableEntry entry in new List<SharedTableData.SharedTableEntry>(shared.Entries))
            {
                if (!keep.Contains(entry.Key))
                {
                    foreach ((string _, StringTable table) in tables)
                    {
                        table.RemoveEntry(entry.Id);
                    }
                    shared.RemoveKey(entry.Key);
                }
            }

            EditorUtility.SetDirty(shared);
            foreach ((string _, StringTable table) in tables)
            {
                EditorUtility.SetDirty(table);
            }
        }
    }

    /// <summary>Re-imports the string tables whenever a CSV in the Localization folder changes (for example after git pull).</summary>
    internal sealed class LocalizationCsvWatcher : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (string path in imported)
            {
                if (path.StartsWith(LocalizationSetup.CsvFolder + "/") && path.EndsWith(".csv"))
                {
                    EditorApplication.delayCall -= LocalizationSetup.Import;
                    EditorApplication.delayCall += LocalizationSetup.Import;
                    return;
                }
            }
        }
    }
}
