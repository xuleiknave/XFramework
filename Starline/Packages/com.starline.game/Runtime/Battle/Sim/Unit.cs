namespace Starline.Battle
{
    /// <summary>Runtime state of one unit in a battle.</summary>
    public sealed class Unit
    {
        public int Id;
        public int Side;
        public UnitDef Def;
        public int Star = 1;
        /// <summary>Star-scaled maximums and damage (see <see cref="UnitDef.HpScale"/>).</summary>
        public Fix MaxHp;
        public Fix MaxShield;
        public Fix Damage;

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
        /// <summary>Progress towards the next round, in ticks (terrain can speed it up or slow it down).</summary>
        public Fix AmmoRefillProgress;

        /// <summary>Terrain modifiers for the tile the unit stands on (see <see cref="TerrainModifiers"/>).</summary>
        public TerrainModifiers Terrain = TerrainModifiers.Identity;
        /// <summary>The tile <see cref="Terrain"/> was computed for (null for open ground).</summary>
        public TileDef Tile;
        /// <summary>Weapon range after terrain.</summary>
        public Fix Range => Terrain.ApplyRange(Def.Range);

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
