using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Starline.Probe
{
    /// <summary>
    /// Checks what the save design needs from the database: conditional writes (optimistic
    /// locking on revision), documents big enough for a whole save, read latency, and a sorted
    /// query for the replay board. Leaves nothing behind.
    /// </summary>
    public static class DatabaseProbe
    {
        private const string SaveId = "probe-save";

        /// <summary>Payload sizes to try. A full save is estimated at 30~80 KB.</summary>
        private static readonly int[] Sizes = { 64 * 1024, 256 * 1024, 1024 * 1024, 4 * 1024 * 1024, 15 * 1024 * 1024 };

        public static async Task Run(ProbeReport report, IProbeStore store)
        {
            report.Add("数据库", true, 0, store.Describe);
            try
            {
                await Step(report, "清空验证数据", () => store.Clear());
                await RunChecks(report, store);
            }
            finally
            {
                await Step(report, "清理验证数据", () => store.Clear());
            }
        }

        private static async Task RunChecks(ProbeReport report, IProbeStore store)
        {
            string save = Payload(80 * 1024);
            var watch = Stopwatch.StartNew();
            await store.Insert(SaveId, 1, save);
            var read = await store.Get(SaveId);
            report.Add("写入并读回 80 KB 存档", read.HasValue && read.Value.revision == 1 && read.Value.payloadLength == save.Length,
                watch.ElapsedMilliseconds, read.HasValue ? "revision " + read.Value.revision + "，长度 " + read.Value.payloadLength : "读不到");

            watch.Restart();
            long totalRead = 0;
            const int reads = 5;
            for (int i = 0; i < reads; i++)
            {
                var r = Stopwatch.StartNew();
                await store.Get(SaveId);
                totalRead += r.ElapsedMilliseconds;
            }
            report.Add("读 80 KB 存档的耗时", true, totalRead / reads, "连续读 " + reads + " 次的平均值");

            watch.Restart();
            bool replaced = await store.ReplaceIfRevision(SaveId, 1, 2, save);
            report.Add("条件写：版本对得上", replaced, watch.ElapsedMilliseconds, replaced ? "写入成功，revision 1 → 2" : "应该成功却失败了");

            watch.Restart();
            bool stale = await store.ReplaceIfRevision(SaveId, 1, 3, save);
            var afterStale = await store.Get(SaveId);
            bool staleOk = !stale && afterStale.HasValue && afterStale.Value.revision == 2;
            report.Add("条件写：版本已过期", staleOk, watch.ElapsedMilliseconds,
                staleOk ? "被正确拒绝，revision 仍是 2" : "旧版本也写进去了，不能用来做乐观锁");

            watch.Restart();
            const int writers = 8;
            bool[] results = await Task.WhenAll(Enumerable.Range(0, writers)
                .Select(i => store.ReplaceIfRevision(SaveId, 2, 3, save)));
            int winners = results.Count(r => r);
            report.Add("条件写：" + writers + " 个请求同时写", winners == 1, watch.ElapsedMilliseconds,
                winners + " 个成功（应该正好 1 个）");

            foreach (int size in Sizes)
            {
                string id = "probe-size-" + size;
                watch.Restart();
                try
                {
                    string payload = Payload(size);
                    await store.Insert(id, 1, payload);
                    var big = await store.Get(id);
                    bool ok = big.HasValue && big.Value.payloadLength == size;
                    report.Add("文档大小 " + Kb(size), ok, watch.ElapsedMilliseconds, ok ? "写入并读回成功" : "读回的长度不对");
                }
                catch (Exception e)
                {
                    // Sizes above what a save needs are expected to fail on some stores; only record the limit.
                    report.Add("文档大小 " + Kb(size), size > 256 * 1024, watch.ElapsedMilliseconds,
                        "写入失败（" + e.GetType().Name + "：" + Trim(e.Message) + "）" + (size > 256 * 1024 ? "，超出存档需要的大小，不影响" : ""));
                }
            }

            watch.Restart();
            var expected = new List<(int power, long created)>();
            var random = new Random(7);
            for (int i = 0; i < 30; i++)
            {
                int power = 1000 + random.Next(0, 50) * 10;
                long created = 1000 + i;
                expected.Add((power, created));
                await store.InsertBoardEntry("probe-board-a-" + i, "probe-a", power, created);
            }
            for (int i = 0; i < 10; i++)
            {
                await store.InsertBoardEntry("probe-board-b-" + i, "probe-b", 1, 1000 + i);
            }
            List<int> top = await store.LowestPowers("probe-a", 20);
            List<int> want = expected.OrderBy(e => e.power).ThenBy(e => e.created).Take(20).Select(e => e.power).ToList();
            bool sorted = top.SequenceEqual(want);
            report.Add("按关卡筛选、按战力排序取前 20", sorted, watch.ElapsedMilliseconds,
                sorted ? "结果正确（录像榜可以直接查询）" : "结果不对：" + string.Join(",", top) + "（需要由云函数自己维护排好序的榜单文档）");
        }

        private static async Task Step(ProbeReport report, string name, Func<Task> action)
        {
            var watch = Stopwatch.StartNew();
            try
            {
                await action();
            }
            catch (Exception e)
            {
                report.Add(name, false, watch.ElapsedMilliseconds, e.GetType().Name + "：" + Trim(e.Message));
                throw;
            }
        }

        private static string Payload(int length)
        {
            // Mixed content so a compressing store cannot shrink it to nothing.
            var chars = new char[length];
            uint x = 2463534242;
            for (int i = 0; i < length; i++)
            {
                x ^= x << 13;
                x ^= x >> 17;
                x ^= x << 5;
                chars[i] = (char)('a' + x % 26);
            }
            return new string(chars);
        }

        private static string Kb(int size) => size >= 1024 * 1024 ? size / (1024 * 1024) + " MB" : size / 1024 + " KB";

        private static string Trim(string s) => s.Length > 160 ? s.Substring(0, 160) + "…" : s;
    }
}
