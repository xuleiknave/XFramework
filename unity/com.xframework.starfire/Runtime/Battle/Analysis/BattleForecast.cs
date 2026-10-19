using System;
using System.Collections.Generic;

namespace XFramework.Battle
{
    /// <summary>Outcome of a pre-battle forecast (GDD 16.4).</summary>
    public sealed class ForecastResult
    {
        public int Runs;
        public int Wins;
        public double WinRate => Runs == 0 ? 0 : (double)Wins / Runs;
        /// <summary>Half width of the 95% confidence interval of the win rate.</summary>
        public double Margin;
        public double AverageSeconds;
        public double AverageLosses;
        /// <summary>Most frequent problems in lost battles, already worded for the UI.</summary>
        public readonly List<string> LossReasons = new List<string>();
        public string Advice;
    }

    /// <summary>
    /// Runs the battle many times headlessly with different seeds. Commander skills are not
    /// used in the forecast, so the real battle can do a little better than predicted.
    /// </summary>
    public static class BattleForecast
    {
        public const int DefaultRuns = 100;
        /// <summary>Forecast seeds start here; the real battle should use a seed outside this range.</summary>
        public const ulong SeedBase = 1_000_000;

        public static ForecastResult Run(Encounter encounter, Formation player, int runs = DefaultRuns, ulong seedBase = SeedBase)
        {
            var result = new ForecastResult { Runs = runs };
            int losses = 0;
            long ticks = 0;
            var lostByUnit = new Dictionary<string, int>();
            var dryByUnit = new Dictionary<string, int>();
            int defeats = 0;
            int defeatsRelayLost = 0;
            int defeatsUnsupplied = 0;

            for (int i = 0; i < runs; i++)
            {
                var battle = new BattleSim(encounter.CreateSetup(player, seedBase + (ulong)i));
                battle.RunToEnd();
                ticks += battle.Tick;
                SideStats stats = battle.Stats[BoardLayout.PlayerSide];
                foreach (KeyValuePair<string, int> kv in stats.Losses)
                {
                    if (UnitCatalog.Get(kv.Key).HasWeapon)
                    {
                        losses += kv.Value;
                    }
                }

                if (battle.Winner == BoardLayout.PlayerSide)
                {
                    result.Wins++;
                    continue;
                }

                defeats++;
                foreach (string id in stats.Losses.Keys)
                {
                    Increment(lostByUnit, id);
                }
                foreach (string id in stats.RanDry)
                {
                    Increment(dryByUnit, id);
                }
                if (stats.RelaysLost > 0 || stats.SourceLost)
                {
                    defeatsRelayLost++;
                }
                if (stats.LongUnsuppliedUnits > 0)
                {
                    defeatsUnsupplied++;
                }
            }

            double p = result.WinRate;
            result.Margin = 1.96 * Math.Sqrt(p * (1 - p) / Math.Max(1, runs));
            result.AverageSeconds = (double)ticks / runs / BattleSim.TicksPerSecond;
            result.AverageLosses = (double)losses / runs;
            BuildReasons(result, defeats, lostByUnit, dryByUnit, defeatsRelayLost, defeatsUnsupplied);
            return result;
        }

        private static void Increment(Dictionary<string, int> counts, string key)
        {
            counts.TryGetValue(key, out int n);
            counts[key] = n + 1;
        }

        private struct Reason
        {
            public double Share;
            public string Text;
            public string Advice;
        }

        private static void BuildReasons(ForecastResult result, int defeats, Dictionary<string, int> lost,
            Dictionary<string, int> dry, int relayLost, int unsupplied)
        {
            if (defeats == 0)
            {
                result.Advice = "推演中没有出现败局，可以直接开战。";
                return;
            }

            var reasons = new List<Reason>();
            string worstLost = MostFrequent(lost, onlyCombatants: true);
            if (worstLost != null)
            {
                reasons.Add(new Reason
                {
                    Share = (double)lost[worstLost] / defeats,
                    Text = UnitCatalog.Get(worstLost).DisplayName + " 被击毁",
                    Advice = AdviceForLoss(UnitCatalog.Get(worstLost)),
                });
            }
            string worstDry = MostFrequent(dry, onlyCombatants: true);
            if (worstDry != null)
            {
                reasons.Add(new Reason
                {
                    Share = (double)dry[worstDry] / defeats,
                    Text = UnitCatalog.Get(worstDry).DisplayName + " 弹药耗尽停火",
                    Advice = "让 " + UnitCatalog.Get(worstDry).DisplayName + " 待在补给覆盖范围内，或者在它附近加一个中继站。",
                });
            }
            if (relayLost > 0)
            {
                reasons.Add(new Reason
                {
                    Share = (double)relayLost / defeats,
                    Text = "补给节点被摧毁",
                    Advice = "在中继站附近放一辆防空车，挡住来切补给线的飞蝗群。",
                });
            }
            if (unsupplied > 0)
            {
                reasons.Add(new Reason
                {
                    Share = (double)unsupplied / defeats,
                    Text = "兵器长时间断补给",
                    Advice = "检查红框的“未接入”兵器，把它们移进绿色覆盖区。",
                });
            }

            reasons.Sort((a, b) => b.Share.CompareTo(a.Share));
            for (int i = 0; i < reasons.Count && i < 2; i++)
            {
                result.LossReasons.Add(reasons[i].Text + "（" + Math.Round(reasons[i].Share * 100) + "% 的败局）");
            }
            result.Advice = reasons.Count > 0 ? reasons[0].Advice : null;
        }

        private static string MostFrequent(Dictionary<string, int> counts, bool onlyCombatants)
        {
            string best = null;
            int bestCount = 0;
            foreach (KeyValuePair<string, int> kv in counts)
            {
                if (onlyCombatants && !UnitCatalog.Get(kv.Key).HasWeapon)
                {
                    continue;
                }
                // Ordinal tie-break keeps the wording stable between runs.
                if (kv.Value > bestCount || (kv.Value == bestCount && best != null && string.CompareOrdinal(kv.Key, best) < 0))
                {
                    best = kv.Key;
                    bestCount = kv.Value;
                }
            }
            return best;
        }

        private static string AdviceForLoss(UnitDef def)
        {
            switch (def.Role)
            {
                case UnitRole.Artillery:
                    return "把 " + def.DisplayName + " 往后放，前面用重型或战车挡住突进的敌人。";
                case UnitRole.Air:
                    return def.DisplayName + " 怕防空，避开敌方防空单位所在的一翼。";
                case UnitRole.Heavy:
                    return "敌方炮台和空中单位克制重型，先用战机或战车解决它们。";
                case UnitRole.AntiAir:
                    return def.DisplayName + " 很脆，放在第二排，让前排替它挡伤害。";
                default:
                    return "前排太薄，考虑多放一个耐打的单位。";
            }
        }
    }
}
