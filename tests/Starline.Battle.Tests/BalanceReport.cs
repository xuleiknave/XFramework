using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Starline.Battle;

namespace Starline.Battle.Tests
{
    /// <summary>
    /// Writes docs/balance-sheet.md: every number that decides a fight (factions, roles, stars,
    /// terrain) gathered from the JSON tables, plus pure-logic battles that check whether
    /// the numbers hold up. Run with: dotnet run --project tests/Starline.Battle.Tests -- --balance
    /// </summary>
    internal static class BalanceReport
    {
        /// <summary>Command points each side spends in the duels.</summary>
        public const int Budget = 12;
        /// <summary>A score this far from 50% after terrain is flagged (GDD 5.5).</summary>
        private const double TerrainFlag = 0.15;
        private const int MaxCount = 16;

        /// <summary>Bosses are tuned per fight and stay out of the duels.</summary>
        private static readonly HashSet<string> Excluded = new HashSet<string> { "wreck_fortress" };

        private static readonly int[] RowOrder = { 3, 4, 2, 5, 1, 6, 0, 7 };

        private static int _seeds = 6;
        private static int _battles;

        public static List<UnitDef> CombatUnits()
        {
            var list = new List<UnitDef>();
            foreach (UnitDef def in UnitCatalog.All)
            {
                if (def.HasWeapon && !Excluded.Contains(def.Id))
                {
                    list.Add(def);
                }
            }
            return list;
        }

        // ------------------------------------------------------------------
        // Battles

        /// <summary>A side with the standard network (source and three crystals) and the given units.</summary>
        public static Formation Squad(params (string id, int count, int star)[] groups)
        {
            var f = new Formation()
                .Add("supply_ship", 4, 3)
                .Add("relay", 2, 4).Add("relay", 2, 1).Add("relay", 2, 7);
            foreach ((string id, int count, int star) in groups)
            {
                UnitDef def = UnitCatalog.Get(id);
                for (int i = 0; i < count; i++)
                {
                    if (!PlaceOne(f, def, star))
                    {
                        throw new Exception("棋盘放不下 " + count + " 个" + Fixtures.Text(def.Name));
                    }
                }
            }
            return f;
        }

        public static bool Fits(string id, int count)
        {
            var f = new Formation().Add("supply_ship", 4, 3);
            UnitDef def = UnitCatalog.Get(id);
            for (int i = 0; i < count; i++)
            {
                if (!PlaceOne(f, def, 1))
                {
                    return false;
                }
            }
            return true;
        }

        private static int[] DepthOrder(UnitRole role)
        {
            switch (role)
            {
                case UnitRole.Craft: return new[] { 1, 0, 2, 3, 4, 5 };
                case UnitRole.PointDefense: return new[] { 1, 2, 0, 3, 4, 5 };
                case UnitRole.Artillery: return new[] { 3, 2, 1, 4, 5, 0 };
                default: return new[] { 0, 1, 2, 3, 4, 5 };
            }
        }

