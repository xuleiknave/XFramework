namespace Starline.Battle
{
    /// <summary>Damage multipliers by attacker role versus target role, loaded from counters.json.</summary>
    public static class CounterTable
    {
        private const int RoleCount = 6;
        private static readonly Fix[,] Multipliers = new Fix[RoleCount, RoleCount];

        public static Fix Get(UnitRole attacker, UnitRole target) =>
            Multipliers[(int)attacker, (int)target];

        internal static void Load(JsonValue root)
        {
            JsonValue roles = root["roles"];
            JsonValue rows = root["multipliers"];
            for (int a = 0; a < roles.Count; a++)
            {
                UnitRole attacker = GameData.ParseEnum<UnitRole>(roles[a]);
                JsonValue row = rows[roles[a].AsString()];
                if (row.Count != roles.Count)
                {
                    throw new System.FormatException(row.Path + "：应有 " + roles.Count + " 个倍率");
                }
                for (int t = 0; t < roles.Count; t++)
                {
                    Multipliers[(int)attacker, (int)GameData.ParseEnum<UnitRole>(roles[t])] = row[t].AsFix();
                }
            }
        }
    }
}
