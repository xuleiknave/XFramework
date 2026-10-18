namespace XFramework.Battle
{
    public enum BattleEventType
    {
        Fired,
        Damaged,
        Destroyed,
        BattleEnded,
    }

    /// <summary>
    /// Something that happened during a tick. The presentation layer reads these
    /// to play effects; the simulation never depends on them.
    /// </summary>
    public struct BattleEvent
    {
        public BattleEventType Type;
        public int Tick;
        public int SourceId;
        public int TargetId;
        public Fix Amount;
        /// <summary>True when the shot was a heavy salvo (double damage).</summary>
        public bool Critical;
    }
}
