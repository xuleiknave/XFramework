using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Starline.Probe
{
    /// <summary>In-process store for local runs and tests; behaves like the real database should.</summary>
    public sealed class MemoryProbeStore : IProbeStore
    {
        private readonly object _lock = new object();
        private readonly Dictionary<string, (long revision, string payload)> _docs = new Dictionary<string, (long, string)>();
        private readonly List<(string id, string stage, int power, long created)> _board = new List<(string, string, int, long)>();

        public string Describe => "内存（本地测试用）";

        public Task Clear()
        {
            lock (_lock)
            {
                _docs.Clear();
                _board.Clear();
            }
            return Task.CompletedTask;
        }

        public Task Insert(string id, long revision, string payload)
        {
            lock (_lock)
            {
                _docs.Add(id, (revision, payload));
            }
            return Task.CompletedTask;
        }

        public Task<bool> ReplaceIfRevision(string id, long expected, long next, string payload)
        {
            lock (_lock)
            {
                if (!_docs.TryGetValue(id, out var doc) || doc.revision != expected)
                {
                    return Task.FromResult(false);
                }
                _docs[id] = (next, payload);
                return Task.FromResult(true);
            }
        }

        public Task<(long revision, int payloadLength)?> Get(string id)
        {
            lock (_lock)
            {
                (long revision, int payloadLength)? result = null;
                if (_docs.TryGetValue(id, out var doc))
                {
                    result = (doc.revision, doc.payload.Length);
                }
                return Task.FromResult(result);
            }
        }

        public Task InsertBoardEntry(string id, string stage, int power, long createdTicks)
        {
            lock (_lock)
            {
                _board.Add((id, stage, power, createdTicks));
            }
            return Task.CompletedTask;
        }

        public Task<List<int>> LowestPowers(string stage, int limit)
        {
            lock (_lock)
            {
                return Task.FromResult(_board.Where(e => e.stage == stage)
                    .OrderBy(e => e.power).ThenBy(e => e.created)
                    .Take(limit).Select(e => e.power).ToList());
            }
        }
    }
}
