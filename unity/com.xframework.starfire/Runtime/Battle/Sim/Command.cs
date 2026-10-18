using System.Collections.Generic;

namespace XFramework.Battle
{
    /// <summary>
    /// A player or AI order. Commands are queued and applied at the start of the
    /// next tick, so the same command list always yields the same battle.
    /// </summary>
    public sealed class Command
    {
        public int Side;
        public OrderType Order;
        public int[] UnitIds;
        public FixVec2 Point;
        public int TargetId = -1;

        public static Command Move(int side, IEnumerable<int> unitIds, FixVec2 point) =>
            new Command { Side = side, Order = OrderType.Move, UnitIds = ToArray(unitIds), Point = point };

        public static Command AttackMove(int side, IEnumerable<int> unitIds, FixVec2 point) =>
            new Command { Side = side, Order = OrderType.AttackMove, UnitIds = ToArray(unitIds), Point = point };

        public static Command Attack(int side, IEnumerable<int> unitIds, int targetId) =>
            new Command { Side = side, Order = OrderType.Attack, UnitIds = ToArray(unitIds), TargetId = targetId };

        public static Command Stop(int side, IEnumerable<int> unitIds) =>
            new Command { Side = side, Order = OrderType.None, UnitIds = ToArray(unitIds) };

        private static int[] ToArray(IEnumerable<int> ids) => new List<int>(ids).ToArray();
    }
}
