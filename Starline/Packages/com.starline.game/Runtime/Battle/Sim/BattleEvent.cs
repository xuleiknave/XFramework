namespace Starline.Battle
{
    public enum BattleEventType
    {
        Fired,
        Damaged,
        Destroyed,
        /// <summary>A unit gained or lost supply. Amount is 1 when supplied, 0 when cut off.</summary>
        SupplyChanged,
        /// <summary>A unit fired its last round. TargetId is the unit.</summary>
        OutOfAmmo,
        /// <summary>A commander skill was cast. SourceId is the side, Amount the skill index.</summary>
        SkillCast,
        /// <summary>An orbital strike landed at Point.</summary>
        StrikeLanded,
        BattleEnded,
    }

    /// <summary>
    /// Something that happened during a tick. The presentation layer reads these to play
    /// effects; the simulation never depends on them.
    /// </summary>
    public struct BattleEvent
    {
        public BattleEventType Type;
        public int Tick;
        public int SourceId;
        public int TargetId;
        public Fix Amount;
        public FixVec2 Point;
        /// <summary>True when the shot was a heavy salvo (double damage).</summary>
        public bool Critical;
    }
}
