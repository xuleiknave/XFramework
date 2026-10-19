using System.Collections.Generic;

namespace XFramework.Battle
{
    /// <summary>Everything needed to start a battle.</summary>
    public sealed class BattleSetup
    {
        public Formation Player;
        public Formation Enemy;
        public ulong Seed = 1;
        public int TimeLimitSeconds = 90;
    }

    /// <summary>Per-side statistics collected during a battle (used by the forecast).</summary>
    public sealed class SideStats
    {
        public readonly Dictionary<string, int> Losses = new Dictionary<string, int>();
        public readonly HashSet<string> RanDry = new HashSet<string>();
        public int RelaysLost;
        public bool SourceLost;
        /// <summary>Combat units that spent more than 5 seconds unsupplied in total.</summary>
        public int LongUnsuppliedUnits;
    }

    /// <summary>
    /// Deterministic formation auto-battle. Advance it with <see cref="Step"/> at
    /// <see cref="TicksPerSecond"/>; the same setup, seed and command sequence always
    /// produce the same result.
    /// </summary>
    public sealed class BattleSim
    {
        public const int TicksPerSecond = 20;
        public const int DrawSide = -1;
        public static readonly Fix TickDuration = Fix.FromRatio(1, TicksPerSecond);

        private const int RetargetIntervalTicks = 10;
        private const int ShieldRegenDelayTicks = 2 * TicksPerSecond;
        private const int LongUnsuppliedTicks = 5 * TicksPerSecond;
        private static readonly Fix StartingSupplyPoints = 2;
        private static readonly Fix MaxSupplyPoints = 10;
        private static readonly Fix SupplyPointsPerTick = Fix.FromRatio(1, TicksPerSecond);
        private static readonly Fix DamageVarianceMin = Fix.FromRatio(9, 10);
        private static readonly Fix DamageVarianceSpan = Fix.FromRatio(2, 10);
        private static readonly Fix[] PreferenceWeights = { Fix.FromRatio(16, 100), Fix.FromRatio(36, 100), Fix.FromRatio(64, 100) };

        private readonly List<Unit> _units = new List<Unit>();
        private readonly List<Unit> _allUnits = new List<Unit>();
        private readonly Dictionary<int, Unit> _unitsById = new Dictionary<int, Unit>();
        private readonly SupplyNetwork[] _networks = { new SupplyNetwork(), new SupplyNetwork() };
        private readonly bool[] _networkDirty = new bool[2];
        private readonly Fix[] _supplyPoints = { StartingSupplyPoints, StartingSupplyPoints };
        private readonly int[,] _skillReadyTick = new int[2, CommanderSkillDef.All.Length];
        private readonly List<Command> _pendingCommands = new List<Command>();
        private readonly List<PendingStrike> _strikes = new List<PendingStrike>();
        private readonly List<BattleEvent> _events = new List<BattleEvent>();
        private readonly List<PendingHit> _hits = new List<PendingHit>();
        private readonly DeterministicRandom _random;

        private struct PendingHit
        {
            public Unit Source;
            public Unit Target;
            public Fix Damage;
            public bool Critical;
        }

        private struct PendingStrike
        {
            public int Side;
            public FixVec2 Point;
            public int LandTick;
        }

        public BattleSim(BattleSetup setup)
        {
            Setup = setup;
            _random = new DeterministicRandom(setup.Seed);
            Stats = new[] { new SideStats(), new SideStats() };
            Spawn(setup.Player, BoardLayout.PlayerSide);
            Spawn(setup.Enemy, BoardLayout.EnemySide);
            _networks[0].BuildLinks();
            _networks[1].BuildLinks();
            UpdateSupply(emitEvents: false);
        }

        public BattleSetup Setup { get; }
        public int Tick { get; private set; }
        public bool IsOver { get; private set; }
        /// <summary>Winning side once the battle is over, or <see cref="DrawSide"/>.</summary>
        public int Winner { get; private set; } = DrawSide;
        public SideStats[] Stats { get; }

        /// <summary>Living units, in id order.</summary>
        public IReadOnlyList<Unit> Units => _units;

        /// <summary>Every unit that took part, including destroyed ones.</summary>
        public IReadOnlyList<Unit> AllUnits => _allUnits;

