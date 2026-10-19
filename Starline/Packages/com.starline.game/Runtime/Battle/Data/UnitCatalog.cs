using System.Collections.Generic;

namespace Starline.Battle
{
    /// <summary>
    /// Act 1 (Earth) unit set for the playable prototype. Values move to data files later.
    /// Counters: air beats heavy and artillery, anti-air beats air, heavy beats light,
    /// light beats artillery, artillery beats heavy.
    /// </summary>
    public static class UnitCatalog
    {
        private static Fix R(int num, int den) => Fix.FromRatio(num, den);

        // ---- Player (Morningstar) ------------------------------------------------

        public static readonly UnitDef SupplyShip = new UnitDef
        {
            Id = "supply_ship", DisplayName = "补给母舰", Role = UnitRole.Structure, Size = 2, CommandCost = 0,
            MaxHp = 900, MaxShield = 200, ShieldRegenPerSecond = 10, Radius = R(18, 10), IsSupplySource = true,
        };

        public static readonly UnitDef Relay = new UnitDef
        {
            Id = "relay", DisplayName = "中继站", Role = UnitRole.Structure, Size = 0, CommandCost = 1,
            MaxHp = 160, MaxShield = 40, ShieldRegenPerSecond = 4, Radius = R(6, 10), IsRelay = true,
        };

        public static readonly UnitDef HeavyMech = new UnitDef
        {
            Id = "heavy_mech", DisplayName = "重型机甲", Role = UnitRole.Heavy, Size = 2, CommandCost = 4,
            MaxHp = 700, MaxShield = 200, ShieldRegenPerSecond = 12,
            Damage = 30, FireIntervalSeconds = R(12, 10), Range = 4, Speed = R(22, 10), Radius = R(16, 10),
        };

        public static readonly UnitDef HoverTank = new UnitDef
        {
            Id = "hover_tank", DisplayName = "悬浮战车", Role = UnitRole.Light, CommandCost = 2,
            MaxHp = 260, MaxShield = 60, ShieldRegenPerSecond = 6,
            Damage = 14, FireIntervalSeconds = R(6, 10), Range = 3, Speed = R(45, 10), Radius = R(8, 10),
            PreferredTargets = new[] { UnitRole.Artillery },
        };

        public static readonly UnitDef AntiAirVehicle = new UnitDef
        {
            Id = "aa_vehicle", DisplayName = "防空车", Role = UnitRole.AntiAir, CommandCost = 1,
            MaxHp = 200, MaxShield = 40, ShieldRegenPerSecond = 5,
            Damage = 10, FireIntervalSeconds = R(4, 10), Range = 7, Speed = 3, Radius = R(8, 10),
            MaxAmmo = 40, AmmoRefillSeconds = R(5, 10),
            PreferredTargets = new[] { UnitRole.Air },
        };

        public static readonly UnitDef MissileCar = new UnitDef
        {
            Id = "missile_car", DisplayName = "导弹车", Role = UnitRole.Artillery, CommandCost = 2,
            MaxHp = 160, MaxShield = 30, ShieldRegenPerSecond = 4,
            Damage = 34, FireIntervalSeconds = R(15, 10), Range = 12, Speed = R(25, 10), Radius = R(8, 10),
            MaxAmmo = 12, AmmoRefillSeconds = 1,
            PreferredTargets = new[] { UnitRole.Heavy },
        };

        public static readonly UnitDef Fighter = new UnitDef
        {
            Id = "fighter", DisplayName = "战机", Role = UnitRole.Air, CommandCost = 1,
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
            Id = "hive_node", DisplayName = "虫巢节点", Role = UnitRole.Structure, Size = 0,
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
            Id = "locust", DisplayName = "飞蝗群", Role = UnitRole.Air,
            MaxHp = 110, Damage = 9, FireIntervalSeconds = R(5, 10), Range = R(25, 10), Speed = 7, Radius = R(6, 10),
            MaxAmmo = 30, AmmoRefillSeconds = R(5, 10), OffGridGraceSeconds = 10,
            PreferredTargets = new[] { UnitRole.Structure, UnitRole.Artillery },
        };

        public static readonly UnitDef AcidTurret = new UnitDef
        {
            Id = "acid_turret", DisplayName = "酸液炮台", Role = UnitRole.Artillery,
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

        public static readonly IReadOnlyList<UnitDef> All = new[]
        {
            SupplyShip, Relay, HeavyMech, HoverTank, AntiAirVehicle, MissileCar, Fighter,
            Hive, HiveNode, Ripper, Crawler, Locust, AcidTurret, BroodMother,
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
