using System;
using System.Collections.Generic;
using System.Diagnostics;
using Starline.Battle;

namespace Starline.Battle.Tests
{
    /// <summary>Minimal test runner so the project needs no NuGet packages.</summary>
    internal static partial class Program
    {
        private static int _failures;

        private static int Main()
        {
            Fixtures.LoadGameData();
            Run("配置表：JSON 解析与定点数精确转换", JsonParsing);
            Run("定点数基础运算", FixMath);
            Run("棋盘坐标：格子中心与源口", BoardGeometry);
            Run("布阵规则：重叠、越界、指挥点、魔网源头", FormationValidation);
            Run("魔网：默认阵型的覆盖情况", SuggestedFormationCoverage);
            Run("魔网：中间节点被毁后两翼断开", NetworkBreaksWhenMiddleNodeDies);
            Run("确定性：同种子两次结果完全相同", SameSeedSameResult);
            Run("断魔网：奥术法师用光法力后停手", UnsuppliedUnitRunsDry);
            Run("机动单位离网 10 秒内仍算有魔网", MobileGracePeriod);
            Run("克制：2 弩手胜 4 怨灵", AntiAirBeatsAir);
            Run("克制：1 重甲骑士胜 3 食尸鬼", HeavyBeatsLight);
            Run("克制：3 狮鹫骑士胜 1 憎恶", AirBeatsHeavy);
            Run("指挥官技能：陨星术消耗法术点数并造成范围伤害", OrbitalStrike);
            Run("指挥官技能：法术点数不足时无法释放", SkillNeedsSupplyPoints);
            Run("战斗在时间限制内结束", BattlesEndInTime);
            Run("战前推演：100 场在 5 秒内完成，胜率在合理区间", ForecastIsFastAndBalanced);
            Run("魔网影响胜负：去掉法力水晶后胜率下降", SupplyMatters);
            Run("星级：★2 / ★3 提升耐久和伤害", StarScaling);
            Run("合成：3 支同星部队合成高一星，并保留上阵位置", MergeKeepsPlacement);
            Run("合成：连锁合成到 ★3", ChainMerge);
            Run("地图：结构完整，每个节点都能到达 Boss", StarMapIsConnected);
            Run("远征：失败扣耐久，Boss 失败扣 2，归零结束", DurabilityRules);
            Run("远征：胜利后三选一，部队自动上阵", RewardsAndAutoDeploy);
            Run("敌方阵容都合法", EncountersAreValid);
            Run("平衡模拟：自动玩家打第一幕", Act1BalanceSimulation);
            RunTerrainTests();

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

        private static void Info(string message) => Console.WriteLine("       " + message);

        // ------------------------------------------------------------------

        private static void FixMath()
        {
            Assert(Fix.FromInt(3) * Fix.FromInt(4) == Fix.FromInt(12), "3*4");
            Assert(Fix.FromInt(10) / Fix.FromInt(4) == Fix.FromRatio(5, 2), "10/4");
            Assert(Fix.Sqrt(Fix.FromInt(81)) == Fix.FromInt(9), "sqrt 81");
            Assert(FixVec2.Distance(new FixVec2(0, 0), new FixVec2(3, 4)) == Fix.FromInt(5), "3-4-5");
            Assert(Fix.FromRatio(-3, 2) == -Fix.FromRatio(3, 2), "negative ratio");
        }

        private static void BoardGeometry()
        {
            // Player front-row cell nearest the contact line, bottom row.
            Assert(BoardLayout.FootprintCenter(0, 0, 0, 1) == new FixVec2(-1, -7), "player (0,0)");
            Assert(BoardLayout.FootprintCenter(1, 0, 0, 1) == new FixVec2(1, -7), "enemy (0,0)");
            // 2x2 anchored at depth 4, row 3 spans x -12..-8, y -2..2.
            Assert(BoardLayout.FootprintCenter(0, 4, 3, 2) == new FixVec2(-10, 0), "player 2x2");
            Assert(BoardLayout.SourcePort(0, 4, 3) == new FixVec2(-8, 0), "source port");
            Assert(BoardLayout.Intersection(1, 2, 4) == new FixVec2(4, 0), "enemy intersection");
        }

        private static void FormationValidation()
        {
            Encounter e = Fixtures.WreckFieldAmbush();
            Assert(FormationRules.Validate(e.SuggestedPlayer, e.CommandLimit).Count == 0,
                "默认阵型应合法：" + string.Join("；", FormationRules.Validate(e.SuggestedPlayer, e.CommandLimit)));
            Assert(FormationRules.Validate(e.Enemy, 99).Count == 0, "敌方阵型应合法");

            Formation f = e.SuggestedPlayer;
            Assert(!FormationRules.CanPlace(f, UnitCatalog.Frigate, 1, 3), "重甲骑士占用的格子不能再放");
            Assert(!FormationRules.CanPlace(f, UnitCatalog.Get("battleship"), 5, 7), "2x2 越界");
            Assert(FormationRules.CanPlace(f, UnitCatalog.Frigate, 1, 0), "空格可以放");
            Assert(!FormationRules.CanPlace(f, UnitCatalog.Relay, 2, 4), "交叉点已有法力水晶");
            Assert(FormationRules.CanPlace(f, UnitCatalog.Relay, 0, 4), "空交叉点可以放法力水晶");

            Formation noSource = f.Clone();
            noSource.Placements.RemoveAll(p => p.Def.IsSupplySource);
            Assert(FormationRules.Validate(noSource, e.CommandLimit).Count > 0, "缺少魔网源头应报错");
            Assert(FormationRules.Validate(f, 5).Count > 0, "指挥点超限应报错");
        }

        private static void SuggestedFormationCoverage()
        {
            Encounter e = Fixtures.WreckFieldAmbush();
            SupplyNetwork net = SupplyNetwork.ForFormation(e.SuggestedPlayer, 0);
            int covered = 0, total = 0;
            foreach (Placement p in e.SuggestedPlayer.Placements)
            {
                if (!p.Def.HasWeapon)
                {
                    continue;
                }
                total++;
                bool c = net.CoversPlacement(p, 0);
                if (c)
                {
                    covered++;
                }
                if (p.UnitId == "missile_ship" && p.Depth == 5 && p.Row == 0)
                {
                    Assert(!c, "左下角奥术法师应未接入");
                }
                if (p.UnitId == "battleship")
                {
                    Assert(c, "重甲骑士应已接入");
                }
            }
            Info("默认阵型：" + covered + " / " + total + " 支部队接入魔网");
            foreach (SupplyNode node in net.Nodes)
            {
                Assert(node.Connected, "所有节点都应连回魔网源头");
            }
        }

        private static void NetworkBreaksWhenMiddleNodeDies()
        {
            var source = new Unit { Hp = 1 };
            var middle = new Unit { Hp = 1 };
            var wing = new Unit { Hp = 1 };
            var net = new SupplyNetwork();
            net.AddNode(source, BoardLayout.SourcePort(0, 4, 3), true);   // (-8, 0)
            net.AddNode(middle, BoardLayout.Intersection(0, 2, 4), false); // (-4, 0)
            net.AddNode(wing, BoardLayout.Intersection(0, 2, 1), false);   // (-4, -6)
            net.BuildLinks();
            Assert(net.Nodes[2].Connected, "两翼节点通过中间节点连通");
            Assert(!SupplyNetwork.AreLinked(net.Nodes[0].Position, net.Nodes[2].Position), "魔网源头和两翼不直接相连");

            middle.Hp = Fix.Zero;
            net.Rebuild();
            Assert(!net.Nodes[2].Connected, "中间节点被毁后两翼断开");
            Assert(!net.IsCovered(new FixVec2(-5, -7)), "两翼覆盖区失效");
            Assert(net.IsCovered(new FixVec2(-9, 1)), "魔网源头附近仍有覆盖");
        }

        private static void SameSeedSameResult()
        {
            Encounter e = Fixtures.WreckFieldAmbush();
            var a = new BattleSim(e.CreateSetup(e.SuggestedPlayer, 42));
            var b = new BattleSim(e.CreateSetup(e.SuggestedPlayer, 42));
            while (!a.IsOver)
            {
                a.Step();
                b.Step();
                Assert(a.ComputeStateHash() == b.ComputeStateHash(), "第 " + a.Tick + " 帧状态不一致");
            }
            Assert(b.IsOver && a.Winner == b.Winner, "结果不一致");

            var c = new BattleSim(e.CreateSetup(e.SuggestedPlayer, 7));
            c.RunToEnd();
            Assert(c.ComputeStateHash() != a.ComputeStateHash(), "不同种子应产生不同过程");
        }

        private static Formation WithSource(int side)
        {
            return new Formation().Add(side == 0 ? "supply_ship" : "hive", 4, 3);
        }

        private static void UnsuppliedUnitRunsDry()
        {
            // A missile ship outside coverage, starting with only 3 rounds, facing a tough target.
            Formation player = WithSource(0).Add("missile_ship", 0, 0);
            Formation enemy = WithSource(1).Add("brood_mother", 0, 0);
            var setup = new BattleSetup { Player = player, Enemy = enemy, Seed = 3, TimeLimitSeconds = 60 };
            var battle = new BattleSim(setup);
            Unit car = battle.Units[1];
            Assert(car.Def.Id == "missile_ship", "测试单位");
            Assert(!car.Supplied, "奥术法师开局应未接入魔网");
            const int carried = 3;
            car.Ammo = carried;

            int shots = 0;
            int lastShotTick = 0;
            while (!battle.IsOver)
            {
                battle.Step();
                foreach (BattleEvent ev in battle.Events)
                {
                    if (ev.Type == BattleEventType.Fired && ev.SourceId == car.Id)
                    {
                        shots++;
                        lastShotTick = battle.Tick;
                    }
                }
            }
            Assert(shots == carried, "断魔网时只能打出携带的 " + carried + " 发，实际 " + shots);
            Assert(car.RanDry, "应记录法力耗尽");
            Info("奥术法师最后一发在第 " + lastShotTick / BattleSim.TicksPerSecond + " 秒，之后停手");
        }

        private static void MobileGracePeriod()
        {
            Formation player = WithSource(0).Add("strike_craft", 0, 0);
            Formation enemy = WithSource(1).Add("crawler", 5, 7);
            var battle = new BattleSim(new BattleSetup { Player = player, Enemy = enemy, Seed = 1 });
            Unit fighter = battle.Units[1];
            for (int i = 0; i < 9 * BattleSim.TicksPerSecond && !battle.IsOver; i++)
            {
                battle.Step();
            }
            Assert(fighter.OffGridTicks > 0, "狮鹫骑士应已离开覆盖范围");
            Assert(fighter.Supplied, "离网 9 秒内仍算有魔网");
            for (int i = 0; i < 3 * BattleSim.TicksPerSecond && !battle.IsOver; i++)
            {
                battle.Step();
            }
            Assert(!fighter.IsAlive || !fighter.Supplied || fighter.InCoverage, "离网超过 10 秒后应断魔网");
        }

        private static int Duel(string attacker, int na, string defender, int nd, ulong seed)
        {
            Formation player = WithSource(0).Add("relay", 0, 4).Add("relay", 0, 1).Add("relay", 0, 7);
            Formation enemy = WithSource(1).Add("hive_node", 0, 4).Add("hive_node", 0, 1).Add("hive_node", 0, 7);
            for (int i = 0; i < na; i++)
            {
                player.Add(attacker, 1, 1 + i * 2 % 8);
            }
            for (int i = 0; i < nd; i++)
            {
                enemy.Add(defender, 1, 1 + i * 2 % 8);
            }
            var battle = new BattleSim(new BattleSetup { Player = player, Enemy = enemy, Seed = seed });
            battle.RunToEnd();
            return battle.Winner;
        }

        private static void AssertWinsAcrossSeeds(string a, int na, string b, int nb)
        {
            int wins = 0;
            for (ulong seed = 1; seed <= 20; seed++)
            {
                if (Duel(a, na, b, nb, seed) == BoardLayout.PlayerSide)
                {
                    wins++;
                }
            }
            Info(na + " " + UnitCatalog.Get(a).DisplayName + " 对 " + nb + " " + UnitCatalog.Get(b).DisplayName + "：20 局胜 " + wins);
            Assert(wins >= 18, "胜场不足 18");
        }

        private static void AntiAirBeatsAir() => AssertWinsAcrossSeeds("pd_ship", 2, "locust", 4);

        private static void HeavyBeatsLight() => AssertWinsAcrossSeeds("battleship", 1, "ripper", 3);

        private static void AirBeatsHeavy() => AssertWinsAcrossSeeds("strike_craft", 3, "crawler", 1);

        private static void OrbitalStrike()
        {
            Encounter e = Fixtures.WreckFieldAmbush();
            var battle = new BattleSim(e.CreateSetup(e.SuggestedPlayer, 5));
            // Wait until 4 supply points have accumulated.
            while (!battle.CanCast(0, CommanderSkill.OrbitalStrike))
            {
                battle.Step();
            }
            Fix before = battle.SupplyPoints(0);
            Unit target = null;
            foreach (Unit u in battle.Units)
            {
                if (u.Side == 1 && u.Def.Id == "acid_turret")
                {
                    target = u;
                    break;
                }
            }
            Fix hpBefore = target.Hp + target.Shield;
            battle.Issue(Command.Cast(0, CommanderSkill.OrbitalStrike, target.Position));
            battle.Step();
            Assert(battle.SupplyPoints(0) < before, "应扣除法术点数");
            Assert(battle.SkillCooldownTicks(0, CommanderSkill.OrbitalStrike) > 0, "应进入冷却");
            for (int i = 0; i < CommanderSkillDef.StrikeDelayTicks + 1; i++)
            {
                battle.Step();
            }
            Assert(!target.IsAlive || target.Hp + target.Shield < hpBefore - Fix.FromInt(100), "目标应受到陨星术伤害");
        }

        private static void SkillNeedsSupplyPoints()
        {
            Encounter e = Fixtures.WreckFieldAmbush();
            var battle = new BattleSim(e.CreateSetup(e.SuggestedPlayer, 5));
            Assert(!battle.CanCast(0, CommanderSkill.OrbitalStrike), "开局 2 点补给不足以释放陨星术");
            battle.Issue(Command.Cast(0, CommanderSkill.OrbitalStrike, new FixVec2(4, 0)));
            battle.Step();
            foreach (BattleEvent ev in battle.Events)
            {
                Assert(ev.Type != BattleEventType.SkillCast, "不应释放成功");
            }
        }

        private static void BattlesEndInTime()
        {
            Encounter e = Fixtures.WreckFieldAmbush();
            int min = int.MaxValue, max = 0;
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var battle = new BattleSim(e.CreateSetup(e.SuggestedPlayer, seed));
                battle.RunToEnd();
                int s = battle.Tick / BattleSim.TicksPerSecond;
                Assert(battle.IsOver && s <= e.TimeLimitSeconds, "超时");
                min = Math.Min(min, s);
                max = Math.Max(max, s);
            }
            Info("默认阵型对古战场伏击：每局 " + min + "~" + max + " 秒");
        }

