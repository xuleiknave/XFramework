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

        /// <summary>A path inside the repository (the folder that holds the Unity project).</summary>
        public static string RepositoryPath(string relative)
        {
            string data = FindDataDirectory();
            var root = new DirectoryInfo(data);
            for (int i = 0; i < 6; i++)
            {
                root = root.Parent;
            }
            return Path.Combine(root.FullName, relative);
        }

        private static string FindDataDirectory()
        {
            const string relative = "Starline/Packages/com.starline.game/Runtime/Resources/StarlineData";
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
                Name = "精英战：古战场伏击",
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
