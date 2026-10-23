using System;
using System.Collections.Generic;

namespace Starline.Battle
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
        /// <summary>Most frequent problems in lost battles, as text for the UI.</summary>
        public readonly List<LocText> LossReasons = new List<LocText>();
        public LocText Advice;
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
            public LocText Text;
            public LocText Advice;
        }

        private static void BuildReasons(ForecastResult result, int defeats, Dictionary<string, int> lost,
            Dictionary<string, int> dry, int relayLost, int unsupplied)
        {
            if (defeats == 0)
            {
                result.Advice = new LocText("forecast.no_defeat");
                return;
            }

            var reasons = new List<Reason>();
            string worstLost = MostFrequent(lost, onlyCombatants: true);
            if (worstLost != null)
            {
                reasons.Add(new Reason
                {
                    Share = (double)lost[worstLost] / defeats,
                    Text = new LocText("forecast.unit_lost", LocText.UnitName(worstLost)),
                    Advice = AdviceForLoss(UnitCatalog.Get(worstLost)),
                });
            }
            string worstDry = MostFrequent(dry, onlyCombatants: true);
            if (worstDry != null)
            {
                reasons.Add(new Reason
                {
                    Share = (double)dry[worstDry] / defeats,
                    Text = new LocText("forecast.unit_dry", LocText.UnitName(worstDry)),
                    Advice = new LocText("forecast.advice_dry", LocText.UnitName(worstDry)),
                });
            }
            if (relayLost > 0)
            {
                reasons.Add(new Reason
                {
                    Share = (double)relayLost / defeats,
                    Text = new LocText("forecast.relay_lost"),
                    Advice = new LocText("forecast.advice_relay_lost"),
                });
            }
            if (unsupplied > 0)
            {
                reasons.Add(new Reason
                {
                    Share = (double)unsupplied / defeats,
                    Text = new LocText("forecast.unsupplied"),
                    Advice = new LocText("forecast.advice_unsupplied"),
                });
            }

            reasons.Sort((a, b) => b.Share.CompareTo(a.Share));
            for (int i = 0; i < reasons.Count && i < 2; i++)
            {
                result.LossReasons.Add(new LocText("forecast.reason_share", reasons[i].Text, (int)Math.Round(reasons[i].Share * 100)));
            }
            result.Advice = reasons.Count > 0 ? reasons[0].Advice : default(LocText);
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

        private static LocText AdviceForLoss(UnitDef def)
        {
            switch (def.Role)
            {
                case UnitRole.Artillery:
                    return new LocText("forecast.advice_artillery", def.Name);
                case UnitRole.Craft:
                    return new LocText("forecast.advice_craft", def.Name);
                case UnitRole.Heavy:
                    return new LocText("forecast.advice_heavy");
                case UnitRole.PointDefense:
                    return new LocText("forecast.advice_point_defense", def.Name);
                default:
                    return new LocText("forecast.advice_default");
            }
        }
    }
}
