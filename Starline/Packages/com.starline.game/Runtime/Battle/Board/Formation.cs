using System.Collections.Generic;

namespace Starline.Battle
{
    /// <summary>A unit placed on a cell (or, for relays, on an intersection).</summary>
    public sealed class Placement
    {
        public string UnitId;
        /// <summary>Cell depth for units; depth line for relays.</summary>
        public int Depth;
        /// <summary>Cell row for units; row line for relays.</summary>
        public int Row;
        /// <summary>Star level 1..5 (shards raise it, GDD 4.8; merging three copies during an expedition, up to 3, GDD 4.5).</summary>
        public int Star = 1;
        /// <summary>The owned ship this placement deploys during an expedition (0 = none).</summary>
        public int ShipId;

        public Placement(string unitId, int depth, int row, int star = 1, int shipId = 0)
        {
            UnitId = unitId;
            Depth = depth;
            Row = row;
            Star = star;
            ShipId = shipId;
        }

        public UnitDef Def => UnitCatalog.Get(UnitId);
        public bool IsRelay => Def.IsRelay;

        public Placement Clone() => new Placement(UnitId, Depth, Row, Star, ShipId);
    }

    /// <summary>One side's deployment: every unit and relay node with its board position.</summary>
    public sealed class Formation
    {
        public readonly List<Placement> Placements = new List<Placement>();

        public Formation Add(string unitId, int depth, int row, int star = 1)
        {
            Placements.Add(new Placement(unitId, depth, row, star));
            return this;
        }

        public Placement FindShip(int shipId)
        {
            foreach (Placement p in Placements)
            {
                if (p.ShipId == shipId)
                {
                    return p;
                }
            }
            return null;
        }

        public int CountRelays()
        {
            int n = 0;
            foreach (Placement p in Placements)
            {
                if (p.IsRelay)
                {
                    n++;
                }
            }
            return n;
        }

        public Formation Clone()
        {
            var copy = new Formation();
            foreach (Placement p in Placements)
            {
                copy.Placements.Add(p.Clone());
            }
            return copy;
        }

        public int CommandPointsUsed()
        {
            int total = 0;
            foreach (Placement p in Placements)
            {
                total += p.Def.CommandCost;
            }
            return total;
        }

        public int Count(string unitId)
        {
            int n = 0;
            foreach (Placement p in Placements)
            {
                if (p.UnitId == unitId)
                {
                    n++;
                }
            }
            return n;
        }

        /// <summary>The unit occupying a cell, or null.</summary>
        public Placement UnitAt(int depth, int row)
        {
            foreach (Placement p in Placements)
            {
                if (p.IsRelay)
                {
                    continue;
                }
                int span = p.Def.Size <= 1 ? 1 : p.Def.Size;
                if (depth >= p.Depth && depth < p.Depth + span && row >= p.Row && row < p.Row + span)
                {
                    return p;
                }
            }
            return null;
        }

        public Placement RelayAt(int depthLine, int rowLine)
        {
            foreach (Placement p in Placements)
            {
                if (p.IsRelay && p.Depth == depthLine && p.Row == rowLine)
                {
                    return p;
                }
            }
            return null;
        }

        public Placement Source()
        {
            foreach (Placement p in Placements)
            {
                if (p.Def.IsSupplySource)
                {
                    return p;
                }
            }
            return null;
        }
    }
}
