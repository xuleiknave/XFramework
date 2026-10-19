using System.Collections.Generic;
using Starline.Battle;

namespace Starline.Expedition
{
    /// <summary>A ship the player owns during an expedition.</summary>
    public sealed class OwnedShip
    {
        public int Id;
        public string UnitId;
        public int Star = 1;

        public UnitDef Def => UnitCatalog.Get(UnitId);
    }

    /// <summary>Result of adding a ship: the ship that ended up in the fleet, and any merges on the way.</summary>
    public sealed class AddShipResult
    {
        public OwnedShip Ship;
        /// <summary>Star levels reached by merging, in order (empty when nothing merged).</summary>
        public readonly List<int> MergedTo = new List<int>();
    }

    /// <summary>
    /// The expedition fleet (GDD 4.5): every owned ship, plus the formation the player keeps
    /// between battles. Three copies of the same ship at the same star merge into one of the
    /// next star, and the merged ship takes over one of their board positions.
    /// </summary>
    public sealed class Fleet
    {
        public readonly List<OwnedShip> Ships = new List<OwnedShip>();
        public Formation Formation = new Formation();
        /// <summary>Relay beacons available for deployment.</summary>
        public int Relays;

        private int _nextId = 1;

        public OwnedShip Get(int shipId)
        {
            foreach (OwnedShip ship in Ships)
            {
                if (ship.Id == shipId)
                {
                    return ship;
                }
            }
            return null;
        }

        public bool IsDeployed(int shipId) => Formation.FindShip(shipId) != null;

        public int RelaysLeft => Relays - Formation.CountRelays();

        /// <summary>Adds a ship and performs any merges it completes.</summary>
        public AddShipResult Add(string unitId, int star = 1)
        {
            var result = new AddShipResult();
            var ship = new OwnedShip { Id = _nextId++, UnitId = unitId, Star = star };
            Ships.Add(ship);

            while (ship.Star < UnitDef.MaxStar)
            {
                List<OwnedShip> group = Ships.FindAll(s => s.UnitId == ship.UnitId && s.Star == ship.Star);
                if (group.Count < 3)
                {
                    break;
                }
                // Prefer keeping deployed copies so the merged ship stays on the board.
                group.Sort((a, b) => (IsDeployed(b.Id) ? 1 : 0) - (IsDeployed(a.Id) ? 1 : 0));
                group.RemoveRange(3, group.Count - 3);

                var merged = new OwnedShip { Id = _nextId++, UnitId = ship.UnitId, Star = ship.Star + 1 };
                Placement keep = null;
                foreach (OwnedShip part in group)
                {
                    Placement p = Formation.FindShip(part.Id);
                    if (p != null)
                    {
                        if (keep == null)
                        {
                            keep = p;
                        }
                        else
                        {
                            Formation.Placements.Remove(p);
                        }
                    }
                    Ships.Remove(part);
                }
                if (keep != null)
                {
                    keep.ShipId = merged.Id;
                    keep.Star = merged.Star;
                }
                Ships.Add(merged);
                result.MergedTo.Add(merged.Star);
                ship = merged;
            }
            result.Ship = ship;
            return result;
        }

        /// <summary>Places a ship on the board.</summary>
        public void Deploy(OwnedShip ship, int depth, int row)
        {
            Formation.Placements.Add(new Placement(ship.UnitId, depth, row, ship.Star, ship.Id));
        }

        /// <summary>How many ships of this type and star the fleet has (3 merge into one).</summary>
        public int CopiesOwned(string unitId, int star)
        {
            int n = 0;
            foreach (OwnedShip ship in Ships)
            {
                if (ship.UnitId == unitId && ship.Star == star)
                {
                    n++;
                }
            }
            return n;
        }
    }
}
