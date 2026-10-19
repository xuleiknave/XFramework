namespace Starline.Battle
{
    /// <summary>Damage multipliers by attacker role versus target role (GDD 5.5).</summary>
    public static class CounterTable
    {
        private static Fix F(int tenths) => Fix.FromRatio(tenths, 10);

        // Rows: attacker. Columns: target, in UnitRole order
        // Light, Heavy, Air, AntiAir, Artillery, Structure.
        private static readonly Fix[,] Multipliers =
        {
            /* Light     */ { F(10), F(5),  F(5),  F(12), F(18), F(12) },
            /* Heavy     */ { F(18), F(10), F(3),  F(18), F(10), F(12) },
            /* Air       */ { F(10), F(18), F(10), F(5),  F(18), F(15) },
            /* AntiAir   */ { F(6),  F(4),  F(25), F(10), F(6),  F(8) },
            /* Artillery */ { F(10), F(18), F(3),  F(10), F(10), F(15) },
            /* Structure */ { F(0),  F(0),  F(0),  F(0),  F(0),  F(0) },
        };

        public static Fix Get(UnitRole attacker, UnitRole target) =>
            Multipliers[(int)attacker, (int)target];
    }
}
