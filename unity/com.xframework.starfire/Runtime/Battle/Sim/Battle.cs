using System.Collections.Generic;

namespace XFramework.Battle
{
    /// <summary>An AI that issues commands for one side once per tick.</summary>
    public interface IBattleAgent
    {
        void Think(Battle battle);
    }

    /// <summary>
    /// Deterministic battle simulation. Advance it with <see cref="Step"/> at a fixed
    /// rate of <see cref="TicksPerSecond"/>; the same scenario, seed and command
    /// sequence always produce the same result.
    /// </summary>
    public sealed class Battle
    {
        public const int TicksPerSecond = 20;
        public const int DrawSide = -1;
        public static readonly Fix TickDuration = Fix.FromRatio(1, TicksPerSecond);

        private const int ShieldRegenDelayTicks = 3 * TicksPerSecond;
        /// <summary>Damage is scaled by a random factor in [0.9, 1.1).</summary>
        private static readonly Fix DamageVarianceMin = Fix.FromRatio(9, 10);
        private static readonly Fix DamageVarianceSpan = Fix.FromRatio(2, 10);

        private readonly List<Unit> _units = new List<Unit>();
        private readonly Dictionary<int, Unit> _unitsById = new Dictionary<int, Unit>();
        private readonly List<Command> _pendingCommands = new List<Command>();
        private readonly List<BattleEvent> _events = new List<BattleEvent>();
        private readonly List<IBattleAgent> _agents = new List<IBattleAgent>();
        private readonly List<PendingHit> _hits = new List<PendingHit>();
        private readonly DeterministicRandom _random;

        private struct PendingHit
        {
            public Unit Source;
            public Unit Target;
            public Fix Damage;
            public bool Critical;
        }

        public Battle(Scenario scenario)
        {
            Scenario = scenario;
            _random = new DeterministicRandom(scenario.Seed);

            int nextId = 1;
            foreach (UnitSpawn spawn in scenario.Spawns)
            {
                UnitDef def = UnitCatalog.Get(spawn.UnitId);
                var unit = new Unit
                {
                    Id = nextId++,
                    Side = spawn.Side,
                    Def = def,
                    Position = spawn.Position,
                    Hp = def.MaxHp,
                    Shield = def.MaxShield,
                };
                _units.Add(unit);
                _unitsById.Add(unit.Id, unit);
            }
        }

        public Scenario Scenario { get; }
        public int Tick { get; private set; }
        public bool IsOver { get; private set; }
        /// <summary>Winning side once the battle is over, or <see cref="DrawSide"/>.</summary>
        public int Winner { get; private set; } = DrawSide;

        /// <summary>Living units, in id order.</summary>
        public IReadOnlyList<Unit> Units => _units;

        /// <summary>Events produced by the most recent <see cref="Step"/>.</summary>
        public IReadOnlyList<BattleEvent> Events => _events;

        public Fix ElapsedSeconds => Fix.FromRatio(Tick, TicksPerSecond);

        public void AddAgent(IBattleAgent agent) => _agents.Add(agent);

        /// <summary>Queues a command; it takes effect at the start of the next tick.</summary>
        public void Issue(Command command) => _pendingCommands.Add(command);

        public Unit GetUnit(int id) => _unitsById.TryGetValue(id, out Unit unit) ? unit : null;

        public void Step()
        {
            if (IsOver)
            {
                return;
            }

            _events.Clear();
            ApplyCommands();
            foreach (IBattleAgent agent in _agents)
            {
                agent.Think(this);
            }

            foreach (Unit unit in _units)
            {
                UpdateMovement(unit);
            }
            ResolveOverlaps();
            ResolveFire();
            RegenerateShields();
            RemoveDestroyed();

            Tick++;
            CheckBattleEnd();
        }

        /// <summary>Runs until the battle ends or the tick limit is reached.</summary>
        public void RunToEnd(int maxTicks = int.MaxValue)
        {
            while (!IsOver && Tick < maxTicks)
            {
                Step();
            }
        }

        public Unit FindNearestEnemy(Unit unit, Fix maxDistance)
        {
            Unit best = null;
            Fix bestSqr = maxDistance * maxDistance;
            foreach (Unit other in _units)
            {
                if (other.Side == unit.Side)
                {
                    continue;
                }
                Fix sqr = FixVec2.SqrDistance(unit.Position, other.Position);
                if (sqr <= bestSqr)
                {
                    // Ties resolve to the lower id because _units is in id order.
                    if (best == null || sqr < bestSqr)
                    {
                        best = other;
                        bestSqr = sqr;
                    }
                }
            }
            return best;
        }