        /// <summary>Events produced by the most recent <see cref="Step"/>.</summary>
        public IReadOnlyList<BattleEvent> Events => _events;

        public Fix ElapsedSeconds => Fix.FromRatio(Tick, TicksPerSecond);

        public SupplyNetwork Network(int side) => _networks[side];

        public Fix SupplyPoints(int side) => _supplyPoints[side];

        public int SkillCooldownTicks(int side, CommanderSkill skill)
        {
            int remaining = _skillReadyTick[side, (int)skill] - Tick;
            return remaining > 0 ? remaining : 0;
        }

        public bool CanCast(int side, CommanderSkill skill) =>
            !IsOver
            && SkillCooldownTicks(side, skill) == 0
            && _supplyPoints[side] >= Fix.FromInt(CommanderSkillDef.Get(skill).Cost);

        /// <summary>Queues a command; it takes effect at the start of the next tick.</summary>
        public void Issue(Command command) => _pendingCommands.Add(command);

        public Unit GetUnit(int id) => _unitsById.TryGetValue(id, out Unit unit) ? unit : null;

        public int CountCombatants(int side)
        {
            int count = 0;
            foreach (Unit unit in _units)
            {
                if (unit.Side == side && unit.Def.HasWeapon)
                {
                    count++;
                }
            }
            return count;
        }

        public int CountInitialCombatants(int side)
        {
            int count = 0;
            foreach (Unit unit in _allUnits)
            {
                if (unit.Side == side && unit.Def.HasWeapon)
                {
                    count++;
                }
            }
            return count;
        }

