using UnityEngine;
using Starline.Battle;

namespace Starline.Client
{
    /// <summary>Shared player-facing wording for roles and ship stats.</summary>
    public static class Wording
    {
        public static string RoleName(UnitRole role)
        {
            switch (role)
            {
                case UnitRole.Light: return Loc.Ui("role.light");
                case UnitRole.Heavy: return Loc.Ui("role.heavy");
                case UnitRole.Craft: return Loc.Ui("role.craft");
                case UnitRole.PointDefense: return Loc.Ui("role.point_defense");
                case UnitRole.Artillery: return Loc.Ui("role.artillery");
                default: return Loc.Ui("role.structure");
            }
        }

        /// <summary>Hull, shield, damage per second, range and ammunition at a star level.</summary>
        public static string ShipStats(UnitDef def, int star)
        {
            float hp = (def.MaxHp * UnitDef.HpScale(star)).ToFloat();
            float shield = (def.MaxShield * UnitDef.HpScale(star)).ToFloat();
            float dps = (def.Damage * UnitDef.DamageScale(star)).ToFloat() / def.FireIntervalSeconds.ToFloat();
            string text = Loc.Ui("stats.survival", Mathf.RoundToInt(hp), Mathf.RoundToInt(shield))
                + "\n" + Loc.Ui("stats.attack", dps.ToString("0"), (def.Range.ToFloat() / BoardLayout.CellSize.ToFloat()).ToString("0.#"));
            if (def.MaxAmmo > 0)
            {
                text += "\n" + Loc.Ui("stats.ammo", def.MaxAmmo);
            }
            if (def.OffGridGraceSeconds > Fix.Zero)
            {
                text += "\n" + Loc.Ui("stats.off_grid", def.OffGridGraceSeconds.ToIntFloor());
            }
            return text;
        }
    }
}
