namespace XFramework.Battle
{
    /// <summary>Runtime state of one unit in a battle.</summary>
    public sealed class Unit
    {
        public int Id;
        public int Side;
        public UnitDef Def;

        public FixVec2 Position;
        public Fix Hp;
        public Fix Shield;

        /// <summary>Enemy currently targeted (-1 for none).</summary>
        public int TargetId = -1;
        public int RetargetTicks;
        public int CooldownTicks;
        public int ShotsFired;
        /// <summary>Tick of the last damage taken, used to delay shield regeneration.</summary>
        public int LastDamagedTick = int.MinValue / 2;

        public int Ammo;
        public int AmmoRefillTicks;

        /// <summary>Inside a connected node's coverage this tick.</summary>
        public bool InCoverage;
        /// <summary>Counts as supplied: in coverage, or a mobile unit still within its grace period.</summary>
        public bool Supplied;
        /// <summary>Consecutive ticks spent outside coverage.</summary>
        public int OffGridTicks;
        /// <summary>Total ticks spent unsupplied (battle statistics).</summary>
        public int UnsuppliedTicksTotal;
        /// <summary>Set once the unit has run out of ammunition at least once.</summary>
        public bool RanDry;

        public bool IsAlive => Hp > Fix.Zero;
        public bool HasAmmo => Def.MaxAmmo == 0 || Ammo > 0;
    }
}