        private static bool PlaceOne(Formation f, UnitDef def, int star)
        {
            foreach (int d in DepthOrder(def.Role))
            {
                foreach (int r in RowOrder)
                {
                    if (FormationRules.CanPlace(f, def, d, r))
                    {
                        f.Add(def.Id, d, r, star);
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Result for one side, averaged over battles. Win: win 1, draw 0.5, loss 0.
        /// Edge: 0.5 + half the difference in surviving durability (hp + shield of combat units,
        /// as a share of what each side started with), so a narrow win is just above 0.5
        /// and a flawless one is 1. Edge stays smooth where the deterministic kernel turns
        /// small advantages into 0% / 100% win rates.
        /// </summary>
        public struct Outcome
        {
            public double Win;
            public double Edge;

            public static Outcome operator +(Outcome x, Outcome y) => new Outcome { Win = x.Win + y.Win, Edge = x.Edge + y.Edge };
            public static Outcome operator /(Outcome x, double n) => new Outcome { Win = x.Win / n, Edge = x.Edge / n };
        }

        /// <summary>Outcome for <paramref name="a"/>, each seed played from both sides of the board.</summary>
        public static Outcome Score(Formation a, Formation b, BattleTerrain terrain = null, int seeds = 0)
        {
            int n = seeds > 0 ? seeds : _seeds;
            var total = new Outcome();
            for (int s = 1; s <= n; s++)
            {
                total += Play(a, b, terrain, (ulong)s, 0);
                total += Play(b, a, terrain, (ulong)s + 1000, 1);
            }
            return total / (2 * n);
        }

        private static Outcome Play(Formation player, Formation enemy, BattleTerrain terrain, ulong seed, int side)
        {
            var battle = new BattleSim(new BattleSetup { Player = player, Enemy = enemy, Seed = seed, Terrain = terrain });
            battle.RunToEnd();
            _battles++;
            var start = new double[2];
            var left = new double[2];
            foreach (Unit u in battle.AllUnits)
            {
                if (u.Def.HasWeapon)
                {
                    start[u.Side] += u.MaxHp.ToFloat() + u.MaxShield.ToFloat();
                    if (u.IsAlive)
                    {
                        left[u.Side] += u.Hp.ToFloat() + u.Shield.ToFloat();
                    }
                }
            }
            double mine = left[side] / start[side];
            double theirs = left[1 - side] / start[1 - side];
            return new Outcome
            {
                Win = battle.Winner == BattleSim.DrawSide ? 0.5 : battle.Winner == side ? 1 : 0,
                Edge = 0.5 + 0.5 * (mine - theirs),
            };
        }

        /// <summary>
        /// Smallest (fractional) k for which <paramref name="build"/>(k) scores 50% against
        /// <paramref name="opponent"/>, interpolating between the counts either side of it.
        /// </summary>
        private static double BreakEven(Func<int, Formation> build, Formation opponent, int maxK, out bool capped, int minK = 1)
        {
            double previous = 0;
            int last = 0;
            for (int k = minK; k <= maxK; k++)
            {
                Formation a;
                try
                {
                    a = build(k);
                }
                catch (Exception)
                {
                    break;
                }
                last = k;
                double score = Score(a, opponent).Edge;
                if (score >= 0.5)
                {
                    capped = false;
                    return k == minK ? k : (k - 1) + (0.5 - previous) / (score - previous);
                }
                previous = score;
            }
            capped = true;
            return last;
        }

        // ------------------------------------------------------------------
        // Report

        public static int Write(string path, int seeds)
        {
            _seeds = seeds;
            var watch = Stopwatch.StartNew();
            var md = new StringBuilder();
            List<UnitDef> combat = CombatUnits();

            md.AppendLine("# 数值平衡表");
            md.AppendLine();
            md.AppendLine("> 本文件由 `dotnet run --project tests/Starline.Battle.Tests -- --balance` 根据 `Runtime/Resources/StarlineData` 的配置表生成，并用内核跑纯逻辑对战。**不要手改**：改了 JSON 之后重新生成。");
            md.AppendLine("> 解读、结论和调整建议见 [game-design.md](game-design.md) 8.6。");
            md.AppendLine();

            UnitTable(md);
            FactionTable(md, combat);
            CounterMatrix(md);
            StarTable(md);
            TerrainTables(md);

            md.AppendLine("---");
            md.AppendLine();
            md.AppendLine("## 模拟方法");
            md.AppendLine();
            md.AppendLine("- 每方用同一套星络：观星台在后排中间，三颗星石立在第 2 条纵线的 1、4、7 号交叉点，前四排全部接入星络。");
            md.AppendLine("- 角色按职业摆放：重装和轻装从前排往后放，飞骑和对空从第二排开始，远程从第四排开始；每排从中间往两边放。双方阵型镜像。");
            md.AppendLine("- 每组对战用 " + seeds + " 个随机种子，每个种子双方各站一次左边（内核里“我方”和“敌方”的结算顺序不同，见第 6 节），共 " + (2 * seeds) + " 场。\n- **胜率**：胜 1、平 0.5、负 0。**优势分** = 50% + （己方剩余耐久比例 − 对方剩余耐久比例）÷ 2，耐久 = 作战角色的生命 + 护盾：险胜略高于 50%，无损全胜是 100%。内核是确定性的，随机只来自 ±10% 的伤害浮动，势均力敌的对战胜率经常直接是 0% 或 100%，所以找平衡点和比较地形时用优势分。\n- 不使用指挥官技能，不算羁绊（还没实现）。");
            md.AppendLine("- 墨城是 Boss，不参加对战；青丘灵族还没有做出数值的角色。");
            md.AppendLine();

            SideBias(md, combat);
            Dictionary<string, double> value = MarginalValue(md, combat);
            Dictionary<string, int> counts = DuelCounts(combat, value);
            double[,] plain = DuelMatrix(md, combat, counts);
            CounterCheck(md, combat, plain);
            StarCheck(md, combat);
            TerrainCheck(md);
            TileCheck(md, combat, counts);

            md.AppendLine("---");
            md.AppendLine();
            md.AppendLine("共模拟 " + _battles + " 场战斗，用时 " + (watch.ElapsedMilliseconds / 1000) + " 秒。");

            File.WriteAllText(path, md.ToString().Replace("\r\n", "\n"));
            Console.WriteLine("已写入 " + path + "（" + _battles + " 场战斗，" + (watch.ElapsedMilliseconds / 1000) + " 秒）");
            return 0;
        }

        private static string F(double v, string format = "0.#") => v.ToString(format, CultureInfo.InvariantCulture);
        private static string F(Fix v, string format = "0.##") => F((double)v.ToFloat(), format);
        private static string Pct(double score) => F(score * 100, "0") + "%";
        private static string Cells(Fix units) => F(units.ToFloat() / BoardLayout.CellSize.ToFloat(), "0.##");

        private static string FactionName(Faction f)
        {
            switch (f)
            {
                case Faction.Morningstar: return "玄武营";
                case Faction.Rangers: return "白虎游骑";
                case Faction.Silvermoon: return "朱雀司天监";
                case Faction.Undead: return "墨蚀者";
                case Faction.Elves: return "青丘灵族";
                default: return "—";
            }
        }

        private static string RoleName(UnitRole r)
        {
            switch (r)
            {
                case UnitRole.Light: return "轻装";
                case UnitRole.Heavy: return "重装";
                case UnitRole.Craft: return "飞骑";
                case UnitRole.PointDefense: return "对空";
                case UnitRole.Artillery: return "远程";
                default: return "建筑";
            }
        }

        private static double Ehp(UnitDef d) => d.MaxHp.ToFloat() + d.MaxShield.ToFloat();
        private static double Dps(UnitDef d) => d.HasWeapon ? d.Damage.ToFloat() / d.FireIntervalSeconds.ToFloat() : 0;

        private static void UnitTable(StringBuilder md)
        {
            md.AppendLine("## 1. 角色固有属性（units.json）");
            md.AppendLine();
            md.AppendLine("距离换算成格（1 格 = 2 单位）。有效生命 = 生命 + 护盾；每秒伤害不含克制倍率；灵力为“—”表示不消耗灵力。");
            md.AppendLine();
            md.AppendLine("| 角色 | id | 阵营 | 职业 | 尺寸 | 指挥点 | 生命 | 护盾 | 护盾恢复/秒 | 伤害 | 攻击间隔 | 每秒伤害 | 射程（格） | 移速（格/秒） | 灵力 | 回复 1 点（秒） | 离网（秒） | 索敌偏好 |");
            md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (UnitDef d in UnitCatalog.All)
            {
                var prefers = new List<string>();
                foreach (UnitRole r in d.PreferredTargets)
                {
                    prefers.Add(r == UnitRole.Structure ? "星络节点" : RoleName(r));
                }
                string size = d.Size == 0 ? "交叉点" : d.Size == 2 ? "2×2" : "1 格";
                md.AppendLine("| " + Fixtures.Text(d.Name) + " | `" + d.Id + "` | " + FactionName(d.Faction) + " | " + RoleName(d.Role) + " | " + size
                    + " | " + (d.CommandCost > 0 ? d.CommandCost.ToString() : "—")
                    + " | " + F(d.MaxHp) + " | " + F(d.MaxShield) + " | " + F(d.ShieldRegenPerSecond)
                    + " | " + (d.HasWeapon ? F(d.Damage) : "—") + " | " + (d.HasWeapon ? F(d.FireIntervalSeconds) : "—")
                    + " | " + (d.HasWeapon ? F(Dps(d)) : "—") + " | " + (d.HasWeapon ? Cells(d.Range) : "—")
                    + " | " + (d.IsStatic ? "不移动" : Cells(d.Speed)) + " | " + (d.MaxAmmo > 0 ? d.MaxAmmo.ToString() : "—")
                    + " | " + (d.MaxAmmo > 0 ? F(d.AmmoRefillSeconds) : "—") + " | " + (d.OffGridGraceSeconds > Fix.Zero ? F(d.OffGridGraceSeconds) : "—")
                    + " | " + (d.HeavySalvoEvery > 0 ? "每第 " + d.HeavySalvoEvery + " 次攻击双倍伤害；" : "") + (!d.HasWeapon ? "—" : prefers.Count > 0 ? string.Join(" > ", prefers) : "最近") + " |");
            }
            md.AppendLine();
        }

        private static void FactionTable(StringBuilder md, List<UnitDef> combat)
        {
            md.AppendLine("## 2. 阵营");
            md.AppendLine();
            md.AppendLine("已做出数值的角色按阵营汇总（设计文档 8.3 的 45 名角色里，其余的还没有数值）。平均值只算可作战的角色，不算 Boss。");
            md.AppendLine();
            md.AppendLine("| 阵营 | 已有数值的角色 | 职业 | 平均有效生命 | 平均每秒伤害 | 平均射程（格） |");
            md.AppendLine("|---|---|---|---|---|---|");
            foreach (Faction f in new[] { Faction.Morningstar, Faction.Rangers, Faction.Silvermoon, Faction.Undead, Faction.Elves })
            {
                var names = new List<string>();
                var roles = new List<string>();
                double ehp = 0, dps = 0, range = 0;
                foreach (UnitDef d in combat)
                {
                    if (d.Faction != f)
                    {
                        continue;
                    }
                    names.Add(Fixtures.Text(d.Name));
                    if (!roles.Contains(RoleName(d.Role)))
                    {
                        roles.Add(RoleName(d.Role));
                    }
                    ehp += Ehp(d);
                    dps += Dps(d);
                    range += d.Range.ToFloat() / 2;
                }
                int n = names.Count;
                md.AppendLine("| " + FactionName(f) + " | " + (n > 0 ? string.Join("、", names) : "（无）") + " | " + string.Join("、", roles)
                    + " | " + (n > 0 ? F(ehp / n, "0") : "—") + " | " + (n > 0 ? F(dps / n) : "—") + " | " + (n > 0 ? F(range / n) : "—") + " |");
            }
            md.AppendLine();
            md.AppendLine("阵营羁绊（设计文档 8.4）还没有进内核，模拟里不算。阵营目前只通过地形（第 5 节）影响战斗。");
            md.AppendLine();
        }

        private static readonly UnitRole[] CombatRoles = { UnitRole.Light, UnitRole.Heavy, UnitRole.Craft, UnitRole.PointDefense, UnitRole.Artillery };

        private static void CounterMatrix(StringBuilder md)
        {
            md.AppendLine("## 3. 职业克制（counters.json）");
            md.AppendLine();
            md.AppendLine("攻击方（行）对目标（列）的伤害倍率。≥ 1.5 记为克制。");
            md.AppendLine();
            md.AppendLine("| 攻击 \\ 目标 | 轻装 | 重装 | 飞骑 | 对空 | 远程 | 建筑 |");
            md.AppendLine("|---|---|---|---|---|---|---|");
            foreach (UnitRole a in CombatRoles)
            {
                var cells = new List<string>();
                foreach (UnitRole t in new[] { UnitRole.Light, UnitRole.Heavy, UnitRole.Craft, UnitRole.PointDefense, UnitRole.Artillery, UnitRole.Structure })
                {
                    Fix m = CounterTable.Get(a, t);
                    string text = F(m);
                    cells.Add(m >= Fix.FromRatio(3, 2) ? "**" + text + "**" : text);
                }
                md.AppendLine("| " + RoleName(a) + " | " + string.Join(" | ", cells) + " |");
            }
            md.AppendLine();
        }

        private static void StarTable(StringBuilder md)
        {
            md.AppendLine("## 4. 升星（skills.json 的 stars）");
            md.AppendLine();
            md.AppendLine("耐久倍率作用于生命、护盾和护盾恢复。“综合战力” = 耐久 × 伤害；按兰彻斯特平方律，一名综合战力为 p 的角色大约顶 √p 名 ★1。升星碎片见设计文档 4.8。");
            md.AppendLine();
            md.AppendLine("| 星级 | 耐久倍率 | 伤害倍率 | 综合战力 | 约等于几名 ★1（√战力） | 比上一星 | 升到这一星的碎片 | 累计碎片（含招募 10） | 等级上限 |");
            md.AppendLine("|---|---|---|---|---|---|---|---|---|");
            int[] shards = { 10, 20, 40, 80, 120 };
            int total = 0;
            double previous = 1;
            for (int s = 1; s <= UnitDef.MaxStar; s++)
            {
                double hp = UnitDef.HpScale(s).ToFloat();
                double dmg = UnitDef.DamageScale(s).ToFloat();
                double power = hp * dmg;
                total += shards[s - 1];
                md.AppendLine("| ★" + s + " | " + F(hp, "0.0#") + " | " + F(dmg, "0.0#") + " | " + F(power, "0.0#") + " | " + F(Math.Sqrt(power), "0.0#")
                    + " | " + (s == 1 ? "—" : "×" + F(power / previous, "0.00")) + " | " + (s == 1 ? "招募 10" : shards[s - 1].ToString()) + " | " + total + " | " + (10 + 10 * s) + " |");
                previous = power;
            }
            md.AppendLine();
            md.AppendLine("远征里 3 名同星角色合成高一星，最高合成到 ★" + UnitDef.MaxMergeStar + "；★4、★5 只能靠碎片升星。");
            md.AppendLine();
        }

        private static string EffectList(List<TerrainEffect> effects)
        {
            var texts = new List<string>();
            foreach (TerrainEffect e in effects)
            {
                texts.Add(Fixtures.Text(e.Text));
            }
            return texts.Count == 0 ? "无" : string.Join("；", texts);
        }

        private static void TerrainTables(StringBuilder md)
        {
            md.AppendLine("## 5. 地形（terrains.json）");
            md.AppendLine();
            md.AppendLine("同一属性的地形修正合计夹在 50%~140%（移速下限 40%），射程最多 ±2 格且不短于 1 格，和职业克制叠乘（设计文档 5.5）。");
            md.AppendLine();
            md.AppendLine("**战场环境**（整场战斗，双方都吃）");
            md.AppendLine();
            md.AppendLine("| 环境 | id | 效果 | 地块 |");
            md.AppendLine("|---|---|---|---|");
            foreach (TerrainDef t in TerrainCatalog.Terrains)
            {
                var tiles = new List<string>();
                foreach (TilePaletteEntry e in t.Palette)
                {
                    tiles.Add(Fixtures.Text(e.Tile.Name) + " " + e.Min + "~" + e.Max);
                }
                string network = (t.LinkRangeBonus != 0 ? "；星络连线 +" + t.LinkRangeBonus + " 格" : "") + (t.CoverageBonus != 0 ? "；覆盖 +" + t.CoverageBonus + " 格" : "");
                md.AppendLine("| " + Fixtures.Text(t.Name) + " | `" + t.Id + "` | " + EffectList(t.Effects) + network + " | " + string.Join("、", tiles) + " |");
            }
            md.AppendLine();
            md.AppendLine("**地块**（飞骑和建筑不受影响）");
            md.AppendLine();
            md.AppendLine("| 地块 | id | 效果 | 星络 | 谁不受影响 | 能否布阵 |");
            md.AppendLine("|---|---|---|---|---|---|");
            foreach (TileDef t in TerrainCatalog.Tiles)
            {
                var network = new List<string>();
                if (t.RelayLinkBonus > 0)
                {
                    network.Add("角上的星石连线 +" + t.RelayLinkBonus + " 格");
                }
                if (t.RelayCoverageBonus > 0)
                {
                    network.Add("角上的星石覆盖 +" + t.RelayCoverageBonus + " 格");
                }
                if (t.NoNetwork)
                {
                    network.Add("站在上面断星络，角上不能放星石");
                }
                var exempt = new List<string>();
                foreach (Faction f in t.ExemptFactions)
                {
                    exempt.Add(FactionName(f));
                }
                md.AppendLine("| " + Fixtures.Text(t.Name) + " | `" + t.Id + "` | " + EffectList(t.Effects) + " | " + (network.Count > 0 ? string.Join("；", network) : "—")
                    + " | " + (exempt.Count > 0 ? string.Join("、", exempt) : "—") + " | " + (t.BlocksDeploy ? "**不能**" : "能") + " |");
            }
            md.AppendLine();
        }

        // ------------------------------------------------------------------
        // Simulations

        public static Formation Reference(params (string id, int count, int star)[] extra)
        {
            var groups = new List<(string, int, int)> { ("battleship", 1, 1), ("frigate", 1, 1), ("missile_ship", 1, 1), ("pd_ship", 2, 1), ("strike_craft", 2, 1) };
            groups.AddRange(extra);
            return Squad(groups.ToArray());
        }

        private static void SideBias(StringBuilder md, List<UnitDef> combat)
        {
            md.AppendLine("## 6. 模拟：镜像对战的站边偏差");
            md.AppendLine();
            md.AppendLine("双方阵容、站位完全相同，只看站在“我方”（左边）的一方。理想值是 50%；偏离说明内核的结算顺序（我方单位先行动、先结算伤害）在势均力敌时会决定胜负。下面所有对战都让双方各站一次左边来抵消它。");
            md.AppendLine();
            md.AppendLine("| 阵容 | 左边胜率 | 左边优势分 |");
            md.AppendLine("|---|---|---|");
            var lines = new List<(string, Formation)> { ("标准混编", Reference()) };
            foreach (UnitDef d in combat)
            {
                int count = d.CommandCost > 0 ? Budget / d.CommandCost : 6;
                while (count > 1 && !Fits(d.Id, count))
                {
                    count--;
                }
                lines.Add((Fixtures.Text(d.Name) + "×" + count, Squad((d.Id, count, 1))));
            }
            foreach ((string name, Formation f) in lines)
            {
                var score = new Outcome();
                for (int s = 1; s <= 2 * _seeds; s++)
                {
                    score += Play(f, f, null, (ulong)s, 0);
                }
                score /= 2 * _seeds;
                md.AppendLine("| " + name + " | " + Pct(score.Win) + " | " + Pct(score.Edge) + " |");
            }
            md.AppendLine();
        }

        /// <summary>Four command points of mixed units: what the marginal-value test compares against.</summary>
        private static readonly (string id, int count, int star)[] Bundle = { ("frigate", 1, 1), ("pd_ship", 1, 1), ("strike_craft", 1, 1) };
        private const int BundleCost = 4;

        private static Dictionary<string, double> MarginalValue(StringBuilder md, List<UnitDef> combat)
        {
            md.AppendLine("## 7. 模拟：性价比（加进混编队伍的边际价值）");
            md.AppendLine();
            md.AppendLine("双方都以标准混编（1 玄甲将 + 1 游侠 + 1 星术士 + 2 连弩手 + 2 鹤骑，" + Budget + " 指挥点）为底。B 方再加 1 游侠 + 1 连弩手 + 1 鹤骑（" + BundleCost + " 指挥点）；");
            md.AppendLine("A 方改加 k 名某角色，用优势分找到打平的 k（可以是小数），换算成“等价指挥点” = " + BundleCost + " ÷ k。它回答的是“在正常的混编阵容里，这名角色值几个指挥点”。");
            md.AppendLine("“同价替换”一列是 A 方加同样 " + BundleCost + " 指挥点的这种角色时的胜率 / 优势分。");
            md.AppendLine();
            md.AppendLine("| 角色 | 阵营 | 职业 | 现在的指挥点 | 同价替换（胜率 / 优势分） | 打平需要几名 | 等价指挥点 | 偏差 |");
            md.AppendLine("|---|---|---|---|---|---|---|---|");
            Formation opponent = Reference(Bundle);
            var result = new Dictionary<string, double>();
            foreach (UnitDef d in combat)
            {
                string same = "—";
                if (d.CommandCost > 0 && BundleCost % d.CommandCost == 0)
                {
                    Outcome o = Score(Reference((d.Id, BundleCost / d.CommandCost, 1)), opponent);
                    same = Pct(o.Win) + " / " + Pct(o.Edge);
                }
                double k = BreakEven(n => Reference((d.Id, n, 1)), opponent, 8, out bool capped, 0);
                double value = k > 0 ? BundleCost / k : double.PositiveInfinity;
                result[d.Id] = capped ? 0 : value;
                string deviation = "—";
                if (d.CommandCost > 0 && !capped)
                {
                    double ratio = value / d.CommandCost - 1;
                    deviation = (ratio >= 0 ? "+" : "") + F(ratio * 100, "0") + "%" + (Math.Abs(ratio) > 0.25 ? (ratio > 0 ? "（偏强）" : "（偏弱）") : "");
                }
                md.AppendLine("| " + Fixtures.Text(d.Name) + " | " + FactionName(d.Faction) + " | " + RoleName(d.Role) + " | " + (d.CommandCost > 0 ? d.CommandCost.ToString() : "—")
                    + " | " + same + " | " + (capped ? "> " + F(k, "0") : F(k, "0.00")) + " | " + (capped ? "< " + F(BundleCost / k, "0.0") : F(value, "0.0")) + " | " + deviation + " |");
            }
            md.AppendLine();
            md.AppendLine("偏差 = 等价指挥点 ÷ 现在的指挥点 − 1，超过 ±25% 标出。墨蚀者现在没有指挥点（只当敌人用），招募墨蚀者后可以按“等价指挥点”定价。");
            md.AppendLine("注意这是加在“标准混编”上的边际价值：底子里已经有 2 名连弩手，所以再加对空的价值偏低、加墨魂和鹤骑的价值也偏低（对面同样有连弩手）。");
            md.AppendLine();
            return result;
        }

        /// <summary>Copies of each unit in the equal-budget duels: Budget / command cost (or the equivalent cost for units without one).</summary>
        private static Dictionary<string, int> DuelCounts(List<UnitDef> combat, Dictionary<string, double> value)
        {
            var counts = new Dictionary<string, int>();
            foreach (UnitDef d in combat)
            {
                double cost = d.CommandCost > 0 ? d.CommandCost : value[d.Id];
                int count = cost > 0 ? Math.Max(1, (int)Math.Round(Budget / cost)) : MaxCount;
                while (count > 1 && !Fits(d.Id, count))
                {
                    count--;
                }
                counts[d.Id] = count;
            }
            return counts;
        }

        private static double[,] Matrix(List<UnitDef> combat, Dictionary<string, int> counts, BattleTerrain terrain)
        {
            int n = combat.Count;
            var m = new double[n, n];
            for (int i = 0; i < n; i++)
            {
                m[i, i] = 0.5;
                for (int j = i + 1; j < n; j++)
                {
                    double s = Score(Squad((combat[i].Id, counts[combat[i].Id], 1)), Squad((combat[j].Id, counts[combat[j].Id], 1)), terrain).Win;
                    m[i, j] = s;
                    m[j, i] = 1 - s;
                }
            }
            return m;
        }

        private static double RowAverage(double[,] m, int i)
        {
            int n = m.GetLength(0);
            double total = 0;
            for (int j = 0; j < n; j++)
            {
                if (j != i)
                {
                    total += m[i, j];
                }
            }
            return total / (n - 1);
        }

        private static double[,] DuelMatrix(StringBuilder md, List<UnitDef> combat, Dictionary<string, int> counts)
        {
            md.AppendLine("## 8. 模拟：同等指挥点的单一角色对战（克制）");
            md.AppendLine();
            md.AppendLine("双方各花 " + Budget + " 指挥点只带一种角色（墨蚀者按第 7 节的等价指挥点），表里是行方的胜率。没有地形。");
            md.AppendLine();
            var header = new StringBuilder("| 行 \\ 列 |");
            var line = new StringBuilder("|---|");
            foreach (UnitDef d in combat)
            {
                header.Append(" " + Fixtures.Text(d.Name) + "×" + counts[d.Id] + " |");
                line.Append("---|");
            }
            header.Append(" 平均 |");
            line.Append("---|");
            md.AppendLine(header.ToString());
            md.AppendLine(line.ToString());
            double[,] m = Matrix(combat, counts, null);
            for (int i = 0; i < combat.Count; i++)
            {
                var row = new StringBuilder("| " + Fixtures.Text(combat[i].Name) + "×" + counts[combat[i].Id] + " |");
                for (int j = 0; j < combat.Count; j++)
                {
                    row.Append(" " + (i == j ? "—" : Pct(m[i, j])) + " |");
                }
                row.Append(" **" + Pct(RowAverage(m, i)) + "** |");
                md.AppendLine(row.ToString());
            }
            md.AppendLine();
            return m;
        }

        private static void CounterCheck(StringBuilder md, List<UnitDef> combat, double[,] m)
        {
            md.AppendLine("**克制关系核对**：克制表里 A 打 B 倍率 ≥ 1.5 且 B 打 A 倍率 < 1 的组合，A 应该赢（胜率 > 50%）。");
            md.AppendLine();
            md.AppendLine("| 克制方 | 被克制方 | 倍率（去 / 回） | 克制方胜率 | 结果 |");
            md.AppendLine("|---|---|---|---|---|");
            int ok = 0, total = 0;
            for (int i = 0; i < combat.Count; i++)
            {
                for (int j = 0; j < combat.Count; j++)
                {
                    Fix go = CounterTable.Get(combat[i].Role, combat[j].Role);
                    Fix back = CounterTable.Get(combat[j].Role, combat[i].Role);
                    if (i == j || go < Fix.FromRatio(3, 2) || back >= Fix.One)
                    {
                        continue;
                    }
                    total++;
                    bool holds = m[i, j] > 0.5;
                    if (holds)
                    {
                        ok++;
                    }
                    md.AppendLine("| " + Fixtures.Text(combat[i].Name) + " | " + Fixtures.Text(combat[j].Name) + " | " + F(go) + " / " + F(back) + " | " + Pct(m[i, j]) + " | " + (holds ? "✓" : "✗ 不成立") + " |");
                }
            }
            md.AppendLine();
            int decided = 0, pairs = 0;
            for (int i = 0; i < combat.Count; i++)
            {
                for (int j = i + 1; j < combat.Count; j++)
                {
                    pairs++;
                    if (m[i, j] <= 0.1 || m[i, j] >= 0.9)
                    {
                        decided++;
                    }
                }
            }
            md.AppendLine("成立 " + ok + " / " + total + "。全部 " + pairs + " 组对战里有 " + decided + " 组的胜率在 10% 以下或 90% 以上。");
            md.AppendLine();
        }

        private static void StarCheck(StringBuilder md, List<UnitDef> combat)
        {
            md.AppendLine("## 9. 模拟：升星的实际价值");
            md.AppendLine();
            md.AppendLine("高一星的角色（小型 4 名、大型 2 名）对低一星的同种角色，找到打平时低星需要几名，再除以高星的人数，得到“一名高一星顶几名低一星”。");
            md.AppendLine("参考线：远征里 3 名合成 1 名，这个数在 3 以下说明合成会亏战力（换来的是省格子和指挥点）；碎片升星没有这个约束，只看每一星的提升是否明显、是否均匀。");
            md.AppendLine();
            md.AppendLine("| 角色 | ★2 顶几名 ★1 | ★3 顶几名 ★2 | ★4 顶几名 ★3 | ★5 顶几名 ★4 | ★5 顶几名 ★1（连乘） |");
            md.AppendLine("|---|---|---|---|---|---|");
            var sums = new double[UnitDef.MaxStar + 1];
            var counts = new int[UnitDef.MaxStar + 1];
            foreach (UnitDef d in combat)
            {
                int high = d.Size >= 2 ? 2 : 4;
                var row = new StringBuilder("| " + Fixtures.Text(d.Name) + " |");
                double chain = 1;
                bool chainCapped = false;
                for (int s = 2; s <= UnitDef.MaxStar; s++)
                {
                    int star = s;
                    double n = BreakEven(k => Squad((d.Id, k, star - 1)), Squad((d.Id, high, star)), MaxCount, out bool capped);
                    double ratio = n / high;
                    chain *= ratio;
                    chainCapped |= capped;
                    if (!capped)
                    {
                        sums[s] += ratio;
                        counts[s]++;
                    }
                    row.Append(" " + (capped ? "> " : "") + F(ratio, "0.00") + " |");
                }
                row.Append(" " + (chainCapped ? "> " : "") + F(chain, "0.0") + " |");
                md.AppendLine(row.ToString());
            }
            var avg = new StringBuilder("| **平均** |");
            for (int s = 2; s <= UnitDef.MaxStar; s++)
            {
                avg.Append(" **" + (counts[s] > 0 ? F(sums[s] / counts[s], "0.00") : "—") + "** |");
            }
            avg.Append(" |");
            md.AppendLine(avg.ToString());
            md.AppendLine();
            md.AppendLine("“>” 表示棋盘放满低星角色也打不过，实际值更大。");
            md.AppendLine();
        }

        /// <summary>Undead mixed army of a given size: the units are added in this order.</summary>
        private static readonly string[] UndeadOrder = { "crawler", "ripper", "locust", "acid_turret", "ripper", "locust", "crawler", "ripper", "acid_turret", "locust", "brood_mother", "ripper", "crawler", "locust", "acid_turret", "ripper" };

        public static Formation UndeadArmy(int size)
        {
            var counts = new Dictionary<string, int>();
            var order = new List<string>();
            for (int i = 0; i < size; i++)
            {
                string id = UndeadOrder[i % UndeadOrder.Length];
                if (!counts.ContainsKey(id))
                {
                    counts[id] = 0;
                    order.Add(id);
                }
                counts[id]++;
            }
            var groups = new List<(string, int, int)>();
            foreach (string id in order)
            {
                groups.Add((id, counts[id], 1));
            }
            return Squad(groups.ToArray());
        }

        private static string DescribeUndead(int size)
        {
            var counts = new Dictionary<string, int>();
            var order = new List<string>();
            for (int i = 0; i < size; i++)
            {
                string id = UndeadOrder[i % UndeadOrder.Length];
                if (!counts.ContainsKey(id))
                {
                    counts[id] = 0;
                    order.Add(id);
                }
                counts[id]++;
            }
            var parts = new List<string>();
            foreach (string id in order)
            {
                parts.Add(Fixtures.Text(UnitCatalog.Get(id).Name) + counts[id]);
            }
            return string.Join(" ", parts);
        }

        private static void TerrainCheck(StringBuilder md)
        {
            md.AppendLine("## 10. 模拟：战场环境对各阵营的影响");
            md.AppendLine();
            md.AppendLine("我方四种 " + Budget + " 指挥点的阵容各对一支墨蚀者混编；墨蚀者的人数取没有地形时最接近 50% 的那一档。然后只加环境效果（不放地块），看我方优势分的变化（百分点）。"
                + "设计文档 5.5 的标准：环境把同一场战斗的胜率改变超过 " + F(TerrainFlag * 100, "0") + " 个百分点就削弱，超过的标 ⚠。这里每格 " + (4 * _seeds) + " 场。");
            md.AppendLine();
            var armies = new List<(string name, Formation f)>
            {
                ("标准混编", Reference()),
                ("玄武营为主：2 玄甲将 + 4 鹤骑", Squad(("battleship", 2, 1), ("strike_craft", 4, 1))),
                ("白虎游骑为主：3 游侠 + 6 连弩手", Squad(("frigate", 3, 1), ("pd_ship", 6, 1))),
                ("司天监为主：4 星术士 + 1 游侠 + 2 连弩手", Squad(("missile_ship", 4, 1), ("frigate", 1, 1), ("pd_ship", 2, 1))),
            };
            var terrains = new List<TerrainDef>(TerrainCatalog.Terrains);
            var header = new StringBuilder("| 我方阵容 | 墨蚀者阵容 | 无地形 |");
            var line = new StringBuilder("|---|---|---|");
            foreach (TerrainDef t in terrains)
            {
                header.Append(" " + Fixtures.Text(t.Name) + " |");
                line.Append("---|");
            }
            md.AppendLine(header.ToString());
            md.AppendLine(line.ToString());
            foreach ((string name, Formation army) in armies)
            {
                int bestSize = 1;
                double bestScore = 0, bestGap = 2;
                for (int size = 4; size <= 16; size++)
                {
                    double score = Score(army, UndeadArmy(size), null, 2 * _seeds).Edge;
                    if (Math.Abs(score - 0.5) < bestGap)
                    {
                        bestGap = Math.Abs(score - 0.5);
                        bestSize = size;
                        bestScore = score;
                    }
                    if (score < 0.4)
                    {
                        break;
                    }
                }
                Formation undead = UndeadArmy(bestSize);
                var row = new StringBuilder("| " + name + " | " + DescribeUndead(bestSize) + " | " + Pct(bestScore) + "（胜率 " + Pct(Score(army, undead, null, 2 * _seeds).Win) + "） |");
                foreach (TerrainDef t in terrains)
                {
                    Outcome o = Score(army, undead, new BattleTerrain { Def = t, Layout = new TerrainLayout() }, 2 * _seeds);
                    double d = o.Edge - bestScore;
                    string text = Pct(o.Edge) + "（" + (d >= 0 ? "+" : "") + F(d * 100, "0") + "；胜率 " + Pct(o.Win) + "）";
                    row.Append(" " + (Math.Abs(d) > TerrainFlag ? "**" + text + "** ⚠" : text) + " |");
                }
                md.AppendLine(row.ToString());
            }
            md.AppendLine();
        }

        /// <summary>Mirror match where one squad's half is covered with a tile; both sides take a turn on the tiles.</summary>
        private static Outcome TiledMirror(Formation squad, TileDef tile)
        {
            var score = new Outcome();
            for (int side = 0; side < 2; side++)
            {
                var layout = new TerrainLayout();
                for (int depth = 0; depth < BoardLayout.Depth; depth++)
                {
                    for (int r = 0; r < BoardLayout.Width; r++)
                    {
                        if (!TerrainGenerator.IsReserved(depth, r))
                        {
                            layout.Set(side, depth, r, tile);
                        }
                    }
                }
                var terrain = new BattleTerrain { Layout = layout };
                for (int s = 1; s <= _seeds; s++)
                {
                    score += Play(squad, squad, terrain, (ulong)s + (ulong)(1000 * side), side);
                }
            }
            return score / (2 * _seeds);
        }

        private static void TileCheck(StringBuilder md, List<UnitDef> combat, Dictionary<string, int> counts)
        {
            md.AppendLine("## 11. 模拟：地块的价值");
            md.AppendLine();
            md.AppendLine("同种角色镜像对战，一方的半场（观星台所在格除外）铺满某种地块，另一方是平地；表里是铺地块一方的优势分（双方轮流铺），50% 表示没有影响。敌人走进这一方半场时也会踩到地块。只测会改变战斗属性、能布阵的地块。");
            md.AppendLine();
            var tiles = new List<TileDef>();
            foreach (TileDef t in TerrainCatalog.Tiles)
            {
                if (t.Effects.Count > 0 && !t.BlocksDeploy)
                {
                    tiles.Add(t);
                }
            }
            var header = new StringBuilder("| 角色 |");
            var line = new StringBuilder("|---|");
            foreach (TileDef t in tiles)
            {
                header.Append(" " + Fixtures.Text(t.Name) + " |");
                line.Append("---|");
            }
            md.AppendLine(header.ToString());
            md.AppendLine(line.ToString());
            foreach (UnitDef d in combat)
            {
                var row = new StringBuilder("| " + Fixtures.Text(d.Name) + "×" + counts[d.Id] + " |");
                Formation squad = Squad((d.Id, counts[d.Id], 1));
                foreach (TileDef tile in tiles)
                {
                    Outcome o = TiledMirror(squad, tile);
                    row.Append(" " + Pct(o.Edge) + " |");
                }
                md.AppendLine(row.ToString());
            }
            md.AppendLine();
            md.AppendLine("飞骑不吃地块；墨泥只对墨蚀者生效；流沙对白虎游骑、沼泽对青丘灵族无效。");
            md.AppendLine();
        }
    }
}
