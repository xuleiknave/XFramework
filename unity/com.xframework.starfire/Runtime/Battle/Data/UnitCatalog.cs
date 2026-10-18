using System.Collections.Generic;

namespace XFramework.Battle
{
    /// <summary>
    /// P0 prototype unit set: a fighter / escort / battleship counter triangle.
    /// Fighters beat battleships, battleships beat escorts, escorts beat fighters.
    /// Values will move to data files in P2.
    /// </summary>
    public static class UnitCatalog
    {
        public static readonly UnitDef Fighter = new UnitDef
        {
            Id = "fighter",
            DisplayName = "战机群",
            Role = UnitRole.Fighter,
            MaxHp = 60,
            MaxShield = 20,
            ShieldRegenPerSecond = 4,
            Damage = 6,
            FireIntervalSeconds = Fix.Half,
            Range = 4,
            Speed = 9,
            Radius = Fix.FromRatio(6, 10),
            SightRange = 18,
        };

        public static readonly UnitDef Escort = new UnitDef
        {
            Id = "escort",
            DisplayName = "护卫舰",
            Role = UnitRole.Escort,
            MaxHp = 140,
            MaxShield = 40,
            ShieldRegenPerSecond = 6,
            Damage = 8,
            FireIntervalSeconds = Fix.Half,
            Range = 7,
            Speed = 5,
            Radius = 1,
            SightRange = 18,
        };

        public static readonly UnitDef Battleship = new UnitDef
        {
            Id = "battleship",
            DisplayName = "战列舰",
            Role = UnitRole.Heavy,
            MaxHp = 500,
            MaxShield = 150,
            ShieldRegenPerSecond = 10,
            Damage = 40,
            FireIntervalSeconds = 2,
            Range = 14,
            Speed = Fix.FromRatio(5, 2),
            Radius = 2,
            SightRange = 20,
            HeavySalvoEvery = 5,
        };

        public static readonly IReadOnlyList<UnitDef> All = new[] { Fighter, Escort, Battleship };

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