        public int CountUnits(int side)
        {
            int count = 0;
            foreach (Unit unit in _units)
            {
                if (unit.Side == side)
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>Hash of the full simulation state, used to verify determinism.</summary>
        public ulong ComputeStateHash()
        {
            ulong hash = 14695981039346656037UL;
            Mix(ref hash, (ulong)Tick);
            Mix(ref hash, _random.State);
            foreach (Unit unit in _units)
            {
                Mix(ref hash, (ulong)unit.Id);
                Mix(ref hash, (ulong)unit.Position.X.Raw);
                Mix(ref hash, (ulong)unit.Position.Y.Raw);
                Mix(ref hash, (ulong)unit.Hp.Raw);
                Mix(ref hash, (ulong)unit.Shield.Raw);
                Mix(ref hash, (ulong)unit.CooldownTicks);
            }
            return hash;
        }

        private static void Mix(ref ulong hash, ulong value)
        {
            for (int i = 0; i < 8; i++)
            {
                hash ^= (value >> (i * 8)) & 0xFF;
                hash *= 1099511628211UL;
            }
        }

        private void ApplyCommands()
        {
            foreach (Command command in _pendingCommands)
            {
                foreach (int id in command.UnitIds)
                {
                    Unit unit = GetUnit(id);
                    if (unit == null || unit.Side != command.Side)
                    {
                        continue;
                    }
                    unit.Order = command.Order;
                    unit.OrderPoint = ClampToMap(command.Point);
                    unit.OrderTargetId = command.TargetId;
                }
            }
            _pendingCommands.Clear();
        }

        private void UpdateMovement(Unit unit)
        {
            Fix step = unit.Def.Speed * TickDuration;
            unit.CurrentTargetId = -1;

            switch (unit.Order)
            {
                case OrderType.Attack:
                {
                    Unit target = GetUnit(unit.OrderTargetId);
                    if (target == null)
                    {
                        unit.Order = OrderType.None;
                        goto case OrderType.None;
                    }
                    Engage(unit, target, step);
                    break;
                }
                case OrderType.Move:
                {
                    unit.Position = FixVec2.MoveTowards(unit.Position, unit.OrderPoint, step);
                    if (unit.Position == unit.OrderPoint)
                    {
                        unit.Order = OrderType.None;
                    }
                    // Fire at anything in range on the way, without stopping.
                    Unit inRange = FindNearestEnemy(unit, unit.Def.Range);
                    unit.CurrentTargetId = inRange != null ? inRange.Id : -1;
                    break;
                }
                case OrderType.AttackMove:
                {
                    Unit enemy = FindNearestEnemy(unit, unit.Def.SightRange);
                    if (enemy != null)
                    {
                        Engage(unit, enemy, step);
                        break;
                    }
                    unit.Position = FixVec2.MoveTowards(unit.Position, unit.OrderPoint, step);
                    if (unit.Position == unit.OrderPoint)
                    {
                        unit.Order = OrderType.None;
                    }
                    break;
                }
                case OrderType.None:
                {
                    Unit enemy = FindNearestEnemy(unit, unit.Def.SightRange);
                    if (enemy != null)
                    {
                        Engage(unit, enemy, step);
                    }
                    break;
                }
            }
        }

        /// <summary>Closes to weapon range and marks the target to be fired on.</summary>
        private void Engage(Unit unit, Unit target, Fix step)
        {
            Fix reach = unit.Def.Range + target.Def.Radius;
            if (FixVec2.SqrDistance(unit.Position, target.Position) > reach * reach)
            {
                unit.Position = FixVec2.MoveTowards(unit.Position, target.Position, step);
            }
            unit.CurrentTargetId = target.Id;
        }

        /// <summary>Pushes overlapping units apart so they do not stack on one spot.</summary>
        private void ResolveOverlaps()
        {
            for (int i = 0; i < _units.Count; i++)
            {
                Unit a = _units[i];
                for (int j = i + 1; j < _units.Count; j++)
                {
                    Unit b = _units[j];
                    Fix minDist = a.Def.Radius + b.Def.Radius;
                    FixVec2 delta = b.Position - a.Position;
                    Fix sqr = delta.SqrMagnitude;
                    if (sqr >= minDist * minDist)
                    {
                        continue;
                    }

                    Fix dist = Fix.Sqrt(sqr);
                    FixVec2 dir = dist > Fix.Zero
                        ? delta / dist
                        : new FixVec2(Fix.One, Fix.Zero); // exact overlap: separate along X
                    FixVec2 push = dir * ((minDist - dist) * Fix.Half);
                    a.Position = ClampToMap(a.Position - push);
                    b.Position = ClampToMap(b.Position + push);
                }
            }
        }

        /// <summary>
        /// All units choose their shots first and damage is applied afterwards, so
        /// a unit destroyed this tick still gets its shot off (no id-order bias).
        /// </summary>
        private void ResolveFire()
        {
            _hits.Clear();
            foreach (Unit unit in _units)
            {
                if (unit.CooldownTicks > 0)
                {
                    unit.CooldownTicks--;
                }

                Unit target = GetUnit(unit.CurrentTargetId);
                if (target == null || unit.CooldownTicks > 0)
                {
                    continue;
                }
                Fix reach = unit.Def.Range + target.Def.Radius;
                if (FixVec2.SqrDistance(unit.Position, target.Position) > reach * reach)
                {
                    continue;
                }

                unit.ShotsFired++;
                bool critical = unit.Def.HeavySalvoEvery > 0 && unit.ShotsFired % unit.Def.HeavySalvoEvery == 0;
                Fix damage = unit.Def.Damage * CounterTable.Get(unit.Def.Role, target.Def.Role);
                damage = damage * (DamageVarianceMin + DamageVarianceSpan * _random.NextFix());
                if (critical)
                {
                    damage = damage * 2;
                }

                _hits.Add(new PendingHit { Source = unit, Target = target, Damage = damage, Critical = critical });
                unit.CooldownTicks = FireIntervalTicks(unit.Def);
                _events.Add(new BattleEvent
                {
                    Type = BattleEventType.Fired,
                    Tick = Tick,
                    SourceId = unit.Id,
                    TargetId = target.Id,
                    Critical = critical,
                });
            }

            foreach (PendingHit hit in _hits)
            {
                ApplyDamage(hit.Source, hit.Target, hit.Damage, hit.Critical);
            }
        }

        private void ApplyDamage(Unit source, Unit target, Fix damage, bool critical)
        {
            if (!target.IsAlive)
            {
                return;
            }

            Fix absorbed = Fix.Min(target.Shield, damage);
            target.Shield -= absorbed;
            target.Hp -= damage - absorbed;
            target.LastDamagedTick = Tick;

            _events.Add(new BattleEvent
            {
                Type = BattleEventType.Damaged,
                Tick = Tick,
                SourceId = source.Id,
                TargetId = target.Id,
                Amount = damage,
                Critical = critical,
            });

            if (!target.IsAlive)
            {
                target.Hp = Fix.Zero;
                _events.Add(new BattleEvent
                {
                    Type = BattleEventType.Destroyed,
                    Tick = Tick,
                    SourceId = source.Id,
                    TargetId = target.Id,
                });
            }
        }

        private void RegenerateShields()
        {
            foreach (Unit unit in _units)
            {
                if (!unit.IsAlive || Tick - unit.LastDamagedTick < ShieldRegenDelayTicks)
                {
                    continue;
                }
                unit.Shield = Fix.Min(unit.Def.MaxShield, unit.Shield + unit.Def.ShieldRegenPerSecond * TickDuration);
            }
        }

        private void RemoveDestroyed()
        {
            for (int i = _units.Count - 1; i >= 0; i--)
            {
                if (!_units[i].IsAlive)
                {
                    _unitsById.Remove(_units[i].Id);
                    _units.RemoveAt(i);
                }
            }
        }

        private void CheckBattleEnd()
        {
            int player = CountUnits(Scenario.PlayerSide);
            int enemy = CountUnits(Scenario.EnemySide);

            if (player == 0 || enemy == 0)
            {
                Winner = player > 0 ? Scenario.PlayerSide : enemy > 0 ? Scenario.EnemySide : DrawSide;
                EndBattle();
            }
            else if (Tick >= Scenario.TimeLimitSeconds * TicksPerSecond)
            {
                // Time up: the side with more remaining durability wins.
                Fix playerScore = RemainingDurability(Scenario.PlayerSide);
                Fix enemyScore = RemainingDurability(Scenario.EnemySide);
                Winner = playerScore > enemyScore ? Scenario.PlayerSide
                    : enemyScore > playerScore ? Scenario.EnemySide
                    : DrawSide;
                EndBattle();
            }
        }

        private void EndBattle()
        {
            IsOver = true;
            _events.Add(new BattleEvent { Type = BattleEventType.BattleEnded, Tick = Tick, SourceId = Winner });
        }

        private Fix RemainingDurability(int side)
        {
            Fix total = Fix.Zero;
            foreach (Unit unit in _units)
            {
                if (unit.Side == side)
                {
                    total += unit.Hp + unit.Shield;
                }
            }
            return total;
        }

        private FixVec2 ClampToMap(FixVec2 p)
        {
            FixVec2 h = Scenario.HalfSize;
            return new FixVec2(Fix.Clamp(p.X, -h.X, h.X), Fix.Clamp(p.Y, -h.Y, h.Y));
        }

        private static int FireIntervalTicks(UnitDef def)
        {
            int ticks = (def.FireIntervalSeconds * TicksPerSecond).ToIntFloor();
            return ticks < 1 ? 1 : ticks;
        }
    }
}