        public void Step()
        {
            if (IsOver)
            {
                return;
            }

            _events.Clear();
            ApplyCommands();
            UpdateSupply(emitEvents: true);
            foreach (Unit unit in _units)
            {
                UpdateTargetAndMove(unit);
            }
            ResolveOverlaps();
            ResolveFire();
            LandStrikes();
            Regenerate();
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

        /// <summary>Hash of the full simulation state, used to verify determinism.</summary>
        public ulong ComputeStateHash()
        {
            ulong hash = 14695981039346656037UL;
            Mix(ref hash, (ulong)Tick);
            Mix(ref hash, _random.State);
            Mix(ref hash, (ulong)_supplyPoints[0].Raw);
            Mix(ref hash, (ulong)_supplyPoints[1].Raw);
            foreach (Unit unit in _units)
            {
                Mix(ref hash, (ulong)unit.Id);
                Mix(ref hash, (ulong)unit.Position.X.Raw);
                Mix(ref hash, (ulong)unit.Position.Y.Raw);
                Mix(ref hash, (ulong)unit.Hp.Raw);
                Mix(ref hash, (ulong)unit.Shield.Raw);
                Mix(ref hash, (ulong)unit.Ammo);
                Mix(ref hash, unit.Supplied ? 1UL : 0UL);
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

        // ------------------------------------------------------------------
        // Setup

        private void Spawn(Formation formation, int side)
        {
            foreach (Placement p in formation.Placements)
            {
                UnitDef def = p.Def;
                FixVec2 position = def.IsRelay
                    ? BoardLayout.Intersection(side, p.Depth, p.Row)
                    : BoardLayout.FootprintCenter(side, p.Depth, p.Row, def.Size);
                var unit = new Unit
                {
                    Id = _allUnits.Count + 1,
                    Side = side,
                    Def = def,
                    Position = position,
                    Hp = def.MaxHp,
                    Shield = def.MaxShield,
                    Ammo = def.MaxAmmo,
                    Supplied = true,
                };
                _units.Add(unit);
                _allUnits.Add(unit);
                _unitsById.Add(unit.Id, unit);

                if (def.IsSupplySource)
                {
                    _networks[side].AddNode(unit, BoardLayout.SourcePort(side, p.Depth, p.Row), true);
                }
                else if (def.IsRelay)
                {
                    _networks[side].AddNode(unit, position, false);
                }
            }
        }

        // ------------------------------------------------------------------
        // Commander skills

        private void ApplyCommands()
        {
            foreach (Command command in _pendingCommands)
            {
                if (!CanCast(command.Side, command.Skill))
                {
                    continue;
                }
                CommanderSkillDef def = CommanderSkillDef.Get(command.Skill);
                _supplyPoints[command.Side] -= Fix.FromInt(def.Cost);
                _skillReadyTick[command.Side, (int)command.Skill] = Tick + def.CooldownSeconds * TicksPerSecond;
                _events.Add(new BattleEvent
                {
                    Type = BattleEventType.SkillCast,
                    Tick = Tick,
                    SourceId = command.Side,
                    Amount = Fix.FromInt((int)command.Skill),
                    Point = command.Point,
                });

                switch (command.Skill)
                {
                    case CommanderSkill.OrbitalStrike:
                        _strikes.Add(new PendingStrike
                        {
                            Side = command.Side,
                            Point = ClampToBounds(command.Point),
                            LandTick = Tick + CommanderSkillDef.StrikeDelayTicks,
                        });
                        break;
                    case CommanderSkill.EmergencyShield:
                        foreach (Unit unit in _units)
                        {
                            if (unit.Side == command.Side)
                            {
                                unit.Shield = Fix.Min(unit.Def.MaxShield, unit.Shield + unit.Def.MaxShield * Fix.Half);
                            }
                        }
                        break;
                }
            }
            _pendingCommands.Clear();
        }

        private void LandStrikes()
        {
            for (int i = 0; i < _strikes.Count; i++)
            {
                PendingStrike strike = _strikes[i];
                if (strike.LandTick != Tick)
                {
                    continue;
                }
                _events.Add(new BattleEvent { Type = BattleEventType.StrikeLanded, Tick = Tick, SourceId = strike.Side, Point = strike.Point });
                Fix radiusSqr = CommanderSkillDef.StrikeRadius * CommanderSkillDef.StrikeRadius;
                foreach (Unit unit in _units)
                {
                    if (unit.Side != strike.Side && FixVec2.SqrDistance(unit.Position, strike.Point) <= radiusSqr)
                    {
                        ApplyDamage(null, unit, CommanderSkillDef.StrikeDamage, false);
                    }
                }
            }
            _strikes.RemoveAll(s => s.LandTick <= Tick);
        }

        // ------------------------------------------------------------------
        // Supply

        private void UpdateSupply(bool emitEvents)
        {
            for (int side = 0; side < 2; side++)
            {
                if (_networkDirty[side])
                {
                    _networks[side].Rebuild();
                    _networkDirty[side] = false;
                }
            }

            foreach (Unit unit in _units)
            {
                bool wasSupplied = unit.Supplied;
                if (unit.Def.IsStructure)
                {
                    unit.InCoverage = true;
                    unit.Supplied = true;
                    continue;
                }

                unit.InCoverage = _networks[unit.Side].IsCovered(unit.Position);
                if (unit.InCoverage)
                {
                    unit.OffGridTicks = 0;
                    unit.Supplied = true;
                }
                else
                {
                    unit.OffGridTicks++;
                    int grace = (unit.Def.OffGridGraceSeconds * TicksPerSecond).ToIntFloor();
                    unit.Supplied = unit.OffGridTicks <= grace;
                }

                if (!unit.Supplied)
                {
                    unit.UnsuppliedTicksTotal++;
                }
                if (emitEvents && wasSupplied != unit.Supplied)
                {
                    _events.Add(new BattleEvent
                    {
                        Type = BattleEventType.SupplyChanged,
                        Tick = Tick,
                        TargetId = unit.Id,
                        Amount = unit.Supplied ? Fix.One : Fix.Zero,
                    });
                }
            }
        }

        // ------------------------------------------------------------------
        // Targeting and movement

        private void UpdateTargetAndMove(Unit unit)
        {
            if (!unit.Def.HasWeapon)
            {
                return;
            }

            Unit target = GetUnit(unit.TargetId);
            if (target == null || unit.RetargetTicks <= 0)
            {
                target = ChooseTarget(unit);
                unit.TargetId = target != null ? target.Id : -1;
                unit.RetargetTicks = RetargetIntervalTicks;
            }
            unit.RetargetTicks--;

            if (target == null || unit.Def.IsStatic)
            {
                return;
            }

            Fix reach = unit.Def.Range + target.Def.Radius;
            if (FixVec2.SqrDistance(unit.Position, target.Position) > reach * reach)
            {
                unit.Position = ClampToBounds(FixVec2.MoveTowards(unit.Position, target.Position, unit.Def.Speed * TickDuration));
            }
        }

        /// <summary>Nearest enemy, with preferred roles weighted as if they were closer.</summary>
        private Unit ChooseTarget(Unit unit)
        {
            Unit best = null;
            Fix bestScore = Fix.MaxValue;
            foreach (Unit other in _units)
            {
                if (other.Side == unit.Side)
                {
                    continue;
                }
                Fix score = FixVec2.SqrDistance(unit.Position, other.Position);
                UnitRole[] preferred = unit.Def.PreferredTargets;
                for (int i = 0; i < preferred.Length && i < PreferenceWeights.Length; i++)
                {
                    // A preference for structures means supply relays: the line, not the fortified source.
                    bool matches = preferred[i] == other.Def.Role
                        && (preferred[i] != UnitRole.Structure || other.Def.IsRelay);
                    if (matches)
                    {
                        score = score * PreferenceWeights[i];
                        break;
                    }
                }
                // Ties resolve to the lower id because _units is in id order.
                if (score < bestScore)
                {
                    best = other;
                    bestScore = score;
                }
            }
            return best;
        }

        /// <summary>Pushes overlapping units apart. Air and ground do not collide; structures never move.</summary>
        private void ResolveOverlaps()
        {
            for (int i = 0; i < _units.Count; i++)
            {
                Unit a = _units[i];
                for (int j = i + 1; j < _units.Count; j++)
                {
                    Unit b = _units[j];
                    if (a.Def.IsAir != b.Def.IsAir)
                    {
                        continue;
                    }
                    bool aFixed = a.Def.IsStatic;
                    bool bFixed = b.Def.IsStatic;
                    if (aFixed && bFixed)
                    {
                        continue;
                    }

                    Fix minDist = a.Def.Radius + b.Def.Radius;
                    FixVec2 delta = b.Position - a.Position;
                    Fix sqr = delta.SqrMagnitude;
                    if (sqr >= minDist * minDist)
                    {
                        continue;
                    }

                    Fix dist = Fix.Sqrt(sqr);
                    FixVec2 dir = dist > Fix.Zero ? delta / dist : new FixVec2(Fix.Zero, Fix.One); // exact overlap: separate along the front
                    Fix overlap = minDist - dist;
                    if (aFixed)
                    {
                        b.Position = ClampToBounds(b.Position + dir * overlap);
                    }
                    else if (bFixed)
                    {
                        a.Position = ClampToBounds(a.Position - dir * overlap);
                    }
                    else
                    {
                        FixVec2 push = dir * (overlap * Fix.Half);
                        a.Position = ClampToBounds(a.Position - push);
                        b.Position = ClampToBounds(b.Position + push);
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        // Combat

        /// <summary>
        /// All units choose their shots first and damage is applied afterwards, so a unit
        /// destroyed this tick still gets its shot off (no id-order bias).
        /// </summary>
        private void ResolveFire()
        {
            _hits.Clear();
            foreach (Unit unit in _units)
            {
                if (!unit.Def.HasWeapon)
                {
                    continue;
                }
                if (unit.CooldownTicks > 0)
                {
                    unit.CooldownTicks--;
                }

                Unit target = GetUnit(unit.TargetId);
                if (target == null || unit.CooldownTicks > 0 || !unit.HasAmmo)
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
                _events.Add(new BattleEvent { Type = BattleEventType.Fired, Tick = Tick, SourceId = unit.Id, TargetId = target.Id, Critical = critical });

                if (unit.Def.MaxAmmo > 0)
                {
                    unit.Ammo--;
                    if (unit.Ammo == 0)
                    {
                        unit.RanDry = true;
                        Stats[unit.Side].RanDry.Add(unit.Def.Id);
                        _events.Add(new BattleEvent { Type = BattleEventType.OutOfAmmo, Tick = Tick, TargetId = unit.Id });
                    }
                }
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
            int sourceId = source != null ? source.Id : -1;

            _events.Add(new BattleEvent
            {
                Type = BattleEventType.Damaged,
                Tick = Tick,
                SourceId = sourceId,
                TargetId = target.Id,
                Amount = damage,
                Critical = critical,
            });

            if (!target.IsAlive)
            {
                target.Hp = Fix.Zero;
                _events.Add(new BattleEvent { Type = BattleEventType.Destroyed, Tick = Tick, SourceId = sourceId, TargetId = target.Id });
            }
        }

        private void Regenerate()
        {
            foreach (Unit unit in _units)
            {
                if (!unit.IsAlive || !unit.Supplied)
                {
                    continue;
                }
                if (Tick - unit.LastDamagedTick >= ShieldRegenDelayTicks)
                {
                    unit.Shield = Fix.Min(unit.Def.MaxShield, unit.Shield + unit.Def.ShieldRegenPerSecond * TickDuration);
                }
                if (unit.Def.MaxAmmo > 0 && unit.Ammo < unit.Def.MaxAmmo)
                {
                    unit.AmmoRefillTicks++;
                    if (unit.AmmoRefillTicks >= RefillTicks(unit.Def))
                    {
                        unit.Ammo++;
                        unit.AmmoRefillTicks = 0;
                    }
                }
            }

            for (int side = 0; side < 2; side++)
            {
                _supplyPoints[side] = Fix.Min(MaxSupplyPoints, _supplyPoints[side] + SupplyPointsPerTick);
            }
        }

        private void RemoveDestroyed()
        {
            for (int i = _units.Count - 1; i >= 0; i--)
            {
                Unit unit = _units[i];
                if (unit.IsAlive)
                {
                    continue;
                }

                SideStats stats = Stats[unit.Side];
                stats.Losses.TryGetValue(unit.Def.Id, out int lost);
                stats.Losses[unit.Def.Id] = lost + 1;
                if (unit.Def.IsRelay)
                {
                    stats.RelaysLost++;
                }
                if (unit.Def.IsSupplySource)
                {
                    stats.SourceLost = true;
                }
                if (unit.Def.IsRelay || unit.Def.IsSupplySource)
                {
                    _networkDirty[unit.Side] = true;
                }

                _unitsById.Remove(unit.Id);
                _units.RemoveAt(i);
            }
        }

        private void CheckBattleEnd()
        {
            int player = CountCombatants(BoardLayout.PlayerSide);
            int enemy = CountCombatants(BoardLayout.EnemySide);

            if (player == 0 || enemy == 0)
            {
                Winner = player > 0 ? BoardLayout.PlayerSide : enemy > 0 ? BoardLayout.EnemySide : DrawSide;
                EndBattle();
            }
            else if (Tick >= Setup.TimeLimitSeconds * TicksPerSecond)
            {
                // Time up: the side with more remaining combat durability wins.
                Fix playerScore = RemainingDurability(BoardLayout.PlayerSide);
                Fix enemyScore = RemainingDurability(BoardLayout.EnemySide);
                Winner = playerScore > enemyScore ? BoardLayout.PlayerSide
                    : enemyScore > playerScore ? BoardLayout.EnemySide
                    : DrawSide;
                EndBattle();
            }
        }

        private void EndBattle()
        {
            IsOver = true;
            foreach (Unit unit in _allUnits)
            {
                if (unit.Def.HasWeapon && unit.UnsuppliedTicksTotal > LongUnsuppliedTicks)
                {
                    Stats[unit.Side].LongUnsuppliedUnits++;
                }
            }
            _events.Add(new BattleEvent { Type = BattleEventType.BattleEnded, Tick = Tick, SourceId = Winner });
        }

        private Fix RemainingDurability(int side)
        {
            Fix total = Fix.Zero;
            foreach (Unit unit in _units)
            {
                if (unit.Side == side && unit.Def.HasWeapon)
                {
                    total += unit.Hp + unit.Shield;
                }
            }
            return total;
        }

        private static FixVec2 ClampToBounds(FixVec2 p)
        {
            FixVec2 h = BoardLayout.HalfExtents;
            return new FixVec2(Fix.Clamp(p.X, -h.X, h.X), Fix.Clamp(p.Y, -h.Y, h.Y));
        }

        private static int FireIntervalTicks(UnitDef def)
        {
            int ticks = (def.FireIntervalSeconds * TicksPerSecond).ToIntFloor();
            return ticks < 1 ? 1 : ticks;
        }

        private static int RefillTicks(UnitDef def)
        {
            int ticks = (def.AmmoRefillSeconds * TicksPerSecond).ToIntFloor();
            return ticks < 1 ? 1 : ticks;
        }
    }
}
