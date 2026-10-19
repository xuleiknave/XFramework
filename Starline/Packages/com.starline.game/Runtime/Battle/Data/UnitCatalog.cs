using System.Collections.Generic;

namespace Starline.Battle
{
    /// <summary>
    /// Act 1 (Frontier) ship set for the playable prototype. Values move to data files later.
    /// Counters: strike craft beat heavy ships and artillery, point defence beats strike craft,
    /// heavy beats light, light beats artillery, artillery beats heavy.
    /// </summary>
    public static class UnitCatalog
    {
        private static Fix R(int num, int den) => Fix.FromRatio(num, den);

        // ---- Player (Morningstar Fleet) ------------------------------------------

        public static readonly UnitDef SupplyShip = new UnitDef
        {
            Id = "supply_ship", DisplayName = "补给母舰", Role = UnitRole.Structure, Size = 2, CommandCost = 0,
            MaxHp = 900, MaxShield = 200, ShieldRegenPerSecond = 10, Radius = R(18, 10), IsSupplySource = true,
        };

        public static readonly UnitDef Relay = new UnitDef
        {
            Id = "relay", DisplayName = "中继信标", Role = UnitRole.Structure, Size = 0, CommandCost = 1,
            MaxHp = 160, MaxShield = 40, ShieldRegenPerSecond = 4, Radius = R(6, 10), IsRelay = true,
        };

        public static readonly UnitDef Battleship = new UnitDef
        {
            Id = "battleship", DisplayName = "战列舰", Role = UnitRole.Heavy, Size = 2, CommandCost = 4,
            MaxHp = 700, MaxShield = 200, ShieldRegenPerSecond = 12,
            Damage = 30, FireIntervalSeconds = R(12, 10), Range = 4, Speed = R(22, 10), Radius = R(16, 10),
        };

        public static readonly UnitDef Frigate = new UnitDef
        {
            Id = "frigate", DisplayName = "护卫舰", Role = UnitRole.Light, CommandCost = 2,
            MaxHp = 260, MaxShield = 60, ShieldRegenPerSecond = 6,
            Damage = 14, FireIntervalSeconds = R(6, 10), Range = 3, Speed = R(45, 10), Radius = R(8, 10),
            PreferredTargets = new[] { UnitRole.Artillery },
        };

        public static readonly UnitDef PointDefenseShip = new UnitDef
        {
            Id = "pd_ship", DisplayName = "点防御舰", Role = UnitRole.PointDefense, CommandCost = 1,
            MaxHp = 200, MaxShield = 40, ShieldRegenPerSecond = 5,
            Damage = 10, FireIntervalSeconds = R(4, 10), Range = 7, Speed = 3, Radius = R(8, 10),
            MaxAmmo = 40, AmmoRefillSeconds = R(5, 10),
            PreferredTargets = new[] { UnitRole.Craft },
        };

        public static readonly UnitDef MissileShip = new UnitDef
        {
            Id = "missile_ship", DisplayName = "导弹舰", Role = UnitRole.Artillery, CommandCost = 2,
            MaxHp = 160, MaxShield = 30, ShieldRegenPerSecond = 4,
            Damage = 34, FireIntervalSeconds = R(15, 10), Range = 12, Speed = R(25, 10), Radius = R(8, 10),
            MaxAmmo = 12, AmmoRefillSeconds = 1,
            PreferredTargets = new[] { UnitRole.Heavy },
        };

        public static readonly UnitDef StrikeCraft = new UnitDef
        {
            Id = "strike_craft", DisplayName = "舰载机群", Role = UnitRole.Craft, CommandCost = 1,
            MaxHp = 120, MaxShield = 30, ShieldRegenPerSecond = 4,
            Damage = 9, FireIntervalSeconds = R(5, 10), Range = R(25, 10), Speed = 7, Radius = R(6, 10),
            MaxAmmo = 30, AmmoRefillSeconds = R(5, 10), OffGridGraceSeconds = 10,
            PreferredTargets = new[] { UnitRole.Structure, UnitRole.Artillery },
        };

        // ---- Enemy (Swarm) ------------------------------------------------------

