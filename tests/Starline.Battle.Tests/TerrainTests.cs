using System.Collections.Generic;
using Starline.Battle;

namespace Starline.Battle.Tests
{
    /// <summary>Battlefield environments and tiles (GDD 5.5).</summary>
    internal static partial class Program
    {
        private static readonly string[] TerrainIds = { "plains", "forest", "desert", "lake" };

        private static void RunTerrainTests()
        {
            Run("地形：配置表读取四种环境、十种地块和角色阵营", TerrainCatalogLoads);
            Run("地形：手写布局解析", TerrainLayoutParses);
            Run("地形：同种子生成相同布局，双方数量相等，满足布阵约束", TerrainGeneratorRules);
            Run("地形：环境效果按阵营和职业生效，叠加后有上限", TerrainEffectsFilter);
            Run("地形：浅水减速，飞骑不受地块影响", ShallowsSlowGroundOnly);
            Run("地形：高地加远程射程", HighGroundRange);
            Run("地形：深水不能布阵，枯竭区角上不能放法力水晶", TerrainDeployRules);
            Run("地形：枯竭区断魔网，亡灵不受影响", DrainedCutsNetwork);
            Run("地形：水面导魔加连线距离，法力泉加覆盖", WaterAndSpringExtendNetwork);
            Run("地形：亡灵在骨堆上回血", BonePileHealsUndead);
            Run("地形：带地形的战斗同种子结果相同", TerrainBattleIsDeterministic);
            Run("平衡模拟：四种环境对默认战局的影响", TerrainBalanceSimulation);
        }

        private static void TerrainCatalogLoads()
        {
            Assert(TerrainCatalog.Tiles.Count == 10, "应有 10 种地块，实际 " + TerrainCatalog.Tiles.Count);
            foreach (string id in TerrainIds)
            {
                Assert(TerrainCatalog.Get(id).Name.Length > 0, id + " 缺少名字");
            }
            TerrainDef forest = TerrainCatalog.Get("forest");
            Assert(forest.Effects.Count == 3 && forest.Effects[1].Stat == TerrainStat.Range && forest.Effects[1].Value == -Fix.One, "森林的远程射程 −1");
            Assert(TerrainCatalog.GetTile("high_ground").Effects[1].Value == Fix.FromRatio(85, 100), "高地受到远程伤害 0.85");
            Assert(TerrainCatalog.GetTile("deep_water").BlocksDeploy, "深水不能布阵");
            Assert(UnitCatalog.Get("frigate").Faction == Faction.Rangers, "游侠属于边境游侠会");
            Assert(UnitCatalog.Get("ripper").Faction == Faction.Undead, "食尸鬼属于亡灵");
            Assert(UnitCatalog.Get("supply_ship").Faction == Faction.None, "源晶塔没有阵营");
        }

        private static void TerrainLayoutParses()
        {
            JsonValue half = Json.Parse("[\"~~......\", \"^.......\", \"........\", \"........\", \"........\", \".......#\"]");
            var layout = new TerrainLayout();
            layout.ParseHalf(BoardLayout.EnemySide, half);
            Assert(layout.TileAt(1, 0, 1) == TerrainCatalog.GetTile("shallows"), "敌方 (0,1) 是浅水");
            Assert(layout.TileAt(1, 5, 7).Id == "deep_water", "敌方 (5,7) 是深水");
            Assert(layout.TileAt(0, 0, 1) == null, "我方半场为空");
            Assert(layout.Describe(1) == "~~......\n^.......\n........\n........\n........\n.......#", "Describe 与输入一致");
            // Enemy cell (1, 0) spans x 2..4, y -8..-6.
            Assert(layout.TileAtPosition(new FixVec2(3, -7)).Id == "high_ground", "世界坐标查到高地");
            Assert(layout.TileAtPosition(new FixVec2(-3, -7)) == null, "我方同位置为空");
            Assert(layout.TileAtPosition(new FixVec2(3, -9)) == null, "棋盘外没有地块");
        }

