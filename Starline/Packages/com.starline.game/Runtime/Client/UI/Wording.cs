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
                case UnitRole.Light: return "轻型舰 · 克远程";
                case UnitRole.Heavy: return "重型舰 · 克轻型";
                case UnitRole.Craft: return "舰载机 · 克重型、远程";
                case UnitRole.PointDefense: return "点防御 · 克舰载机";
                case UnitRole.Artillery: return "远程舰 · 克重型";
                default: return "补给";
            }
        }

        /// <summary>Hull, shield, damage per second, range and ammunition at a star level.</summary>
        public static string ShipStats(UnitDef def, int star)
        {
            float hp = (def.MaxHp * UnitDef.HpScale(star)).ToFloat();
            float shield = (def.MaxShield * UnitDef.HpScale(star)).ToFloat();
            float dps = (def.Damage * UnitDef.DamageScale(star)).ToFloat() / def.FireIntervalSeconds.ToFloat();
            string text = "结构 " + Mathf.RoundToInt(hp) + "  护盾 " + Mathf.RoundToInt(shield)
                + "\n每秒伤害 " + dps.ToString("0") + "  射程 " + (def.Range.ToFloat() / BoardLayout.CellSize.ToFloat()).ToString("0.#") + " 格";
            if (def.MaxAmmo > 0)
            {
                text += "\n弹药 " + def.MaxAmmo + "（断补给后打完就停火）";
            }
            if (def.OffGridGraceSeconds > Fix.Zero)
            {
                text += "\n可离网 " + def.OffGridGraceSeconds.ToIntFloor() + " 秒";
            }
            return text;
        }
    }
}
