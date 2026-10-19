namespace XFramework.Battle
{
    /// <summary>Unit role. Drives the counter relationships and targeting preferences.</summary>
    public enum UnitRole
    {
        Light,
        Heavy,
        Air,
        AntiAir,
        Artillery,
        /// <summary>Supply sources and relay nodes: no weapon, never move.</summary>
        Structure,
    }

    /// <summary>
    /// Intrinsic attributes of a unit type (GDD 6.2). Distances are in battle units
    /// (one board cell is <see cref="BoardLayout.CellSize"/> units); times are in seconds.
    /// </summary>
    public sealed class UnitDef
    {
        public string Id;
        public string DisplayName;
        public UnitRole Role;

        /// <summary>Footprint on the board: 1 = one cell, 2 = 2x2 cells, 0 = a grid intersection (relays).</summary>
        public int Size = 1;
        /// <summary>Command points needed to deploy it.</summary>
        public int CommandCost;

        // Survival
        public Fix MaxHp;
        public Fix MaxShield;
        /// <summary>Shield regenerated per second while connected to supply and out of combat.</summary>
        public Fix ShieldRegenPerSecond;

        // Firepower (Damage == 0 means the unit has no weapon)
        public Fix Damage;
        public Fix FireIntervalSeconds = Fix.One;
        public Fix Range;
        /// <summary>Intrinsic trait: every Nth shot deals double damage (0 = no trait).</summary>
        public int HeavySalvoEvery;

        // Mobility
        public Fix Speed;
        public Fix Radius = Fix.One;

        // Supply
        /// <summary>Ammunition carried. 0 means the weapon never runs dry.</summary>
        public int MaxAmmo;
        /// <summary>Seconds to restore one round while connected to supply.</summary>
        public Fix AmmoRefillSeconds = Fix.One;
        /// <summary>Mobile units count as supplied for this many seconds after leaving the network.</summary>
        public Fix OffGridGraceSeconds;

        /// <summary>Roles this unit prefers to attack, in priority order. Structure means supply relays.</summary>
        public UnitRole[] PreferredTargets = new UnitRole[0];

        public bool IsSupplySource;
        public bool IsRelay;

        public bool HasWeapon => Damage > Fix.Zero;
        public bool IsStructure => Role == UnitRole.Structure;
        public bool IsAir => Role == UnitRole.Air;
        public bool IsStatic => Speed == Fix.Zero;
    }
}