        private static void ForecastIsFastAndBalanced()
        {
            Encounter e = Fixtures.WreckFieldAmbush();
            var sw = Stopwatch.StartNew();
            ForecastResult r = BattleForecast.Run(e, e.SuggestedPlayer);
            sw.Stop();
            Info("推演 100 场用时 " + sw.ElapsedMilliseconds + " 毫秒；胜率 " + Math.Round(r.WinRate * 100) + "% ±" + Math.Round(r.Margin * 100)
                + "%，平均 " + Math.Round(r.AverageSeconds) + " 秒，平均损失 " + Math.Round(r.AverageLosses, 1));
            foreach (string reason in r.LossReasons)
            {
                Info("败因：" + reason);
            }
            Info("建议：" + r.Advice);
            Assert(sw.ElapsedMilliseconds < 5000, "推演太慢");
            Assert(r.WinRate >= 0.25 && r.WinRate <= 0.85, "默认阵型胜率应在 25%~85% 之间，实际 " + r.WinRate);
        }

        private static void SupplyMatters()
        {
            Encounter e = Fixtures.WreckFieldAmbush();
            Formation noRelays = e.SuggestedPlayer.Clone();
            noRelays.Placements.RemoveAll(p => p.IsRelay);
            double with = BattleForecast.Run(e, e.SuggestedPlayer).WinRate;
            double without = BattleForecast.Run(e, noRelays).WinRate;
            Info("有法力水晶 " + Math.Round(with * 100) + "%，没有法力水晶 " + Math.Round(without * 100) + "%");
            Assert(without < with - 0.1, "去掉法力水晶后胜率应明显下降");
        }
    }
}