        private static void TerrainGeneratorRules()
        {
            foreach (string id in TerrainIds)
            {
                TerrainDef def = TerrainCatalog.Get(id);
                TerrainLayout first = TerrainGenerator.Generate(def, 12345);
                Assert(first.ComputeHash() == TerrainGenerator.Generate(def, 12345).ComputeHash(), id + "：同种子布局应相同");
                Assert(first.ComputeHash() != TerrainGenerator.Generate(def, 54321).ComputeHash(), id + "：不同种子布局应不同");

                for (ulong seed = 1; seed <= 200; seed++)
                {
                    TerrainLayout layout = TerrainGenerator.Generate(def, seed);
                    foreach (TilePaletteEntry entry in def.Palette)
                    {
                        int p = layout.Count(0, entry.Tile);
                        int e = layout.Count(1, entry.Tile);
                        Assert(p == e, id + " 种子 " + seed + "：双方 " + entry.Tile.Name + " 数量应相等（" + p + " / " + e + "）");
                        Assert(p <= entry.Max, id + " 种子 " + seed + "：" + entry.Tile.Name + " 超过上限");
                    }
                    for (int side = 0; side < 2; side++)
                    {
                        for (int d = 0; d < BoardLayout.Depth; d++)
                        {
                            for (int r = 0; r < BoardLayout.Width; r++)
                            {
                                Assert(!TerrainGenerator.IsReserved(d, r) || layout.TileAt(side, d, r) == null, id + "：源晶塔区域应保持空白");
                            }
                        }
                        Assert(TerrainGenerator.BlockedCount(layout, side) <= TerrainGenerator.MaxBlockedPerSide, id + "：不能布阵的格子过多");
                        Assert(TerrainGenerator.OpenFrontCells(layout, side) >= TerrainGenerator.MinOpenFrontCells, id + "：前排空位不足");
                        Assert(TerrainGenerator.HasOpenSquare(layout, side), id + "：没有留给大型单位的 2×2 空地");
                    }
                }
            }
            Info("湖心枢纽（种子 7）我方半场，第一行是前排：\n" + Indent(TerrainGenerator.Generate(TerrainCatalog.Get("lake"), 7).Describe(0)));
        }

        private static string Indent(string text) => "         " + text.Replace("\n", "\n         ");

        private static void TerrainEffectsFilter()
        {
            var forest = new BattleTerrain { Def = TerrainCatalog.Get("forest"), Layout = new TerrainLayout() };
            TerrainModifiers ranger = TerrainModifiers.For(UnitCatalog.Get("frigate"), forest, null);
            TerrainModifiers knight = TerrainModifiers.For(UnitCatalog.Get("battleship"), forest, null);
            TerrainModifiers mage = TerrainModifiers.For(UnitCatalog.Get("missile_ship"), forest, null);
            TerrainModifiers ghoul = TerrainModifiers.For(UnitCatalog.Get("ripper"), forest, null);
            Assert(ranger.Speed == Fix.FromRatio(115, 100), "游侠会在森林移速 +15%");
            Assert(knight.Speed == Fix.FromRatio(9, 10), "重装在森林移速 −10%");
            Assert(mage.RangeBonus == -BoardLayout.CellSize && mage.ApplyRange(Fix.FromInt(12)) == Fix.FromInt(10), "远程在森林射程 −1 格");
            Assert(ghoul.Speed == Fix.One && ghoul.RangeBonus == Fix.Zero, "亡灵轻装在森林不受影响");

            var desert = new BattleTerrain { Def = TerrainCatalog.Get("desert"), Layout = new TerrainLayout() };
            Assert(TerrainModifiers.For(UnitCatalog.Get("frigate"), desert, null).Speed == Fix.One, "游侠会不受沙地减速");
            Assert(TerrainModifiers.For(UnitCatalog.Get("battleship"), desert, null).Speed == Fix.FromRatio(9, 10), "骑士在沙地减速");
            Assert(TerrainModifiers.For(UnitCatalog.Get("strike_craft"), desert, null).Speed == Fix.One, "飞骑不受沙地减速");
            Assert(TerrainModifiers.For(UnitCatalog.Get("missile_ship"), desert, null).ManaRegen == Fix.FromRatio(6, 10), "法力枯竭");
            Assert(TerrainModifiers.For(UnitCatalog.Get("acid_turret"), desert, null).ManaRegen == Fix.One, "亡灵不受法力枯竭影响");

            // Desert −10% and quicksand −40% multiply to 0.54; deep water would hit the 0.4 floor.
            TileDef quicksand = TerrainCatalog.GetTile("quicksand");
            Assert(TerrainModifiers.For(UnitCatalog.Get("battleship"), desert, quicksand).Speed == Fix.FromRatio(9, 10) * Fix.FromRatio(6, 10), "沙地与流沙叠乘");
            Assert(TerrainModifiers.For(UnitCatalog.Get("frigate"), desert, quicksand).Speed == Fix.One, "游侠会不受流沙影响");
            TerrainModifiers deep = TerrainModifiers.For(UnitCatalog.Get("battleship"), desert, TerrainCatalog.GetTile("deep_water"));
            Assert(deep.Speed == Fix.FromRatio(2, 5), "移速下限 40%");
            Assert(TerrainModifiers.For(UnitCatalog.Get("relay"), desert, quicksand).Speed == Fix.One, "建筑不受地形影响");
            Assert(TerrainModifiers.For(UnitCatalog.Get("frigate"), null, null).Speed == Fix.One, "没有地形时不变");
        }

