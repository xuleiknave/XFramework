using System.Collections.Generic;

namespace XFramework.Battle
{
    public sealed class UnitSpawn
    {
        public string UnitId;
        public int Side;
        public FixVec2 Position;

        public UnitSpawn(string unitId, int side, FixVec2 position)
        {
            UnitId = unitId;
            Side = side;
            Position = position;
        }
    }

    /// <summary>Initial setup of a battle.</summary>
    public sealed class Scenario
    {
        public const int PlayerSide = 0;
        public const int EnemySide = 1;

        public string Name;
        public ulong Seed = 1;
        /// <summary>Half extents of the playable area, centred on the origin.</summary>
        public FixVec2 HalfSize = new FixVec2(40, 25);
        /// <summary>Battle ends in a draw-by-score after this many seconds.</summary>
        public int TimeLimitSeconds = 180;
        public List<UnitSpawn> Spawns = new List<UnitSpawn>();

        public Scenario Add(string unitId, int side, int x, int y)
        {
            Spawns.Add(new UnitSpawn(unitId, side, new FixVec2(x, y)));
            return this;
        }

        /// <summary>P0 test map: two mirrored fleets of 4 fighters, 3 escorts, 1 battleship.</summary>
        public static Scenario CreateP0Skirmish(ulong seed = 1)
        {
            var s = new Scenario { Name = "P0 遭遇战", Seed = seed };
            AddFleet(s, Scenario.PlayerSide, -1);
            AddFleet(s, Scenario.EnemySide, 1);
            return s;
        }

        private static void AddFleet(Scenario s, int side, int dir)
        {
            s.Add("battleship", side, 32 * dir, 0);
            s.Add("escort", side, 26 * dir, -6);
            s.Add("escort", side, 26 * dir, 0);
            s.Add("escort", side, 26 * dir, 6);
            s.Add("fighter", side, 22 * dir, -9);
            s.Add("fighter", side, 22 * dir, -3);
            s.Add("fighter", side, 22 * dir, 3);
            s.Add("fighter", side, 22 * dir, 9);
        }
    }
}