        public static readonly UnitDef Hive = new UnitDef
        {
            Id = "hive", DisplayName = "母巢", Role = UnitRole.Structure, Size = 2,
            MaxHp = 900, MaxShield = 200, ShieldRegenPerSecond = 10, Radius = R(18, 10), IsSupplySource = true,
        };

        public static readonly UnitDef HiveNode = new UnitDef
        {
            Id = "hive_node", DisplayName = "巢链节点", Role = UnitRole.Structure, Size = 0,
            MaxHp = 160, MaxShield = 40, ShieldRegenPerSecond = 4, Radius = R(6, 10), IsRelay = true,
        };

        public static readonly UnitDef Ripper = new UnitDef
        {
            Id = "ripper", DisplayName = "撕裂者", Role = UnitRole.Light,
            MaxHp = 220, MaxShield = 20, ShieldRegenPerSecond = 4,
            Damage = 16, FireIntervalSeconds = R(6, 10), Range = R(18, 10), Speed = 5, Radius = R(8, 10),
            PreferredTargets = new[] { UnitRole.Artillery },
        };

        public static readonly UnitDef Crawler = new UnitDef
        {
            Id = "crawler", DisplayName = "甲壳兽", Role = UnitRole.Heavy,
            MaxHp = 480, MaxShield = 60, ShieldRegenPerSecond = 6,
            Damage = 20, FireIntervalSeconds = 1, Range = R(25, 10), Speed = R(25, 10), Radius = 1,
        };

        public static readonly UnitDef Locust = new UnitDef
        {
            Id = "locust", DisplayName = "飞蝗群", Role = UnitRole.Craft,
            MaxHp = 110, Damage = 9, FireIntervalSeconds = R(5, 10), Range = R(25, 10), Speed = 7, Radius = R(6, 10),
            MaxAmmo = 30, AmmoRefillSeconds = R(5, 10), OffGridGraceSeconds = 10,
            PreferredTargets = new[] { UnitRole.Structure, UnitRole.Artillery },
        };

        public static readonly UnitDef AcidTurret = new UnitDef
        {
            Id = "acid_turret", DisplayName = "酸液炮巢", Role = UnitRole.Artillery,
            MaxHp = 300, MaxShield = 40, ShieldRegenPerSecond = 4,
            Damage = 30, FireIntervalSeconds = R(16, 10), Range = 13, Radius = 1,
            MaxAmmo = 10, AmmoRefillSeconds = R(12, 10),
            PreferredTargets = new[] { UnitRole.Heavy },
        };

        public static readonly UnitDef BroodMother = new UnitDef
        {
            Id = "brood_mother", DisplayName = "母体", Role = UnitRole.Heavy, Size = 2,
            MaxHp = 1100, MaxShield = 200, ShieldRegenPerSecond = 10,
            Damage = 40, FireIntervalSeconds = R(15, 10), Range = 4, Speed = R(18, 10), Radius = R(16, 10),
            HeavySalvoEvery = 4,
        };

        /// <summary>Act 1 boss: a fortress the Swarm grew out of old Starline wreckage. Static, with a heavy salvo.</summary>
        public static readonly UnitDef WreckFortress = new UnitDef
        {
            Id = "wreck_fortress", DisplayName = "残骸要塞", Role = UnitRole.Heavy, Size = 2,
            MaxHp = 2000, MaxShield = 400, ShieldRegenPerSecond = 12,
            Damage = 34, FireIntervalSeconds = R(12, 10), Range = 9, Radius = R(18, 10),
            HeavySalvoEvery = 3,
        };

        public static readonly IReadOnlyList<UnitDef> All = new[]
        {
            SupplyShip, Relay, Battleship, Frigate, PointDefenseShip, MissileShip, StrikeCraft,
            Hive, HiveNode, Ripper, Crawler, Locust, AcidTurret, BroodMother, WreckFortress,
        };

        public static UnitDef Get(string id)
        {
            foreach (UnitDef def in All)
            {
                if (def.Id == id)
                {
                    return def;
                }
            }
            throw new KeyNotFoundException("Unknown unit id: " + id);
        }
    }
}
