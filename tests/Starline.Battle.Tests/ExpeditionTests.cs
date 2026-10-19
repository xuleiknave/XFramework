using System.Collections.Generic;
using Starline.Battle;
using Starline.Expedition;

namespace Starline.Battle.Tests
{
    internal static partial class Program
    {
        private static void StarScaling()
        {
            var player = new Formation().Add("supply_ship", 4, 3)
                .Add("frigate", 0, 1).Add("frigate", 0, 3, 2).Add("frigate", 0, 5, 3);
            var enemy = new Formation().Add("hive", 4, 3).Add("ripper", 0, 3);
            var battle = new BattleSim(new BattleSetup { Player = player, Enemy = enemy, Seed = 1 });
            var frigates = new List<Unit>();
            foreach (Unit u in battle.Units)
            {
                if (u.Def.Id == "frigate")
                {
                    frigates.Add(u);
                }
            }
            Fix baseHp = UnitCatalog.Frigate.MaxHp;
            Assert(frigates[0].MaxHp == baseHp && frigates[0].Hp == baseHp, "★1 应为基础耐久");
            Assert(frigates[1].MaxHp == baseHp * Fix.FromRatio(16, 10), "★2 耐久应为 1.6 倍");
            Assert(frigates[2].Damage == UnitCatalog.Frigate.Damage * Fix.FromRatio(22, 10), "★3 伤害应为 2.2 倍");
            Info("护卫舰耐久 ★1/★2/★3：" + frigates[0].MaxHp.ToIntFloor() + " / " + frigates[1].MaxHp.ToIntFloor() + " / " + frigates[2].MaxHp.ToIntFloor());
        }

        private static void MergeKeepsPlacement()
        {
            var fleet = new Fleet();
            fleet.Formation.Add("supply_ship", 4, 3);
            OwnedShip a = fleet.Add("frigate").Ship;
            fleet.Add("frigate");
            fleet.Deploy(a, 0, 2);
            AddShipResult result = fleet.Add("frigate");
            Assert(result.MergedTo.Count == 1 && result.MergedTo[0] == 2, "应合成 ★2");
            Assert(fleet.Ships.Count == 1 && fleet.Ships[0].Star == 2, "舰队应只剩 1 艘 ★2 护卫舰");
            Placement p = fleet.Formation.FindShip(result.Ship.Id);
            Assert(p != null && p.Depth == 0 && p.Row == 2 && p.Star == 2, "★2 舰应接替原来的上阵位置");
            Assert(fleet.Formation.Count("frigate") == 1, "棋盘上应只剩 1 艘护卫舰");
        }

        private static void ChainMerge()
        {
            var fleet = new Fleet();
            for (int i = 0; i < 6; i++)
            {
                fleet.Add("pd_ship");
            }
            fleet.Add("pd_ship");
            fleet.Add("pd_ship");
            AddShipResult result = fleet.Add("pd_ship");
            Assert(result.MergedTo.Count == 2 && result.Ship.Star == 3, "第 9 艘应连锁合成到 ★3");
            Assert(fleet.Ships.Count == 1, "9 艘 ★1 应合成 1 艘 ★3");
        }

        private static void StarMapIsConnected()
        {
            for (ulong seed = 1; seed <= 50; seed++)
            {
                StarMap map = StarMap.GenerateAct1(seed);
                Assert(map.Boss.Type == NodeType.Boss, "最后一层应是 Boss");
                var reachable = new HashSet<int>();
                foreach (MapNode n in map.Layers[0])
                {
                    reachable.Add(n.Id);
                }
                for (int layer = 0; layer + 1 < map.Layers.Count; layer++)
                {
                    foreach (MapNode n in map.Layers[layer])
                    {
                        Assert(n.Next.Count >= 1, "每个节点至少有 1 条出路");
                        if (n.IsBattle)
                        {
                            Assert(n.EncounterKey != null, "战斗节点要有敌方阵容");
                        }
                        foreach (int next in n.Next)
                        {
                            Assert(map.Get(next).Layer == layer + 1, "连线只能通向下一层");
                            if (reachable.Contains(n.Id))
                            {
                                reachable.Add(next);
                            }
                        }
                    }
                }
                Assert(reachable.Count == map.Nodes.Count, "种子 " + seed + "：有节点到不了");
            }
            StarMap sample = StarMap.GenerateAct1(7);
            var lines = new List<string>();
            foreach (List<MapNode> layer in sample.Layers)
            {
                lines.Add(string.Join("/", layer.ConvertAll(n => n.Type.ToString())));
            }
            Info("种子 7 的星图：" + string.Join(" → ", lines));
        }

        private static void DurabilityRules()
        {
            var run = new ExpeditionRun(3);
            run.Enter(run.Reachable()[0]);
            run.ReportBattle(false);
            Assert(run.Durability == ExpeditionRun.MaxDurability - 1 && run.State == RunState.Map, "普通战失败应 −1 并回到星图");
            Assert(run.Rewards.Count == 0, "失败没有奖励");

            // Walk to the boss, losing nothing else.
            while (run.State == RunState.Map && run.Reachable()[0].Type != NodeType.Boss)
            {
                MapNode node = run.Reachable()[0];
                run.Enter(node);
                if (run.State == RunState.Battle)
                {
                    run.ReportBattle(true);
                    run.ChooseReward(-1);
                }
                else
                {
                    run.ChooseSupply(false);
                }
            }
            run.Enter(run.Reachable()[0]);
            Assert(run.Current.Type == NodeType.Boss, "应到达 Boss");
            int before = run.Durability;
            run.ReportBattle(false);
            Assert(run.Durability == before - 2 && run.State == RunState.Battle, "Boss 失败应 −2 并可以重打");
            while (run.State == RunState.Battle)
            {
                run.ReportBattle(false);
            }
            Assert(run.State == RunState.Defeat && run.Durability == 0, "耐久归零应结束远征");
        }

