namespace XFramework.Battle
{
    public enum OrderType
    {
        /// <summary>No order: engage enemies that come within sight.</summary>
        None,
        /// <summary>Move to a point, firing at anything in range on the way without stopping.</summary>
        Move,
        /// <summary>Move to a point, but stop to fight enemies that come within sight.</summary>
        AttackMove,
        /// <summary>Chase and attack a specific enemy unit.</summary>
        Attack,
    }

    /// <summary>Runtime state of one unit in a battle.</summary>
    public sealed class Unit
    {
        public int Id;
        public int Side;
        public UnitDef Def;

        public FixVec2 Position;
        public Fix Hp;
        public Fix Shield;

        public OrderType Order;
        public FixVec2 OrderPoint;
        public int OrderTargetId;

        /// <summary>Enemy currently being shot at (-1 for none).</summary>
        public int CurrentTargetId = -1;
        public int CooldownTicks;
        public int ShotsFired;
        /// <summary>Tick of the last damage taken, used to delay shield regeneration.</summary>
        public int LastDamagedTick = int.MinValue / 2;

        public bool IsAlive => Hp > Fix.Zero;
    }
}