        /// <summary>A one-on-one setup far apart so units walk for a while.</summary>
        private static BattleSim Walker(string unitId, BattleTerrain terrain)
        {
            Formation player = WithSource(0).Add(unitId, 1, 0);
            Formation enemy = WithSource(1).Add("crawler", 5, 7);
            return new BattleSim(new BattleSetup { Player = player, Enemy = enemy, Seed = 1, Terrain = terrain });
        }

        private static Fix FirstStepDistance(string unitId, BattleTerrain terrain)
        {
            BattleSim battle = Walker(unitId, terrain);
            Unit unit = battle.Units[1];
            FixVec2 start = unit.Position;
            battle.Step();
            return FixVec2.Distance(start, unit.Position);
        }

        private static BattleTerrain Plains(params (int side, int depth, int row, string tile)[] tiles)
        {
            var layout = new TerrainLayout();
            foreach (var t in tiles)
            {
                layout.Set(t.side, t.depth, t.row, TerrainCatalog.GetTile(t.tile));
            }
            return new BattleTerrain { Def = TerrainCatalog.Get("plains"), Layout = layout };
        }

        private static void ShallowsSlowGroundOnly()
        {
            BattleTerrain water = Plains((0, 1, 0, "shallows"));
            Fix dry = FirstStepDistance("frigate", null);
            Fix wet = FirstStepDistance("frigate", water);
            Assert(wet < dry, "浅水里的游侠应更慢");
            Assert(Fix.Abs(wet - dry * Fix.FromRatio(7, 10)) < Fix.FromRatio(1, 100), "浅水移速应为 70%：" + wet + " / " + dry);
            Assert(FirstStepDistance("strike_craft", water) == FirstStepDistance("strike_craft", null), "狮鹫骑士飞过浅水不减速");
            Info("游侠第一帧移动：陆地 " + dry + "，浅水 " + wet);
        }

        private static void HighGroundRange()
        {
            BattleTerrain hill = Plains((0, 1, 0, "high_ground"));
            BattleSim battle = Walker("missile_ship", hill);
            Unit mage = battle.Units[1];
            Assert(mage.Range == UnitCatalog.Get("missile_ship").Range + BoardLayout.CellSize, "高地上奥术法师射程 +1 格");
            Assert(mage.Terrain.RangedDamageTaken == Fix.FromRatio(85, 100), "高地上受到远程伤害 −15%");

            var forestHill = new BattleTerrain { Def = TerrainCatalog.Get("forest"), Layout = hill.Layout };
            Unit forestMage = Walker("missile_ship", forestHill).Units[1];
            Assert(forestMage.Range == UnitCatalog.Get("missile_ship").Range, "森林 −1 与高地 +1 抵消");
        }

