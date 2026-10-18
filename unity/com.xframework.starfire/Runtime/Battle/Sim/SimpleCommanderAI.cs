namespace XFramework.Battle
{
    /// <summary>
    /// P0 opponent: once per second, sends every idle unit that has no enemy in
    /// sight toward the nearest enemy. Units fight on their own once in sight.
    /// </summary>
    public sealed class SimpleCommanderAI : IBattleAgent
    {
        private const int ThinkIntervalTicks = Battle.TicksPerSecond;

        private readonly int _side;
        private readonly int[] _single = new int[1];

        public SimpleCommanderAI(int side)
        {
            _side = side;
        }

        public void Think(Battle battle)
        {
            if (battle.Tick % ThinkIntervalTicks != 0)
            {
                return;
            }

            foreach (Unit unit in battle.Units)
            {
                if (unit.Side != _side || unit.Order != OrderType.None)
                {
                    continue;
                }
                if (battle.FindNearestEnemy(unit, unit.Def.SightRange) != null)
                {
                    continue;
                }

                Unit target = battle.FindNearestEnemy(unit, Fix.FromInt(1000));
                if (target != null)
                {
                    _single[0] = unit.Id;
                    battle.Issue(Command.AttackMove(_side, _single, target.Position));
                }
            }
        }
    }
}
