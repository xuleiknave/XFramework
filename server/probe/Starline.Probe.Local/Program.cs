using System;
using System.Threading.Tasks;
using Starline.Probe;
using Starline.Probe.Mongo;

namespace Starline.Probe.Local
{
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            if (args.Length > 0 && args[0] == "--print-golden")
            {
                Console.WriteLine(BattleProbe.RunScenario(out int wins, out int ticks) + "  （我方胜 " + wins + " 场，共 " + ticks + " 帧）");
                return 0;
            }

            string mongo = args.Length > 1 && args[0] == "--mongo" ? args[1] : null;
            bool skipSteam = Array.IndexOf(args, "--no-steam") >= 0;

            ProbeReport[] reports =
            {
                ProbeRunner.Ping(),
                ProbeRunner.Battle(),
                ProbeRunner.Ping(),
                skipSteam ? null : await ProbeRunner.Steam(),
                await ProbeRunner.Database(() => Task.FromResult(mongo == null ? new MemoryProbeStore() : MongoProbeStore.OpenLocal(mongo))),
            };

            bool allOk = true;
            foreach (ProbeReport report in reports)
            {
                if (report == null)
                {
                    continue;
                }
                Console.WriteLine(report.ToJson());
                allOk &= report.AllOk;
            }
            Console.WriteLine(allOk ? "全部通过" : "有检查没通过");
            return allOk ? 0 : 1;
        }
    }
}
