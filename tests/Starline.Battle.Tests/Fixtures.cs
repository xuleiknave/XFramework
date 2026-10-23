using System;
using System.Collections.Generic;
using System.IO;
using Starline.Battle;

namespace Starline.Battle.Tests
{
    internal static class Fixtures
    {
        /// <summary>Loads the game data straight from the Unity package's Resources folder.</summary>
        public static void LoadGameData()
        {
            string dir = FindDataDirectory();
            GameData.Load(name =>
            {
                string path = Path.Combine(dir, name + ".json");
                return File.Exists(path) ? File.ReadAllText(path) : null;
            });
        }

        public const string LocalizationDirectory = "Starline/Packages/com.starline.game/Localization";
        public const string SourceLanguage = "zh-Hans";

        private static LocCsv _gameText;

        /// <summary>The string tables in Localization/*.csv (Game, UI).</summary>
        public static LocCsv LoadTable(string name) =>
            LocCsv.Parse(File.ReadAllText(Path.Combine(FindDirectory(LocalizationDirectory), name + ".csv")));

        /// <summary>Core text in Simplified Chinese, so test output stays readable; fails on a missing key.</summary>
        public static string Text(LocText text)
        {
            if (_gameText == null)
            {
                _gameText = LoadTable("Game");
            }
            LocCsv.Row row = _gameText.Rows.Find(r => r.Key == text.Key);
            if (row == null)
            {
                throw new Exception("Game.csv 缺少 key " + text.Key);
            }
            var args = new object[text.Args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                args[i] = text.Args[i] is LocText inner ? Text(inner) : text.Args[i];
            }
            return string.Format(row.Get(SourceLanguage), args);
        }

        public static string Join(string separator, List<LocText> texts) => string.Join(separator, texts.ConvertAll(Text));

        private static string FindDataDirectory() => FindDirectory("Starline/Packages/com.starline.game/Runtime/Resources/StarlineData");

        public static string FindDirectory(string relative)
        {
            foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                {
                    string candidate = Path.Combine(dir.FullName, relative);
                    if (Directory.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
            throw new DirectoryNotFoundException("找不到数据目录 " + relative);
        }

        /// <summary>The original prototype battle (matches the formation mock-up); many tests use it as a fixed scenario.</summary>
        public static Encounter WreckFieldAmbush()
        {
            var enemy = new Formation()
                .Add("hive", 4, 3)
                .Add("hive_node", 2, 4).Add("hive_node", 2, 1).Add("hive_node", 2, 7)
                .Add("ripper", 0, 1).Add("crawler", 0, 2).Add("crawler", 0, 5).Add("ripper", 0, 6)
                .Add("crawler", 1, 3).Add("crawler", 1, 4)
                .Add("locust", 1, 0).Add("locust", 1, 7)
                .Add("acid_turret", 2, 2).Add("acid_turret", 2, 5)
                .Add("brood_mother", 3, 0);

            var player = new Formation()
                .Add("supply_ship", 4, 3)
                .Add("relay", 2, 4).Add("relay", 2, 1).Add("relay", 2, 7)
                .Add("battleship", 0, 3)
                .Add("frigate", 0, 1).Add("frigate", 0, 6)
                .Add("strike_craft", 0, 0).Add("strike_craft", 5, 7)
                .Add("pd_ship", 2, 2).Add("pd_ship", 2, 5)
                .Add("missile_ship", 3, 1).Add("missile_ship", 3, 6).Add("missile_ship", 5, 0);

            return new Encounter
            {
                Id = "wreck_ambush",
                Enemy = enemy,
                SuggestedPlayer = player,
                Roster = new List<RosterEntry>
                {
                    new RosterEntry("supply_ship", 1),
                    new RosterEntry("relay", 4),
                    new RosterEntry("battleship", 1),
                    new RosterEntry("frigate", 3),
                    new RosterEntry("pd_ship", 3),
                    new RosterEntry("missile_ship", 3),
                    new RosterEntry("strike_craft", 2),
                },
            };
        }
    }
}
