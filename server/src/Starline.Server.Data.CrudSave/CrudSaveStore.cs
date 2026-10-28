using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using UOS.FuncStateless.MongoDB;

namespace Starline.Server.Data.CrudSave
{
    /// <summary>
    /// <see cref="IServerStore"/> on UOS CRUD Save, which is a MongoDB database. One document per
    /// account, identity link, save slot and idempotency key; conditional writes filter on "revision".
    /// </summary>
    public sealed class CrudSaveStore : IServerStore
    {
        public const string DatabaseName = "starline";

        private readonly IMongoCollection<BsonDocument> _accounts;
        private readonly IMongoCollection<BsonDocument> _identities;
        private readonly IMongoCollection<BsonDocument> _saves;
        private readonly IMongoCollection<BsonDocument> _deletedSaves;
        private readonly IMongoCollection<BsonDocument> _idempotency;
        private readonly IMongoCollection<BsonDocument> _links;

        public CrudSaveStore(IMongoClient client, string databaseName = DatabaseName)
        {
            IMongoDatabase db = client.GetDatabase(databaseName);
            _accounts = db.GetCollection<BsonDocument>("accounts");
            _identities = db.GetCollection<BsonDocument>("identities");
            _saves = db.GetCollection<BsonDocument>("saves");
            _deletedSaves = db.GetCollection<BsonDocument>("saves_deleted");
            _idempotency = db.GetCollection<BsonDocument>("idempotency");
            _links = db.GetCollection<BsonDocument>("link_codes");
        }

        /// <summary>Connects through the UOS CRUD SDK; with no id it picks the app's only database.</summary>
        public static async Task<CrudSaveStore> OpenUos(string databaseId = "", string password = "")
        {
            IMongoClient client = await MongoConnectionManager.GetConnection(databaseId ?? "", password ?? "");
            return new CrudSaveStore(client);
        }

        public static CrudSaveStore OpenLocal(string connectionString, string databaseName = DatabaseName)
        {
            return new CrudSaveStore(new MongoClient(connectionString), databaseName);
        }

