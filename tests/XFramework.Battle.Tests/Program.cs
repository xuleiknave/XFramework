using System;
using System.Collections.Generic;
using XFramework.Battle;

namespace XFramework.Battle.Tests
{
    /// <summary>Minimal test runner so the project needs no NuGet packages.</summary>
    internal static class Program
    {
        private static int _failures;

        private static int Main()
        {
            Run("定点数基础运算", FixMath);
            Run("确定性：同种子两次结果完全相同", SameSeedSameResult);
            Run("确定性：带指令的回放结果相同", ReplayWithCommandsMatches);
            Run("克制：4 战机群胜 1 战列舰", FightersBeatBattleship);
            Run("克制：1 战列舰胜 3 护卫舰", BattleshipBeatsEscorts);
            Run("克制：2 护卫舰胜 4 战机群", EscortsBeatFighters);
            Run("P0 遭遇战在时间限制内结束", SkirmishEndsInTime);
            Run("集火指令：所选单位攻击指定目标", AttackCommandFocusesTarget);
            Run("移动指令：单位到达目标点", MoveCommandReachesPoint);

            Console.WriteLine();
            Console.WriteLine(_failures == 0 ? "全部通过" : _failures + " 项失败");
            return _failures == 0 ? 0 : 1;
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                Console.WriteLine("[通过] " + name);
            }
            catch (Exception e)
            {
                _failures++;
                Console.WriteLine("[失败] " + name + "：" + e.Message);
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new Exception(message);
            }
        }

        private static void FixMath()
        {
            Assert(Fix.FromInt(3) * Fix.FromInt(4) == Fix.FromInt(12), "3*4");
            Assert(Fix.FromInt(10) / Fix.FromInt(4) == Fix.FromRatio(5, 2), "10/4");
            Assert(Fix.Sqrt(Fix.FromInt(81)) == Fix.FromInt(9), "sqrt 81");
            Fix s2 = Fix.Sqrt(Fix.FromInt(2));
            Assert(Math.Abs(s2.ToFloat() - 1.41421f) < 0.001f, "sqrt 2 = " + s2);
            var a = new FixVec2(0, 0);
            var b = new FixVec2(3, 4);
            Assert(FixVec2.Distance(a, b) == Fix.FromInt(5), "distance 3-4-5");
            Assert(FixVec2.MoveTowards(a, b, Fix.FromInt(10)) == b, "MoveTowards overshoot clamps");
        }

        private static Battle CreateAutoBattle(Scenario scenario)
        {
            var battle = new Battle(scenario);
            battle.AddAgent(new SimpleCommanderAI(Scenario.PlayerSide));
            battle.AddAgent(new SimpleCommanderAI(Scenario.EnemySide));
            return battle;
        }

        private static void SameSeedSameResult()
        {
            Battle a = CreateAutoBattle(Scenario.CreateP0Skirmish(42));
            Battle b = CreateAutoBattle(Scenario.CreateP0Skirmish(42));
            while (!a.IsOver)
            {
                a.Step();
                b.Step();
                Assert(a.ComputeStateHash() == b.ComputeStateHash(), "第 " + a.Tick + " 帧状态不一致");
            }
            Assert(b.IsOver && a.Winner == b.Winner, "结果不一致");

            Battle c = CreateAutoBattle(Scenario.CreateP0Skirmish(7));
            c.RunToEnd();
            Assert(c.ComputeStateHash() != a.ComputeStateHash(), "不同种子应产生不同过程");
        }

        private static void ReplayWithCommandsMatches()
        {
            // Record a battle where the player issues commands at fixed ticks, then replay it.
            var log = new List<KeyValuePair<int, Command>>();
            ulong RunOnce(bool record)
            {
                var battle = new Battle(Scenario.CreateP0Skirmish(3));
                battle.AddAgent(new SimpleCommanderAI(Scenario.EnemySide));
                int logIndex = 0;
                while (!battle.IsOver)
                {
                    if (record && battle.Tick == 40)
                    {
                        var cmd = Command.AttackMove(Scenario.PlayerSide, new[] { 1, 2, 3, 4, 5, 6, 7, 8 }, new FixVec2(10, 0));
                        log.Add(new KeyValuePair<int, Command>(battle.Tick, cmd));
                    }
                    if (record && battle.Tick == 200)
                    {
                        var cmd = Command.Attack(Scenario.PlayerSide, new[] { 5, 6, 7, 8 }, 9);
                        log.Add(new KeyValuePair<int, Command>(battle.Tick, cmd));
                    }
                    while (logIndex < log.Count && log[logIndex].Key == battle.Tick)
                    {
                        battle.Issue(log[logIndex].Value);
                        logIndex++;
                    }
                    battle.Step();
                }
                return battle.ComputeStateHash();
            }

            ulong recorded = RunOnce(true);
            ulong replayed = RunOnce(false);
            Assert(log.Count == 2, "应记录 2 条指令");
            Assert(recorded == replayed, "回放结果与原局不同");
        }