        private static void TerrainDeployRules()
        {
            BattleTerrain terrain = Plains((0, 1, 1, "deep_water"), (0, 3, 6, "drained"), (1, 0, 0, "deep_water"));
            Formation f = WithSource(0);
            Assert(!FormationRules.CanPlace(f, UnitCatalog.Frigate, 1, 1, terrain, 0), "深水不能布阵");
            Assert(FormationRules.CanPlace(f, UnitCatalog.Frigate, 1, 1), "不带地形时可以放");
            Assert(!FormationRules.CanPlace(f, UnitCatalog.Get("battleship"), 0, 0, terrain, 0), "2×2 压到深水不能放");
            Assert(FormationRules.CanPlace(f, UnitCatalog.Frigate, 0, 0, terrain, 0), "敌方半场的深水不影响我方同位置");
            Assert(!FormationRules.CanPlace(f, UnitCatalog.Frigate, 0, 0, terrain, 1), "敌方半场的深水挡住敌方布阵");
            Assert(!FormationRules.CanPlace(f, UnitCatalog.Relay, 3, 6, terrain, 0), "枯竭区角上不能放法力水晶");
            Assert(!FormationRules.CanPlace(f, UnitCatalog.Relay, 4, 7, terrain, 0), "枯竭区另一角也不行");
            Assert(FormationRules.CanPlace(f, UnitCatalog.Relay, 2, 6, terrain, 0), "不挨着枯竭区的交叉点可以");
            Formation bad = WithSource(0).Add("frigate", 1, 1);
            Assert(FormationRules.Validate(bad, 99, terrain).Count > 0, "站在深水上的阵型应报错");
            Assert(FormationRules.Validate(bad, 99).Count == 0, "不带地形时合法");
        }

        private static void DrainedCutsNetwork()
        {
            // Both units sit right next to the source port, so they would be covered on open ground.
            BattleTerrain terrain = Plains((0, 3, 3, "drained"), (1, 3, 3, "drained"));
            Formation player = WithSource(0).Add("missile_ship", 3, 3);
            Formation enemy = WithSource(1).Add("acid_turret", 3, 3);
            var battle = new BattleSim(new BattleSetup { Player = player, Enemy = enemy, Seed = 1, Terrain = terrain });
            Unit mage = battle.Units[1];
            Unit turret = battle.Units[3];
            Assert(mage.Def.Id == "missile_ship" && turret.Def.Id == "acid_turret", "测试单位");
            Assert(!mage.InCoverage, "站在枯竭区的奥术法师不算接入魔网");
            Assert(turret.InCoverage, "亡灵不受枯竭区影响");
            var open = new BattleSim(new BattleSetup { Player = player, Enemy = enemy, Seed = 1 });
            Assert(open.Units[1].InCoverage, "没有枯竭区时已接入");
            Placement p = player.Placements[1];
            Assert(!SupplyNetwork.ForFormation(player, 0, terrain).CoversPlacement(p, 0), "布阵预览也显示未接入");
        }

        private static void WaterAndSpringExtendNetwork()
        {
            // Source port at (4, 4); a crystal 4 cells away on the same line is out of normal range.
            Formation f = WithSource(0).Add("relay", 0, 4);
            Assert(!SupplyNetwork.ForFormation(f, 0).Nodes[1].Connected, "平地上 4 格连不上");
            BattleTerrain water = Plains((0, 0, 4, "shallows"));
            SupplyNetwork wet = SupplyNetwork.ForFormation(f, 0, water);
            Assert(wet.Nodes[1].Connected, "水边的法力水晶连线距离 +1，4 格能连上");

            Formation g = WithSource(0).Add("relay", 2, 4).Add("frigate", 0, 1);
            Placement far = g.Placements[2];
            Assert(!SupplyNetwork.ForFormation(g, 0).CoversPlacement(far, 0), "平地上覆盖不到 (0,1)");
            BattleTerrain spring = Plains((0, 2, 3, "mana_spring"));
            Assert(SupplyNetwork.ForFormation(g, 0, spring).CoversPlacement(far, 0), "法力泉让覆盖 +1 格，覆盖到 (0,1)");
        }

