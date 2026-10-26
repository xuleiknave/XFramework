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
        /// <summary>Battlefield environment and tiles; null for plain ground (GDD 5.5).</summary>
        public BattleTerrain Terrain;

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
            Terrain = Terrain,
        };
    }
}
