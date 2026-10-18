namespace XFramework.Battle
{
    /// <summary>Damage multipliers by attacker role versus target role (GDD 5.5).</summary>
    public static class CounterTable
    {
        // Rows: attacker, columns: target, in order Fighter, Escort, Heavy.
        private static readonly Fix[,] Multipliers =
        {
            //            vs Fighter            vs Escort          vs Heavy
            /* Fighter */ { Fix.One,             Fix.Half,          Fix.FromInt(2) },
            /* Escort  */ { Fix.FromRatio(5, 2), Fix.One,           Fix.Half },
            /* Heavy   */ { Fix.FromRatio(1, 4), Fix.FromInt(2),    Fix.One },
        };

        public static Fix Get(UnitRole attacker, UnitRole target) =>
            Multipliers[(int)attacker, (int)target];
    }
}
