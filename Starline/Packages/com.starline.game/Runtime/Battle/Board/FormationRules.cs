using System.Collections.Generic;

namespace Starline.Battle
{
    /// <summary>Deployment rules shared by the editor UI, the AI and the battle.</summary>
    public static class FormationRules
    {
        /// <summary>Can this unit be placed with its anchor at (depth, row) without overlapping?</summary>
        public static bool CanPlace(Formation formation, UnitDef def, int depth, int row, Placement ignore = null)
        {
            if (def.IsRelay)
            {
                if (!BoardLayout.IsIntersectionInside(depth, row))
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
                    Placement occupant = formation.UnitAt(d, r);
                    if (occupant != null && occupant != ignore)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>Returns the problems as text for the UI; an empty list means the formation is valid.</summary>
        public static List<LocText> Validate(Formation formation, int commandLimit)
        {
            var errors = new List<LocText>();
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
                if (!CanPlace(probe, def, p.Depth, p.Row))
                {
                    errors.Add(new LocText("formation.bad_position", def.Name));
                }
                probe.Placements.Add(p);
            }

            if (sources != 1)
            {
                errors.Add(new LocText("formation.one_source"));
            }
            if (combatants == 0)
            {
                errors.Add(new LocText("formation.no_combatant"));
            }
            int used = formation.CommandPointsUsed();
            if (used > commandLimit)
            {
                errors.Add(new LocText("formation.over_command", used, commandLimit));
            }
            return errors;
        }
    }
}
