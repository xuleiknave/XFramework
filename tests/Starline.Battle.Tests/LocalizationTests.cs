using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Starline.Battle;
using Starline.Expedition;

namespace Starline.Battle.Tests
{
    internal static partial class Program
    {
        private static readonly Regex Placeholder = new Regex(@"\{(\d+)[^}]*\}");

        /// <summary>Every row has source text, and translations use the same placeholders.</summary>
        private static void LocalizationTablesAreComplete()
        {
            foreach (string name in Fixtures.TableNames)
            {
                LocCsv table = Fixtures.LoadTable(name);
                Assert(table.Languages.Contains(Fixtures.SourceLanguage), name + ".csv 缺少 " + Fixtures.SourceLanguage + " 列");
                var missing = new Dictionary<string, int>();
                foreach (LocCsv.Row row in table.Rows)
                {
                    string source = row.Get(Fixtures.SourceLanguage);
                    Assert(source.Length > 0, name + ".csv 的 " + row.Key + " 没有中文");
                    foreach (string language in table.Languages)
                    {
                        string text = row.Get(language);
                        if (text.Length == 0)
                        {
                            missing.TryGetValue(language, out int n);
                            missing[language] = n + 1;
                            continue;
                        }
                        Assert(Placeholders(text) == Placeholders(source),
                            name + ".csv 的 " + row.Key + "（" + language + "）占位符和中文不一致");
                    }
                }
                Assert(LocCsv.Parse(table.Write()).Write() == table.Write(), name + ".csv 读写不一致");
                string untranslated = "";
                foreach (KeyValuePair<string, int> kv in missing)
                {
                    untranslated += "，" + kv.Key + " 未翻译 " + kv.Value + " 条";
                }
                Info(name + ".csv：" + table.Rows.Count + " 条" + untranslated);
            }
        }

        /// <summary>Every hero in Characters.csv has a name and all five bio paragraphs (★1~★5).</summary>
        private static void HeroesHaveBios()
        {
            var keys = new HashSet<string>();
            var heroes = new List<string>();
            foreach (LocCsv.Row row in Fixtures.LoadTable("Characters").Rows)
            {
                keys.Add(row.Key);
                string[] parts = row.Key.Split('.');
                if (parts.Length == 3 && parts[0] == "hero" && parts[2] == "name")
                {
                    heroes.Add(parts[1]);
                }
            }
            foreach (string hero in heroes)
            {
                for (int star = 1; star <= 5; star++)
                {
                    Assert(keys.Contains("bio." + hero + "." + star), "Characters.csv 缺少 bio." + hero + "." + star);
                }
            }
            foreach (string key in keys)
            {
                if (key.StartsWith("bio.") || key.StartsWith("hero."))
                {
                    Assert(heroes.Contains(key.Split('.')[1]), "Characters.csv 的 " + key + " 没有对应的 hero.<id>.name");
                }
            }
            Assert(heroes.Count == 47, "Characters.csv 应有 45 名角色 + 2 名传说角色，现在是 " + heroes.Count);
            Info("角色小传：" + heroes.Count + " 名角色");
        }

        /// <summary>Units, skills, encounters and acts in the JSON tables all have their text.</summary>
        private static void GameDataHasText()
        {
            foreach (UnitDef def in UnitCatalog.All)
            {
                Fixtures.Text(def.Name);
            }
            foreach (CommanderSkillDef skill in CommanderSkillDef.All)
            {
                Fixtures.Text(skill.Name);
                Fixtures.Text(skill.Description);
            }
            foreach (string id in EncounterLibrary.Ids())
            {
                Encounter e = EncounterLibrary.Create(id);
                Fixtures.Text(e.Name);
                Fixtures.Text(e.Briefing);
            }
            foreach (TileDef tile in TerrainCatalog.Tiles)
            {
                Fixtures.Text(tile.Name);
                Fixtures.Text(tile.Description);
                tile.Effects.ForEach(e => Fixtures.Text(e.Text));
            }
            foreach (TerrainDef terrain in TerrainCatalog.Terrains)
            {
                Fixtures.Text(terrain.Name);
                Fixtures.Text(terrain.Description);
                terrain.Effects.ForEach(e => Fixtures.Text(e.Text));
            }
            foreach (ActConfig act in ExpeditionConfig.Current.Acts)
            {
                ActStory s = act.Story;
                foreach (LocText text in new[] { act.Title, s.IntroTitle, s.Intro, s.VictoryTitle, s.Victory, s.DefeatTitle, s.Defeat })
                {
                    Fixtures.Text(text);
                }
            }
        }

        /// <summary>
        /// UI.csv matches the client: every "#key" in the UXML layouts and in settings.json (option
        /// names), and every key-shaped string literal in the client code ("map.title", including
        /// both sides of a ?:) exists, and every row is used. This is the only check possible without Unity.
        /// </summary>
        private static void ClientKeysExist()
        {
            var keys = new HashSet<string>();
            Fixtures.LoadTable("UI").Rows.ForEach(r => keys.Add(r.Key));
            string package = Fixtures.FindDirectory("Starline/Packages/com.starline.game/Runtime");
            var used = new HashSet<string>();
            var uxml = new Regex("(?:text|tooltip)=\"#([^\"]+)\"");
            var code = new Regex("\"([a-z][a-z_]*\\.[a-z0-9_.]+)\"");
            void Check(string file, Regex pattern)
            {
                foreach (Match m in pattern.Matches(File.ReadAllText(file)))
                {
                    if (m.Groups[1].Value.EndsWith(".json"))
                    {
                        continue; // a config file name, not a text key
                    }
                    used.Add(m.Groups[1].Value);
                    Assert(keys.Contains(m.Groups[1].Value), Path.GetFileName(file) + " 用到的 " + m.Groups[1].Value + " 不在 UI.csv 里");
                }
            }
            foreach (string file in Directory.GetFiles(Path.Combine(package, "Resources/StarlineUI"), "*.uxml"))
            {
                Check(file, uxml);
            }
            Check(Path.Combine(package, "Resources/StarlineData/settings.json"), new Regex("\"#([^\"]+)\""));
            foreach (string file in Directory.GetFiles(Path.Combine(package, "Client"), "*.cs", SearchOption.AllDirectories))
            {
                if (file.Replace('\\', '/').Contains("/Client/Audio/"))
                {
                    continue; // strings there are music.json routes such as "battle.boss", not text keys
                }
                Check(file, code);
            }
            var unused = new List<string>();
            foreach (string key in keys)
            {
                if (!used.Contains(key))
                {
                    unused.Add(key);
                }
            }
            Assert(unused.Count == 0, "UI.csv 里有没用到的 key：" + string.Join("，", unused));
            Info("界面用到 " + used.Count + " 个 key");
        }

        private static string Placeholders(string text)
        {
            var found = new SortedSet<int>();
            foreach (Match m in Placeholder.Matches(text))
            {
                found.Add(int.Parse(m.Groups[1].Value));
            }
            return string.Join(",", found);
        }
    }
}
