using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Starline.Server.Data
{
    /// <summary>In-memory <see cref="IServerStore"/> for tests and local runs. Thread-safe; records are copied in and out.</summary>
    public sealed class MemoryServerStore : IServerStore
    {
        private readonly object _lock = new object();
        private readonly Dictionary<string, AccountRecord> _accounts = new Dictionary<string, AccountRecord>();
        private readonly Dictionary<string, string> _identities = new Dictionary<string, string>();
        private readonly Dictionary<string, SaveRecord> _saves = new Dictionary<string, SaveRecord>();
        private readonly Dictionary<string, string> _idempotent = new Dictionary<string, string>();
        private readonly Dictionary<string, LinkCodeRecord> _links = new Dictionary<string, LinkCodeRecord>();

        /// <summary>Deleted saves, for tests.</summary>
        public List<SaveRecord> Archived { get; } = new List<SaveRecord>();

        public Task<AccountRecord> GetAccount(string id)
        {
            lock (_lock)
            {
                return Task.FromResult(_accounts.TryGetValue(id, out AccountRecord a) ? a.Clone() : null);
            }
        }

        public Task<bool> InsertAccount(AccountRecord account)
        {
            lock (_lock)
            {
                if (_accounts.ContainsKey(account.Id))
                {
                    return Task.FromResult(false);
                }
                _accounts[account.Id] = account.Clone();
                return Task.FromResult(true);
            }
        }

        public Task<bool> ReplaceAccount(AccountRecord account, long expectedRevision)
        {
            lock (_lock)
            {
                if (!_accounts.TryGetValue(account.Id, out AccountRecord stored) || stored.Revision != expectedRevision)
                {
                    return Task.FromResult(false);
                }
                _accounts[account.Id] = account.Clone();
                return Task.FromResult(true);
            }
        }

        public Task<string> FindIdentity(string identity)
        {
            lock (_lock)
            {
                return Task.FromResult(_identities.TryGetValue(identity, out string id) ? id : null);
            }
        }

        public Task<bool> InsertIdentity(string identity, string accountId)
        {
            lock (_lock)
            {
                if (_identities.ContainsKey(identity))
                {
                    return Task.FromResult(false);
                }
                _identities[identity] = accountId;
                return Task.FromResult(true);
            }
        }

        public Task<List<string>> ListIdentities(string accountId)
        {
            lock (_lock)
            {
                return Task.FromResult(_identities.Where(p => p.Value == accountId).Select(p => p.Key).OrderBy(k => k, System.StringComparer.Ordinal).ToList());
            }
        }

        public Task<bool> MoveIdentity(string identity, string fromAccount, string toAccount)
        {
            lock (_lock)
            {
                if (!_identities.TryGetValue(identity, out string current) || current != fromAccount)
                {
                    return Task.FromResult(false);
                }
                _identities[identity] = toAccount;
                return Task.FromResult(true);
            }
        }

        public Task<bool> InsertLinkCode(LinkCodeRecord link)
        {
            lock (_lock)
            {
                if (_links.ContainsKey(link.Code))
                {
                    return Task.FromResult(false);
                }
                _links[link.Code] = link.Clone();
                return Task.FromResult(true);
            }
        }

        public Task<LinkCodeRecord> GetLinkCode(string code)
        {
            lock (_lock)
            {
                return Task.FromResult(_links.TryGetValue(code, out LinkCodeRecord l) ? l.Clone() : null);
            }
        }

        public Task<bool> DeleteLinkCode(string code)
        {
            lock (_lock)
            {
                return Task.FromResult(_links.Remove(code));
            }
        }

        public Task<SaveRecord> GetSave(string accountId, int slot)
        {
            lock (_lock)
            {
                return Task.FromResult(_saves.TryGetValue(SaveRecord.IdOf(accountId, slot), out SaveRecord s) ? s.Clone() : null);
            }
        }

        public Task<List<SaveRecord>> ListSaves(string accountId)
        {
            lock (_lock)
            {
                return Task.FromResult(_saves.Values.Where(s => s.AccountId == accountId).OrderBy(s => s.Slot).Select(s => s.Clone()).ToList());
            }
        }

        public Task<bool> InsertSave(SaveRecord save)
        {
            lock (_lock)
            {
                if (_saves.ContainsKey(save.Id))
                {
                    return Task.FromResult(false);
                }
                _saves[save.Id] = save.Clone();
                return Task.FromResult(true);
            }
        }

        public Task<bool> ReplaceSave(SaveRecord save, long expectedRevision)
        {
            lock (_lock)
            {
                if (!_saves.TryGetValue(save.Id, out SaveRecord stored) || stored.Revision != expectedRevision)
                {
                    return Task.FromResult(false);
                }
                _saves[save.Id] = save.Clone();
                return Task.FromResult(true);
            }
        }

        public Task<bool> ArchiveSave(SaveRecord save, long expectedRevision, long deletedAt)
        {
            lock (_lock)
            {
                if (!_saves.TryGetValue(save.Id, out SaveRecord stored) || stored.Revision != expectedRevision)
                {
                    return Task.FromResult(false);
                }
                _saves.Remove(save.Id);
                Archived.Add(stored);
                return Task.FromResult(true);
            }
        }

        public Task<string> GetIdempotent(string accountId, string key)
        {
            lock (_lock)
            {
                return Task.FromResult(_idempotent.TryGetValue(accountId + "/" + key, out string response) ? response : null);
            }
        }

        public Task PutIdempotent(string accountId, string key, string response, long createdAt)
        {
            lock (_lock)
            {
                _idempotent[accountId + "/" + key] = response;
                return Task.CompletedTask;
            }
        }
    }
}