        private static void BonePileHealsUndead()
        {
            BattleTerrain terrain = Plains((1, 5, 0, "bone_pile"), (0, 5, 0, "bone_pile"));
            Formation player = WithSource(0).Add("frigate", 5, 0);
            Formation enemy = WithSource(1).Add("ripper", 5, 0);
            var battle = new BattleSim(new BattleSetup { Player = player, Enemy = enemy, Seed = 1, Terrain = terrain });
            Unit ranger = battle.Units[1];
            Unit ghoul = battle.Units[3];
            ranger.Hp = ranger.MaxHp * Fix.Half;
            ghoul.Hp = ghoul.MaxHp * Fix.Half;
            Fix rangerBefore = ranger.Hp;
            Fix ghoulBefore = ghoul.Hp;
            battle.Step();
            Assert(ghoul.Hp > ghoulBefore, "亡灵在骨堆上回血");
            Assert(ranger.Hp == rangerBefore, "游侠在骨堆上不回血");
        }

        private static void TerrainBattleIsDeterministic()
        {
            Encounter e = Fixtures.WreckFieldAmbush();
            e.Terrain = new BattleTerrain { Def = TerrainCatalog.Get("forest"), Layout = TerrainGenerator.Generate(TerrainCatalog.Get("forest"), 3) };
            var a = new BattleSim(e.CreateSetup(e.SuggestedPlayer, 42));
            var b = new BattleSim(e.CreateSetup(e.SuggestedPlayer, 42));
            while (!a.IsOver)
            {
                a.Step();
                b.Step();
                Assert(a.ComputeStateHash() == b.ComputeStateHash(), "第 " + a.Tick + " 帧状态不一致");
            }
            e.Terrain = null;
            var plain = new BattleSim(e.CreateSetup(e.SuggestedPlayer, 42));
            plain.RunToEnd();
            Assert(plain.ComputeStateHash() != a.ComputeStateHash(), "地形应改变战斗过程");
        }

        /// <summary>
        /// Two measurements on the fixed mock-up battle. Environment effects alone (no tiles) must
        /// not move the win rate by more than 15 points. Tiles are reported across ten generated
        /// layouts: one fixed formation that ignores the tiles swings a lot between layouts (a real
        /// player adapts in deployment, and the forecast shows it), so only the spread is printed.
        /// </summary>
        private static void TerrainBalanceSimulation()
        {
            Encounter e = Fixtures.WreckFieldAmbush();
            const int runs = 40;
            e.Terrain = null;
            double baseline = BattleForecast.Run(e, e.SuggestedPlayer, runs).WinRate;
            Info("没有地形：胜率 " + Percent(baseline));
            var tooStrong = new List<string>();
            foreach (string id in TerrainIds)
            {
                TerrainDef def = TerrainCatalog.Get(id);
                e.Terrain = new BattleTerrain { Def = def, Layout = new TerrainLayout() };
                double effectsOnly = BattleForecast.Run(e, e.SuggestedPlayer, runs).WinRate;

                var rates = new List<string>();
                double total = 0;
                int layouts = 0;
                for (ulong seed = 1; layouts < 10 && seed < 200; seed++)
                {
                    var terrain = new BattleTerrain { Def = def, Layout = TerrainGenerator.Generate(def, seed) };
                    // Skip layouts the fixed formations cannot stand on.
                    if (FormationRules.Validate(e.SuggestedPlayer, e.CommandLimit, terrain).Count > 0
                        || FormationRules.Validate(e.Enemy, 99, terrain, BoardLayout.EnemySide).Count > 0)
                    {
                        continue;
                    }
                    e.Terrain = terrain;
                    double rate = BattleForecast.Run(e, e.SuggestedPlayer, runs).WinRate;
                    rates.Add(Percent(rate));
                    total += rate;
                    layouts++;
                }
                Assert(layouts > 0, id + "：没有可用的布局");
                Info(def.Name + "：只算环境效果 " + Percent(effectsOnly) + "；加上地块平均 " + Percent(total / layouts)
                    + "（" + string.Join("、", rates) + "）");
                if (System.Math.Abs(effectsOnly - baseline) > 0.15)
                {
                    tooStrong.Add(def.Name);
                }
            }
            e.Terrain = null;
            Assert(tooStrong.Count == 0, string.Join("、", tooStrong) + " 的环境效果让胜率变化超过 15 个百分点");
        }

        private static string Percent(double rate) => (int)System.Math.Round(rate * 100) + "%";
    }
}
