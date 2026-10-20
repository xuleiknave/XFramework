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
                case UnitRole.Light: return "轻装 · 克远程";
                case UnitRole.Heavy: return "重装 · 克轻装";
                case UnitRole.Craft: return "飞骑 · 克重装、远程";
                case UnitRole.PointDefense: return "对空 · 克飞骑";
                case UnitRole.Artillery: return "远程 · 克重装";
                default: return "魔网";
            }
        }

        /// <summary>Hull, shield, damage per second, range and ammunition at a star level.</summary>
        public static string ShipStats(UnitDef def, int star)
        {
            float hp = (def.MaxHp * UnitDef.HpScale(star)).ToFloat();
            float shield = (def.MaxShield * UnitDef.HpScale(star)).ToFloat();
            float dps = (def.Damage * UnitDef.DamageScale(star)).ToFloat() / def.FireIntervalSeconds.ToFloat();
            string text = "生命 " + Mathf.RoundToInt(hp) + "  护盾 " + Mathf.RoundToInt(shield)
                + "\n每秒伤害 " + dps.ToString("0") + "  射程 " + (def.Range.ToFloat() / BoardLayout.CellSize.ToFloat()).ToString("0.#") + " 格";
            if (def.MaxAmmo > 0)
            {
                text += "\n法力 " + def.MaxAmmo + "（断开魔网后用完就停手）";
            }
            if (def.OffGridGraceSeconds > Fix.Zero)
            {
                text += "\n可离网 " + def.OffGridGraceSeconds.ToIntFloor() + " 秒";
            }
            return text;
        }
    }
}