        private static int Duel(string attackerId, int attackers, string defenderId, int defenders, ulong seed)
        {
            var s = new Scenario { Seed = seed };
            for (int i = 0; i < attackers; i++)
            {
                s.Add(attackerId, Scenario.PlayerSide, -15, (i - attackers / 2) * 3);
            }
            for (int i = 0; i < defenders; i++)
            {
                s.Add(defenderId, Scenario.EnemySide, 15, (i - defenders / 2) * 4);
            }
            Battle battle = CreateAutoBattle(s);
            battle.RunToEnd();
            return battle.Winner;
        }

        private static void AssertWinsAcrossSeeds(string a, int na, string b, int nb)
        {
            for (ulong seed = 1; seed <= 20; seed++)
            {
                int winner = Duel(a, na, b, nb, seed);
                Assert(winner == Scenario.PlayerSide, "种子 " + seed + " 时 " + na + " " + a + " 未能战胜 " + nb + " " + b);
            }
        }

        private static void FightersBeatBattleship() => AssertWinsAcrossSeeds("fighter", 4, "battleship", 1);

        private static void BattleshipBeatsEscorts() => AssertWinsAcrossSeeds("battleship", 1, "escort", 3);

        private static void EscortsBeatFighters() => AssertWinsAcrossSeeds("escort", 2, "fighter", 4);

        private static void SkirmishEndsInTime()
        {
            int min = int.MaxValue, max = 0, playerWins = 0, enemyWins = 0;
            for (ulong seed = 1; seed <= 20; seed++)
            {
                Battle battle = CreateAutoBattle(Scenario.CreateP0Skirmish(seed));
                battle.RunToEnd();
                int seconds = battle.Tick / Battle.TicksPerSecond;
                Assert(seconds < 180, "种子 " + seed + " 用了 " + seconds + " 秒");
                min = Math.Min(min, seconds);
                max = Math.Max(max, seconds);
                if (battle.Winner == Scenario.PlayerSide)
                {
                    playerWins++;
                }
                else if (battle.Winner == Scenario.EnemySide)
                {
                    enemyWins++;
                }
            }
            Console.WriteLine("       对称遭遇战 20 局：用时 " + min + "~" + max + " 秒，左方胜 " + playerWins + " 局，右方胜 " + enemyWins + " 局，其余平局");
        }

        private static void AttackCommandFocusesTarget()
        {
            var battle = new Battle(Scenario.CreateP0Skirmish(1));
            battle.AddAgent(new SimpleCommanderAI(Scenario.EnemySide));
            // Player fighters (ids 5-8) focus the enemy battleship (id 9).
            battle.Issue(Command.Attack(Scenario.PlayerSide, new[] { 5, 6, 7, 8 }, 9));
            bool hitBattleship = false;
            for (int i = 0; i < 20 * 20 && !battle.IsOver; i++)
            {
                battle.Step();
                foreach (BattleEvent e in battle.Events)
                {
                    if (e.Type == BattleEventType.Fired && e.SourceId >= 5 && e.SourceId <= 8)
                    {
                        Assert(e.TargetId == 9, "战机攻击了非指定目标 " + e.TargetId);
                        hitBattleship = true;
                    }
                }
                if (battle.GetUnit(9) == null)
                {
                    break;
                }
            }
            Assert(hitBattleship, "战机没有攻击战列舰");
        }

        private static void MoveCommandReachesPoint()
        {
            // A distant enemy keeps the battle from ending immediately.
            var s = new Scenario()
                .Add("escort", Scenario.PlayerSide, 0, 0)
                .Add("escort", Scenario.EnemySide, 39, -24);
            var battle = new Battle(s);
            var target = new FixVec2(10, 5);
            battle.Issue(Command.Move(Scenario.PlayerSide, new[] { 1 }, target));
            for (int i = 0; i < 100; i++)
            {
                battle.Step();
            }
            Unit unit = battle.GetUnit(1);
            Assert(unit.Position == target, "未到达目标点，当前位置 " + unit.Position);
            Assert(unit.Order == OrderType.None, "到达后应恢复待命");
        }
    }
}
