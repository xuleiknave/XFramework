using System.Collections.Generic;

namespace Starline.Battle
{
    /// <summary>Deployment rules shared by the editor UI, the AI and the battle.</summary>
    public static class FormationRules
    {
        /// <summary>Can this unit be placed with its anchor at (depth, row) without overlapping?</summary>
        public static bool CanPlace(Formation formation, UnitDef def, int depth, int row, Placement ignore = null) =>
            CanPlace(formation, def, depth, row, null, BoardLayout.PlayerSide, ignore);

        /// <summary>Same, also respecting the terrain: no units on blocking tiles, no crystals beside drained tiles.</summary>
        public static bool CanPlace(Formation formation, UnitDef def, int depth, int row, BattleTerrain terrain, int side, Placement ignore = null)
        {
            if (def.IsRelay)
            {
                if (!BoardLayout.IsIntersectionInside(depth, row))
                {
                    return false;
                }
                if (terrain != null && !terrain.AllowsRelay(side, depth, row))
                {
                    return false;
                }
                Placement existing = formation.RelayAt(depth, row);
                return existing == null || existing == ignore;
            }

            int span = def.Size <= 1 ? 1 : def.Size;
            for (int d = depth; d < depth + span; d++)
            {
                for (int r = row; r < row + span; r++)
                {
                    if (!BoardLayout.IsCellInside(d, r))
                    {
                        return false;
                    }
                    if (terrain != null && !terrain.IsDeployable(side, d, r))
                    {
                        return false;
                    }
                    Placement occupant = formation.UnitAt(d, r);
                    if (occupant != null && occupant != ignore)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>Returns human-readable problems; an empty list means the formation is valid.</summary>
        public static List<string> Validate(Formation formation, int commandLimit, BattleTerrain terrain = null, int side = BoardLayout.PlayerSide)
        {
            var errors = new List<string>();
            int sources = 0;
            int combatants = 0;
            var probe = new Formation();

            foreach (Placement p in formation.Placements)
            {
                UnitDef def = p.Def;
                if (def.IsSupplySource)
                {
                    sources++;
                }
                if (def.HasWeapon)
                {
                    combatants++;
                }
                if (!CanPlace(probe, def, p.Depth, p.Row, terrain, side))
                {
                    errors.Add(def.DisplayName + " 的位置无效或与其他单位重叠");
                }
                probe.Placements.Add(p);
            }

            if (sources != 1)
            {
                errors.Add("需要且只能有 1 个魔网源头");
            }
            if (combatants == 0)
            {
                errors.Add("至少需要上阵 1 名作战角色");
            }
            int used = formation.CommandPointsUsed();
            if (used > commandLimit)
            {
                errors.Add("指挥点超出上限（" + used + " / " + commandLimit + "）");
            }
            return errors;
        }
    }
}
