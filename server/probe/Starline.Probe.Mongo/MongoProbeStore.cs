using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using UOS.FuncStateless.MongoDB;

namespace Starline.Probe.Mongo
{
    /// <summary>IProbeStore on MongoDB, the document database behind UOS CRUD.</summary>
    public sealed class MongoProbeStore : IProbeStore
    {
        private const string DatabaseName = "starline_probe";

        private readonly IMongoCollection<BsonDocument> _saves;
        private readonly IMongoCollection<BsonDocument> _board;

        public MongoProbeStore(IMongoClient client, string describe)
        {
            IMongoDatabase db = client.GetDatabase(DatabaseName);
            _saves = db.GetCollection<BsonDocument>("probe_saves");
            _board = db.GetCollection<BsonDocument>("probe_board");
            Describe = describe;
        }

        public string Describe { get; }

        /// <summary>Connects through the UOS CRUD SDK; with no id it picks the app's only database.</summary>
        public static async Task<IProbeStore> OpenUos(string databaseId, string password)
        {
            var client = await MongoConnectionManager.GetConnection(databaseId ?? "", password ?? "");
            return new MongoProbeStore(client, "UOS CRUD（MongoDB）" + (string.IsNullOrEmpty(databaseId) ? "，自动选择的数据库" : "，" + databaseId));
        }

        public static IProbeStore OpenLocal(string connectionString)
        {
            return new MongoProbeStore(new MongoClient(connectionString), "MongoDB（" + connectionString + "）");
        }

        public async Task Clear()
        {
            await _saves.DeleteManyAsync(Builders<BsonDocument>.Filter.Regex("_id", new BsonRegularExpression("^probe-")));
            await _board.DeleteManyAsync(Builders<BsonDocument>.Filter.Regex("_id", new BsonRegularExpression("^probe-")));
        }

        public Task Insert(string id, long revision, string payload)
        {
            return _saves.InsertOneAsync(new BsonDocument { { "_id", id }, { "revision", revision }, { "payload", payload } });
        }

        public async Task<bool> ReplaceIfRevision(string id, long expected, long next, string payload)
        {
            var filter = Builders<BsonDocument>.Filter.Eq("_id", id) & Builders<BsonDocument>.Filter.Eq("revision", expected);
            var doc = new BsonDocument { { "_id", id }, { "revision", next }, { "payload", payload } };
            ReplaceOneResult result = await _saves.ReplaceOneAsync(filter, doc);
            return result.ModifiedCount == 1;
        }

        public async Task<(long revision, int payloadLength)?> Get(string id)
        {
            BsonDocument doc = await _saves.Find(Builders<BsonDocument>.Filter.Eq("_id", id)).FirstOrDefaultAsync();
            if (doc == null)
            {
                return null;
            }
            return (doc["revision"].ToInt64(), doc["payload"].AsString.Length);
        }

        public Task InsertBoardEntry(string id, string stage, int power, long createdTicks)
        {
            return _board.InsertOneAsync(new BsonDocument { { "_id", id }, { "stage", stage }, { "power", power }, { "created", createdTicks } });
        }

        public async Task<List<int>> LowestPowers(string stage, int limit)
        {
            List<BsonDocument> docs = await _board.Find(Builders<BsonDocument>.Filter.Eq("stage", stage))
                .Sort(Builders<BsonDocument>.Sort.Ascending("power").Ascending("created"))
                .Limit(limit)
                .ToListAsync();
            return docs.Select(d => d["power"].AsInt32).ToList();
        }
    }
}
