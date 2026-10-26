using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Starline.Probe
{
    /// <summary>Entry points shared by the cloud function and the local runner.</summary>
    public static class ProbeRunner
    {
        /// <summary>Only process info: call it right after deploying to see cold-start and warm latency.</summary>
        public static ProbeReport Ping()
        {
            var report = new ProbeReport();
            ProcessInfo.Stamp(report, "ping");
            report.Add("云函数在运行", true, 0, DateTime.UtcNow.ToString("o"));
            return report;
        }

        public static ProbeReport Battle()
        {
            var report = new ProbeReport();
            ProcessInfo.Stamp(report, "battle");
            Guard(report, "内核", () => BattleProbe.Run(report));
            return report;
        }

        public static async Task<ProbeReport> Steam()
        {
            var report = new ProbeReport();
            ProcessInfo.Stamp(report, "steam");
            await SteamProbe.Run(report);
            return report;
        }

        public static async Task<ProbeReport> Database(Func<Task<IProbeStore>> openStore)
        {
            var report = new ProbeReport();
            ProcessInfo.Stamp(report, "database");
            var watch = Stopwatch.StartNew();
            IProbeStore store;
            try
            {
                store = await openStore();
                report.Add("连接数据库", true, watch.ElapsedMilliseconds, "");
            }
            catch (Exception e)
            {
                report.Add("连接数据库", false, watch.ElapsedMilliseconds, e.GetType().Name + "：" + e.Message);
                return report;
            }
            try
            {
                await DatabaseProbe.Run(report, store);
            }
            catch (Exception e)
            {
                report.Add("数据库检查中断", false, 0, e.GetType().Name + "：" + e.Message);
            }
            return report;
        }

        private static void Guard(ProbeReport report, string name, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                report.Add(name + " 出错", false, 0, e.GetType().Name + "：" + e.Message);
            }
        }
    }
}
