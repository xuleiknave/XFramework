using System.Collections.Generic;
using Starline.Battle;

namespace Starline.Expedition
{
    /// <summary>Result of <see cref="FormationCopy.Apply"/>.</summary>
    public sealed class FormationCopyResult
    {
        /// <summary>Characters from the source that were placed with one of the player's own.</summary>
        public int Placed;
        /// <summary>Source placements left empty: the player has no such character, or the command limit ran out.</summary>
        public readonly List<Placement> Missing = new List<Placement>();
        /// <summary>Placed with a different star than the source used.</summary>
        public readonly List<Placement> StarDiffers = new List<Placement>();
        public int RelaysPlaced;
        public int RelaysMissing;
    }

    /// <summary>
    /// "照抄布阵" (replay-server.md section 2): rebuilds the fleet's formation from someone
    /// else's (a replay's) using the player's own characters. Positions are kept; a character
    /// the player does not have leaves its cell empty for the player to fill.
    /// </summary>
    public static class FormationCopy
    {
        public static FormationCopyResult Apply(Fleet fleet, Formation source, int commandLimit)
        {
            var result = new FormationCopyResult();
            Formation formation = fleet.Formation;
            formation.Placements.Clear();

            // The supply source first: every formation needs exactly one and it costs nothing.
            Placement sourceTower = source.Source();
            if (sourceTower != null)
            {
                formation.Add(sourceTower.UnitId, sourceTower.Depth, sourceTower.Row);
            }

            // Characters next (they matter more than relays when command points are short), in source order.
            var used = new HashSet<int>();
            foreach (Placement p in source.Placements)
            {
                if (p.Def.IsStructure)
                {
                    continue;
                }
                OwnedShip ship = PickShip(fleet, p, used);
                if (ship == null
                    || !FormationRules.CanPlace(formation, ship.Def, p.Depth, p.Row)
                    || formation.CommandPointsUsed() + ship.Def.CommandCost > commandLimit)
                {
                    result.Missing.Add(p);
                    continue;
                }
                used.Add(ship.Id);
                fleet.Deploy(ship, p.Depth, p.Row);
                result.Placed++;
                if (ship.Star != p.Star)
                {
                    result.StarDiffers.Add(p);
                }
            }

            foreach (Placement p in source.Placements)
            {
                if (!p.IsRelay)
                {
                    continue;
                }
                if (fleet.RelaysLeft > 0
                    && FormationRules.CanPlace(formation, p.Def, p.Depth, p.Row)
                    && formation.CommandPointsUsed() + p.Def.CommandCost <= commandLimit)
                {
                    formation.Add(p.UnitId, p.Depth, p.Row);
                    result.RelaysPlaced++;
                }
                else
                {
                    result.RelaysMissing++;
                }
            }
            return result;
        }

        /// <summary>An unused owned copy of the character: the same star if possible, otherwise the highest.</summary>
        private static OwnedShip PickShip(Fleet fleet, Placement wanted, HashSet<int> used)
        {
            OwnedShip best = null;
            foreach (OwnedShip ship in fleet.Ships)
            {
                if (ship.UnitId != wanted.UnitId || used.Contains(ship.Id))
                {
                    continue;
                }
                if (ship.Star == wanted.Star)
                {
                    return ship;
                }
                if (best == null || ship.Star > best.Star)
                {
                    best = ship;
                }
            }
            return best;
        }
    }
}
