namespace XFramework.Battle
{
    /// <summary>Unit role. Drives the counter relationships.</summary>
    public enum UnitRole
    {
        Fighter,
        Escort,
        Heavy,
    }

    /// <summary>
    /// Intrinsic attributes of a unit type (GDD 6.2). Speeds and ranges are in
    /// battle units; times are in seconds and converted to ticks by the battle.
    /// </summary>
    public sealed class UnitDef
    {
        public string Id;
        public string DisplayName;
        public UnitRole Role;

        // Survival
        public Fix MaxHp;
        public Fix MaxShield;
        /// <summary>Shield points regenerated per second once out of combat.</summary>
        public Fix ShieldRegenPerSecond;

        // Firepower
        public Fix Damage;
        public Fix FireIntervalSeconds;
        public Fix Range;

        // Mobility
        public Fix Speed;
        /// <summary>Collision radius used to keep units from stacking.</summary>
        public Fix Radius;

        /// <summary>Distance at which an idle unit notices and engages enemies.</summary>
        public Fix SightRange;

        /// <summary>
        /// Intrinsic trait: every Nth shot deals double damage (0 = no trait).
        /// Used by the battleship's "heavy salvo".
        /// </summary>
        public int HeavySalvoEvery;
    }
}
