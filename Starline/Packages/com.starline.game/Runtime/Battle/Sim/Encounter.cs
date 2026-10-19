using System.Collections.Generic;

namespace Starline.Battle
{
    /// <summary>How many copies of a unit the player owns for deployment.</summary>
    public sealed class RosterEntry
    {
        public string UnitId;
        public int Count;

        public RosterEntry(string unitId, int count)
        {
            UnitId = unitId;
            Count = count;
        }

        public UnitDef Def => UnitCatalog.Get(UnitId);
    }

    /// <summary>A single battle node: the enemy formation plus what the player may deploy.</summary>
    public sealed class Encounter
    {
        public string Name;
        /// <summary>One or two sentences shown before deployment.</summary>
        public string Briefing;
        public Formation Enemy;
        public List<RosterEntry> Roster = new List<RosterEntry>();
        public int CommandLimit = 22;
        public int IntelPoints = 3;
        public int TimeLimitSeconds = 90;
        /// <summary>A sensible starting formation the player can edit.</summary>
        public Formation SuggestedPlayer;

        public int OwnedCount(string unitId)
        {
            foreach (RosterEntry entry in Roster)
            {
                if (entry.UnitId == unitId)
                {
                    return entry.Count;
                }
            }
            return 0;
        }

        public BattleSetup CreateSetup(Formation player, ulong seed) => new BattleSetup
        {
            Player = player,
            Enemy = Enemy,
            Seed = seed,
            TimeLimitSeconds = TimeLimitSeconds,
        };

        /// <summary>Act 1 elite battle used by the playable prototype (matches the formation mock-up).</summary>
        public static Encounter CreateWreckFieldAmbush()
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
                Name = "精英战：残骸带伏击",
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
