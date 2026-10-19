using Starline.Battle;

namespace Starline.Expedition
{
    /// <summary>
    /// Simple placement heuristics: where a newly gained ship or relay goes by default, and
    /// the "auto deploy" used by tests. The player can always move things afterwards.
    /// </summary>
    public static class AutoDeploy
    {
        /// <summary>Puts a ship on its preferred row and depth, inside supply coverage if possible.</summary>
        public static bool PlaceShip(Fleet fleet, OwnedShip ship, int commandLimit)
        {
            Formation formation = fleet.Formation;
            UnitDef def = ship.Def;
            if (fleet.IsDeployed(ship.Id) || formation.CommandPointsUsed() + def.CommandCost > commandLimit)
            {
                return false;
            }

            SupplyNetwork network = SupplyNetwork.ForFormation(formation, BoardLayout.PlayerSide);
            int preferred = PreferredDepth(def.Role);
            int bestScore = int.MaxValue;
            int bestDepth = -1, bestRow = -1;
            for (int d = 0; d < BoardLayout.Depth; d++)
            {
                for (int r = 0; r < BoardLayout.Width; r++)
                {
                    if (!FormationRules.CanPlace(formation, def, d, r))
                    {
                        continue;
                    }
                    var probe = new Placement(def.Id, d, r);
                    int score = Abs(d - preferred) * 4;
                    if (!network.CoversPlacement(probe, BoardLayout.PlayerSide))
                    {
                        score += def.IsCraft ? 2 : 20;
                    }
                    // Strike craft fly from the wings; everything else holds the centre.
                    int span = def.Size <= 1 ? 1 : def.Size;
                    int offCentre = Abs(2 * r + span - BoardLayout.Width) / 2;
                    score += def.IsCraft ? (BoardLayout.Width / 2 - offCentre) * 2 : offCentre;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestDepth = d;
                        bestRow = r;
                    }
                }
            }
            if (bestDepth < 0)
            {
                return false;
            }
            fleet.Deploy(ship, bestDepth, bestRow);
            return true;
        }

        /// <summary>Adds a relay where it brings the most deployed ships (then cells) into coverage.</summary>
        public static bool PlaceRelay(Fleet fleet, int commandLimit)
        {
            Formation formation = fleet.Formation;
            UnitDef relay = UnitCatalog.Relay;
            if (fleet.RelaysLeft <= 0 || formation.CommandPointsUsed() + relay.CommandCost > commandLimit)
            {
                return false;
            }

            int baseScore = CoverageScore(formation, SupplyNetwork.ForFormation(formation, BoardLayout.PlayerSide));
            int bestScore = baseScore;
            int bestD = -1, bestR = -1;
            for (int d = 0; d <= BoardLayout.Depth; d++)
            {
                for (int r = 0; r <= BoardLayout.Width; r++)
                {
                    if (!FormationRules.CanPlace(formation, relay, d, r))
                    {
                        continue;
                    }
                    var placement = new Placement(relay.Id, d, r);
                    formation.Placements.Add(placement);
                    SupplyNetwork network = SupplyNetwork.ForFormation(formation, BoardLayout.PlayerSide);
                    int score = IsConnected(network, BoardLayout.Intersection(BoardLayout.PlayerSide, d, r))
                        ? CoverageScore(formation, network) : int.MinValue;
                    formation.Placements.Remove(placement);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestD = d;
                        bestR = r;
                    }
                }
            }
            if (bestD < 0)
            {
                return false;
            }
            formation.Add(relay.Id, bestD, bestR);
            return true;
        }

        /// <summary>Deploys every reserve ship and relay that still fits.</summary>
        public static void FillFormation(Fleet fleet, int commandLimit)
        {
            // Relays first so ships can choose covered cells, then ships from the most expensive down.
            while (PlaceRelay(fleet, commandLimit))
            {
            }
            var reserve = fleet.Ships.FindAll(s => !fleet.IsDeployed(s.Id) && s.Def.HasWeapon);
            reserve.Sort((a, b) => b.Def.CommandCost - a.Def.CommandCost);
            foreach (OwnedShip ship in reserve)
            {
                PlaceShip(fleet, ship, commandLimit);
            }
        }

        private static int CoverageScore(Formation formation, SupplyNetwork network)
        {
            int score = 0;
            foreach (Placement p in formation.Placements)
            {
                if (p.Def.HasWeapon && network.CoversPlacement(p, BoardLayout.PlayerSide))
                {
                    score += 100;
                }
            }
            for (int d = 0; d < BoardLayout.Depth; d++)
            {
                for (int r = 0; r < BoardLayout.Width; r++)
                {
                    if (network.IsCovered(BoardLayout.FootprintCenter(BoardLayout.PlayerSide, d, r, 1)))
                    {
                        score++;
                    }
                }
            }
            return score;
        }

        private static bool IsConnected(SupplyNetwork network, FixVec2 position)
        {
            foreach (SupplyNode node in network.Nodes)
            {
                if (node.Position == position)
                {
                    return node.Connected;
                }
            }
            return false;
        }

        private static int PreferredDepth(UnitRole role)
        {
            switch (role)
            {
                case UnitRole.Heavy:
                case UnitRole.Light:
                case UnitRole.Craft:
                    return 0;
                case UnitRole.PointDefense:
                    return 2;
                case UnitRole.Artillery:
                    return 3;
                default:
                    return 4;
            }
        }

        private static int Abs(int v) => v < 0 ? -v : v;
    }
}
