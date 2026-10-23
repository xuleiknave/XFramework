using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Starline.Battle;

namespace Starline.Probe
{
    /// <summary>
    /// Can the cloud function load the config tables and run the kernel, and does it get exactly
    /// the same result as .NET on a developer machine? The server re-runs every battle, so any
    /// difference here would make honest players fail verification.
    /// </summary>
    public static class BattleProbe
    {
        public const int Seeds = 20;

        /// <summary>
        /// Combined hash of the fixed scenario over seeds 1..20, computed with .NET 8 on Linux x64.
        /// Changing the kernel or the data tables changes it; run the local runner with --print-golden to refresh.
        /// </summary>
        public const string GoldenHash = "c701cccfce654a73";

        private static readonly object LoadLock = new object();

        public static void Run(ProbeReport report)
        {
            var watch = Stopwatch.StartNew();
            bool loadedNow = EnsureGameData();
            report.Add("加载配置表", true, watch.ElapsedMilliseconds,
                loadedNow ? "本次调用加载（冷启动）" : "已在内存里（热调用）");

            watch.Restart();
            string hash = RunScenario(out int playerWins, out int totalTicks);
            long ms = watch.ElapsedMilliseconds;
            bool same = hash == GoldenHash;
            report.Add("内核重跑 " + Seeds + " 场", same, ms,
                "合并哈希 " + hash + (same ? "，与本地 .NET 一致" : "，与本地 .NET 的 " + GoldenHash + " 不一致！")
                + "；我方胜 " + playerWins + " 场，共 " + totalTicks + " 帧，平均每场 " + (ms * 1.0 / Seeds).ToString("0.0") + " ms");
        }

        /// <summary>Runs the fixed scenario for every seed and folds winner, length and final state into one hash.</summary>
        public static string RunScenario(out int playerWins, out int totalTicks)
        {
            EnsureGameData();
            ulong combined = 14695981039346656037UL;
            playerWins = 0;
            totalTicks = 0;
            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                var battle = new BattleSim(new BattleSetup { Player = Player(), Enemy = Enemy(), Seed = seed });
                battle.RunToEnd();
                if (battle.Winner == 0)
                {
                    playerWins++;
                }
                totalTicks += battle.Tick;
                combined = Mix(combined, (ulong)(battle.Winner + 2));
                combined = Mix(combined, (ulong)battle.Tick);
                combined = Mix(combined, battle.ComputeStateHash());
            }
            return combined.ToString("x16");
        }

        private static ulong Mix(ulong hash, ulong value)
        {
            for (int i = 0; i < 8; i++)
            {
                hash ^= (value >> (i * 8)) & 0xff;
                hash *= 1099511628211UL;
            }
            return hash;
        }

        private static bool EnsureGameData()
        {
            lock (LoadLock)
            {
                if (GameData.IsLoaded)
                {
                    return false;
                }
                Assembly assembly = typeof(BattleProbe).Assembly;
                GameData.Load(name =>
                {
                    using Stream stream = assembly.GetManifestResourceStream("StarlineData." + name + ".json");
                    if (stream == null)
                    {
                        return null;
                    }
                    using var reader = new StreamReader(stream);
                    return reader.ReadToEnd();
                });
                return true;
            }
        }

        // The prototype "ancient battlefield ambush" (same as the kernel tests' fixed scenario).
        private static Formation Enemy() => new Formation()
            .Add("hive", 4, 3)
            .Add("hive_node", 2, 4).Add("hive_node", 2, 1).Add("hive_node", 2, 7)
            .Add("ripper", 0, 1).Add("crawler", 0, 2).Add("crawler", 0, 5).Add("ripper", 0, 6)
            .Add("crawler", 1, 3).Add("crawler", 1, 4)
            .Add("locust", 1, 0).Add("locust", 1, 7)
            .Add("acid_turret", 2, 2).Add("acid_turret", 2, 5)
            .Add("brood_mother", 3, 0);

        private static Formation Player() => new Formation()
            .Add("supply_ship", 4, 3)
            .Add("relay", 2, 4).Add("relay", 2, 1).Add("relay", 2, 7)
            .Add("battleship", 0, 3)
            .Add("frigate", 0, 1).Add("frigate", 0, 6)
            .Add("strike_craft", 0, 0).Add("strike_craft", 5, 7)
            .Add("pd_ship", 2, 2).Add("pd_ship", 2, 5)
            .Add("missile_ship", 3, 1).Add("missile_ship", 3, 6).Add("missile_ship", 5, 0);
    }
}
