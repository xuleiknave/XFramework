using System.Threading.Tasks;
using Starline.Probe.Mongo;
using Unity.UOS.Func.Stateless.Core.Attributes;

namespace Starline.Probe.Uos
{
    /// <summary>
    /// S0 verification on UOS: each function returns a JSON report (see server/probe/README.md).
    /// Call Ping a few times first (cold start, then warm), then Battle, Steam and Database.
    /// </summary>
    [CloudService]
    public class ProbeService
    {
        [CloudFunc]
        public Task<string> Ping() => Task.FromResult(ProbeRunner.Ping().ToJson());

        [CloudFunc]
        public Task<string> Battle() => Task.FromResult(ProbeRunner.Battle().ToJson());

        [CloudFunc]
        public async Task<string> Steam() => (await ProbeRunner.Steam()).ToJson();

        /// <param name="databaseId">Leave empty when the app has a single database.</param>
        /// <param name="password">Leave empty when UOS is allowed to store the database password.</param>
        [CloudFunc]
        public async Task<string> Database(string databaseId, string password)
        {
            ProbeReport report = await ProbeRunner.Database(() => MongoProbeStore.OpenUos(databaseId, password));
            return report.ToJson();
        }
    }
}
