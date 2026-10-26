namespace Starline.Battle
{
    /// <summary>Unit role. Drives the counter relationships and targeting preferences.</summary>
    public enum UnitRole
    {
        /// <summary>Frigates and other fast escorts.</summary>
        Light,
        /// <summary>Battleships and other armoured capital ships.</summary>
        Heavy,
        /// <summary>Strike craft: small, fast, fly between the big ships.</summary>
        Craft,
        /// <summary>Point-defence ships that shred strike craft.</summary>
        PointDefense,
        /// <summary>Long-range missile and siege ships.</summary>
        Artillery,
        /// <summary>Supply sources and relay nodes: no weapon, never move.</summary>
        Structure,
    }

    /// <summary>The five factions of the story (story.md section 2). Structures and neutral units use None.</summary>
    public enum Faction
    {
        None,
        /// <summary>晨星骑士团</summary>
        Morningstar,
        /// <summary>边境游侠会</summary>
        Rangers,
        /// <summary>银月法师塔</summary>
        Silvermoon,
        /// <summary>亡灵</summary>
        Undead,
        /// <summary>影歌精灵</summary>
        Elves,
    }

    /// <summary>
    /// Intrinsic attributes of a unit type (GDD 6.2). Distances are in battle units
    /// (one board cell is <see cref="BoardLayout.CellSize"/> units); times are in seconds.
    /// </summary>
    public sealed class UnitDef
    {
        public string Id;
        /// <summary>Name and one-line flavour text (Game table; enemies have no description).</summary>
        public LocText Name => LocText.UnitName(Id);
        public LocText Description => new LocText("unit." + Id + ".description");
        public UnitRole Role;
        public Faction Faction;

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
        public bool IsCraft => Role == UnitRole.Craft;
        public bool IsStatic => Speed == Fix.Zero;

        public const int MaxStar = 3;

        /// <summary>Durability multiplier for a star level (hull, shield and shield regeneration); from skills.json.</summary>
        public static Fix HpScale(int star) => CommanderSkillDef.StarHpScale[Clamp(star) - 1];

        /// <summary>Damage multiplier for a star level; from skills.json.</summary>
        public static Fix DamageScale(int star) => CommanderSkillDef.StarDamageScale[Clamp(star) - 1];

        private static int Clamp(int star) => star < 1 ? 1 : star > MaxStar ? MaxStar : star;
    }
}
