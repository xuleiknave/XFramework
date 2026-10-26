namespace Starline.Battle
{
    /// <summary>
    /// Deployed strength ("上阵战力", replay-server.md section 3): what the lowest-power
    /// leaderboard ranks by. Computed from the formation, never taken from the client.
    /// Weights come from power.json.
    /// </summary>
    public static class PowerRating
    {
        private static Fix _durabilityWeight = Fix.One;
        private static Fix _dpsWeight = Fix.One;
        private static bool _countStructures;

        public static void Load(JsonValue root)
        {
            _durabilityWeight = root.FixValue("durability", Fix.One);
            _dpsWeight = root.FixValue("dps", Fix.One);
            _countStructures = root.Bool("countStructures");
        }

        /// <summary>Power of one placement: (hull + shield) and damage per second, scaled by star and weighted.</summary>
        public static int Of(Placement placement)
        {
            UnitDef def = placement.Def;
            if (def.IsStructure && !_countStructures)
            {
                return 0;
            }
            Fix durability = (def.MaxHp + def.MaxShield) * UnitDef.HpScale(placement.Star);
            Fix dps = def.HasWeapon ? def.Damage * UnitDef.DamageScale(placement.Star) / def.FireIntervalSeconds : Fix.Zero;
            return (durability * _durabilityWeight + dps * _dpsWeight).ToIntFloor();
        }

        public static int Of(Formation formation)
        {
            int total = 0;
            foreach (Placement p in formation.Placements)
            {
                total += Of(p);
            }
            return total;
        }
    }
}
