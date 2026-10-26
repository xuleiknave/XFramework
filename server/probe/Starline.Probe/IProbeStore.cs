using System.Collections.Generic;
using System.Threading.Tasks;

namespace Starline.Probe
{
    /// <summary>
    /// The handful of database operations the save design relies on (server-database.md section 11):
    /// whole-document writes, a write that only succeeds while the revision is unchanged, and a
    /// filtered, sorted query for the replay board.
    /// </summary>
    public interface IProbeStore
    {
        string Describe { get; }

        Task Clear();

        Task Insert(string id, long revision, string payload);

        /// <summary>Replaces the document only if its revision is still <paramref name="expected"/>; false when someone else wrote first.</summary>
        Task<bool> ReplaceIfRevision(string id, long expected, long next, string payload);

        /// <summary>The revision and payload length of a document, or null when it does not exist.</summary>
        Task<(long revision, int payloadLength)?> Get(string id);

        Task InsertBoardEntry(string id, string stage, int power, long createdTicks);

        /// <summary>Powers of the lowest-power entries of one stage, ascending, ties by creation time.</summary>
        Task<List<int>> LowestPowers(string stage, int limit);
    }
}