        /// <summary>Indexes the queries need; safe to call more than once.</summary>
        public async Task EnsureIndexes()
        {
            await _saves.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("account").Ascending("slot")));
            await _identities.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("account")));
        }

        private static FilterDefinition<BsonDocument> ById(string id) => Builders<BsonDocument>.Filter.Eq("_id", id);

        private static FilterDefinition<BsonDocument> ByIdAndRevision(string id, long revision) =>
            ById(id) & Builders<BsonDocument>.Filter.Eq("revision", revision);

        private static async Task<bool> TryInsert(IMongoCollection<BsonDocument> collection, BsonDocument doc)
        {
            try
            {
                await collection.InsertOneAsync(doc);
                return true;
            }
            catch (MongoWriteException e) when (e.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                return false;
            }
        }

        // ------------------------------------------------------------------
        // Accounts

        private static BsonDocument ToDoc(AccountRecord a) => new BsonDocument
        {
            { "_id", a.Id },
            { "revision", a.Revision },
            { "identity", a.Identity ?? "" },
            { "displayName", a.DisplayName ?? "" },
            { "createdAt", a.CreatedAt },
            { "lastLoginAt", a.LastLoginAt },
            { "sessionSecret", a.SessionSecret ?? "" },
            { "sessionExpiresAt", a.SessionExpiresAt },
            { "linkFailures", a.LinkFailures },
            { "linkFailuresSince", a.LinkFailuresSince },
            { "mergedInto", a.MergedInto ?? "" },
        };

        private static AccountRecord ToAccount(BsonDocument d) => new AccountRecord
        {
            Id = d["_id"].AsString,
            Revision = d["revision"].ToInt64(),
            Identity = d.GetValue("identity", "").AsString,
            DisplayName = d.GetValue("displayName", "").AsString,
            CreatedAt = d.GetValue("createdAt", 0L).ToInt64(),
            LastLoginAt = d.GetValue("lastLoginAt", 0L).ToInt64(),
            SessionSecret = d.GetValue("sessionSecret", "").AsString,
            SessionExpiresAt = d.GetValue("sessionExpiresAt", 0L).ToInt64(),
            LinkFailures = d.GetValue("linkFailures", 0).ToInt32(),
            LinkFailuresSince = d.GetValue("linkFailuresSince", 0L).ToInt64(),
            MergedInto = NullIfEmpty(d.GetValue("mergedInto", "").AsString),
        };

        private static string NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;

        public async Task<AccountRecord> GetAccount(string id)
        {
            BsonDocument doc = await _accounts.Find(ById(id)).FirstOrDefaultAsync();
            return doc == null ? null : ToAccount(doc);
        }

        public Task<bool> InsertAccount(AccountRecord account) => TryInsert(_accounts, ToDoc(account));

        public async Task<bool> ReplaceAccount(AccountRecord account, long expectedRevision)
        {
            ReplaceOneResult result = await _accounts.ReplaceOneAsync(ByIdAndRevision(account.Id, expectedRevision), ToDoc(account));
            return result.MatchedCount == 1;
        }

        public async Task<string> FindIdentity(string identity)
        {
            BsonDocument doc = await _identities.Find(ById(identity)).FirstOrDefaultAsync();
            return doc?["account"].AsString;
        }

        public Task<bool> InsertIdentity(string identity, string accountId) =>
            TryInsert(_identities, new BsonDocument { { "_id", identity }, { "account", accountId } });

        public async Task<List<string>> ListIdentities(string accountId)
        {
            List<BsonDocument> docs = await _identities.Find(Builders<BsonDocument>.Filter.Eq("account", accountId)).ToListAsync();
            return docs.Select(d => d["_id"].AsString).OrderBy(id => id, System.StringComparer.Ordinal).ToList();
        }

        public async Task<bool> MoveIdentity(string identity, string fromAccount, string toAccount)
        {
            UpdateResult result = await _identities.UpdateOneAsync(
                ById(identity) & Builders<BsonDocument>.Filter.Eq("account", fromAccount),
                Builders<BsonDocument>.Update.Set("account", toAccount));
            return result.MatchedCount == 1;
        }

        // ------------------------------------------------------------------
        // Link codes

        public Task<bool> InsertLinkCode(LinkCodeRecord link) =>
            TryInsert(_links, new BsonDocument { { "_id", link.Code }, { "account", link.AccountId }, { "expiresAt", link.ExpiresAt } });

        public async Task<LinkCodeRecord> GetLinkCode(string code)
        {
            BsonDocument doc = await _links.Find(ById(code)).FirstOrDefaultAsync();
            return doc == null ? null : new LinkCodeRecord
            {
                Code = doc["_id"].AsString,
                AccountId = doc["account"].AsString,
                ExpiresAt = doc["expiresAt"].ToInt64(),
            };
        }

        public async Task<bool> DeleteLinkCode(string code)
        {
            DeleteResult result = await _links.DeleteOneAsync(ById(code));
            return result.DeletedCount == 1;
        }

        // ------------------------------------------------------------------
        // Saves

        private static BsonDocument ToDoc(SaveRecord s) => new BsonDocument
        {
            { "_id", s.Id },
            { "account", s.AccountId },
            { "slot", s.Slot },
            { "revision", s.Revision },
            { "name", s.Name ?? "" },
            { "createdAt", s.CreatedAt },
            { "lastPlayedAt", s.LastPlayedAt },
            { "playMs", s.PlayMs },
            { "runsStarted", s.RunsStarted },
            { "runsWon", s.RunsWon },
            { "lastReportAt", s.LastReportAt },
            { "createKey", s.CreateKey ?? "" },
        };

        private static SaveRecord ToSave(BsonDocument d) => new SaveRecord
        {
            AccountId = d["account"].AsString,
            Slot = d["slot"].ToInt32(),
            Revision = d["revision"].ToInt64(),
            Name = d.GetValue("name", "").AsString,
            CreatedAt = d.GetValue("createdAt", 0L).ToInt64(),
            LastPlayedAt = d.GetValue("lastPlayedAt", 0L).ToInt64(),
            PlayMs = d.GetValue("playMs", 0L).ToInt64(),
            RunsStarted = d.GetValue("runsStarted", 0).ToInt32(),
            RunsWon = d.GetValue("runsWon", 0).ToInt32(),
            LastReportAt = d.GetValue("lastReportAt", 0L).ToInt64(),
            CreateKey = d.GetValue("createKey", "").AsString,
        };

        public async Task<SaveRecord> GetSave(string accountId, int slot)
        {
            BsonDocument doc = await _saves.Find(ById(SaveRecord.IdOf(accountId, slot))).FirstOrDefaultAsync();
            return doc == null ? null : ToSave(doc);
        }

        public async Task<List<SaveRecord>> ListSaves(string accountId)
        {
            List<BsonDocument> docs = await _saves.Find(Builders<BsonDocument>.Filter.Eq("account", accountId))
                .Sort(Builders<BsonDocument>.Sort.Ascending("slot"))
                .ToListAsync();
            return docs.Select(ToSave).ToList();
        }

        public Task<bool> InsertSave(SaveRecord save) => TryInsert(_saves, ToDoc(save));

        public async Task<bool> ReplaceSave(SaveRecord save, long expectedRevision)
        {
            ReplaceOneResult result = await _saves.ReplaceOneAsync(ByIdAndRevision(save.Id, expectedRevision), ToDoc(save));
            return result.MatchedCount == 1;
        }

        public async Task<bool> ArchiveSave(SaveRecord save, long expectedRevision, long deletedAt)
        {
            BsonDocument removed = await _saves.FindOneAndDeleteAsync(ByIdAndRevision(save.Id, expectedRevision));
            if (removed == null)
            {
                return false;
            }
            removed["_id"] = save.Id + ":" + deletedAt;
            removed["deletedAt"] = deletedAt;
            await _deletedSaves.InsertOneAsync(removed);
            return true;
        }

        // ------------------------------------------------------------------
        // Idempotency

        public async Task<string> GetIdempotent(string accountId, string key)
        {
            BsonDocument doc = await _idempotency.Find(ById(accountId + "/" + key)).FirstOrDefaultAsync();
            return doc?["response"].AsString;
        }

        public async Task PutIdempotent(string accountId, string key, string response, long createdAt)
        {
            var doc = new BsonDocument { { "_id", accountId + "/" + key }, { "response", response }, { "createdAt", createdAt } };
            await _idempotency.ReplaceOneAsync(ById(accountId + "/" + key), doc, new ReplaceOptions { IsUpsert = true });
        }
    }
}