        private static void RewardsAndAutoDeploy()
        {
            var run = new ExpeditionRun(11);
            run.Enter(run.Reachable()[0]);
            run.ReportBattle(true);
            Assert(run.State == RunState.Reward && run.Rewards.Count == 3, "胜利后应有 3 个奖励");
            Assert(run.CommandLimit == ExpeditionRun.BaseCommandLimit + ExpeditionRun.CommandLimitPerBattle, "胜利后指挥点上限提高");
            int ships = run.Fleet.Ships.Count;
            int index = run.Rewards.FindIndex(r => r.Kind == RewardKind.Ship);
            string name = run.Rewards[index].Title;
            run.ChooseReward(index);
            Assert(run.State == RunState.Map, "选完奖励回到星图");
            Assert(run.Fleet.Ships.Count == ships + 1 || run.Messages.Exists(m => m.Contains("合成")), "应获得舰船");
            SupplyNetwork network = SupplyNetwork.ForFormation(run.Fleet.Formation, BoardLayout.PlayerSide);
            int deployed = 0, covered = 0;
            foreach (Placement p in run.Fleet.Formation.Placements)
            {
                if (p.Def.HasWeapon)
                {
                    deployed++;
                    if (network.CoversPlacement(p, BoardLayout.PlayerSide) || p.Def.IsCraft)
                    {
                        covered++;
                    }
                }
            }
            Assert(deployed == 4, "新舰船应自动上阵");
            Assert(covered == deployed, "自动上阵应放在补给覆盖内");
            Assert(FormationRules.Validate(run.Fleet.Formation, run.CommandLimit).Count == 0, "阵型应合法");
            Info("选了 " + name + "：" + string.Join("；", run.Messages));
        }

        private static void EncountersAreValid()
        {
            var keys = new List<string> { EncounterLibrary.Boss };
            foreach (string[] tier in EncounterLibrary.NormalTiers)
            {
                keys.AddRange(tier);
            }
            foreach (string[] tier in EncounterLibrary.EliteTiers)
            {
                keys.AddRange(tier);
            }
            foreach (string key in keys)
            {
                Encounter e = EncounterLibrary.Create(key);
                List<string> errors = FormationRules.Validate(e.Enemy, 999);
                Assert(errors.Count == 0, e.Name + "：" + string.Join("，", errors));
                Assert(!string.IsNullOrEmpty(e.Briefing), e.Name + " 缺少简报");
            }
        }

        /// <summary>
        /// Plays whole Act 1 runs with a naive bot: random route, first reward, auto deploy,
        /// no commander skills. A real player should do clearly better than this.
        /// </summary>
        private static void Act1BalanceSimulation()
        {
            const int runs = 60;
            var fights = new Dictionary<string, int>();
            var wins = new Dictionary<string, int>();
            int cleared = 0, durabilityLeft = 0, bossAttempts = 0;
            for (ulong seed = 1; seed <= runs; seed++)
            {
                var run = new ExpeditionRun(seed);
                var random = new DeterministicRandom(seed + 99);
                ulong battleSeed = seed * 1000;
                while (!run.IsOver)
                {
                    switch (run.State)
                    {
                        case RunState.Map:
                            List<MapNode> options = run.Reachable();
                            run.Enter(options[random.Range(0, options.Count)]);
                            break;
                        case RunState.Battle:
                        {
                            AutoDeploy.FillFormation(run.Fleet, run.CommandLimit);
                            var battle = new BattleSim(run.CurrentEncounter.CreateSetup(run.Fleet.Formation.Clone(), battleSeed++));
                            battle.RunToEnd();
                            string key = run.Current.EncounterKey;
                            Increment(fights, key);
                            if (battle.Winner == BoardLayout.PlayerSide)
                            {
                                Increment(wins, key);
                            }
                            if (run.Current.Type == NodeType.Boss)
                            {
                                bossAttempts++;
                            }
                            run.ReportBattle(battle.Winner == BoardLayout.PlayerSide);
                            break;
                        }
                        case RunState.Reward:
                            run.ChooseReward(0);
                            break;
                        case RunState.Supply:
                            run.ChooseSupply(run.Durability < ExpeditionRun.MaxDurability);
                            break;
                    }
                }
                if (run.State == RunState.Victory)
                {
                    cleared++;
                    durabilityLeft += run.Durability;
                }
            }

            var keys = new List<string>(fights.Keys);
            keys.Sort((a, b) => Order(a) - Order(b));
            foreach (string key in keys)
            {
                wins.TryGetValue(key, out int w);
                Info(EncounterLibrary.Create(key).Name + "：" + w + " / " + fights[key] + " 胜（" + (100 * w / fights[key]) + "%）");
            }
            Info("通关 " + cleared + " / " + runs + "，Boss 共挑战 " + bossAttempts + " 次，通关时平均剩余耐久 "
                + (cleared > 0 ? (durabilityLeft / (double)cleared).ToString("0.0") : "-"));
            Assert(cleared >= runs / 5 && cleared <= runs * 9 / 10, "自动玩家通关率应在 20%~90%");
        }

        private static int Order(string key)
        {
            if (key == EncounterLibrary.Boss)
            {
                return 100;
            }
            for (int t = 0; t < EncounterLibrary.NormalTiers.Length; t++)
            {
                if (System.Array.IndexOf(EncounterLibrary.NormalTiers[t], key) >= 0)
                {
                    return t * 10;
                }
            }
            return 50;
        }

        private static void Increment(Dictionary<string, int> counts, string key)
        {
            counts.TryGetValue(key, out int n);
            counts[key] = n + 1;
        }
    }
}
