using System.Collections.Generic;
using System.Threading.Tasks;

namespace Starline.Server.Data
{
    /// <summary>
    /// Everything the game server keeps. Implementations: <see cref="MemoryServerStore"/> (tests and
    /// local runs) and the CRUD Save one (MongoDB, Starline.Server.Data.CrudSave); a MySQL one follows
    /// for CRUD Storage (server/db/schema.sql). Writes that replace a record only succeed when the stored
    /// revision still equals <c>expectedRevision</c>, and return false otherwise.
    /// </summary>
    public interface IServerStore
    {
        Task<AccountRecord> GetAccount(string id);

        /// <summary>False when an account with this id already exists.</summary>
        Task<bool> InsertAccount(AccountRecord account);

        Task<bool> ReplaceAccount(AccountRecord account, long expectedRevision);

        /// <summary>Account id linked to a login identity, or null.</summary>
        Task<string> FindIdentity(string identity);

        /// <summary>Links an identity to an account; false when the identity is already linked.</summary>
        Task<bool> InsertIdentity(string identity, string accountId);

        /// <summary>The identities linked to an account, sorted.</summary>
        Task<List<string>> ListIdentities(string accountId);

        /// <summary>Links an identity to another account; false when it is no longer linked to <paramref name="fromAccount"/>.</summary>
        Task<bool> MoveIdentity(string identity, string fromAccount, string toAccount);

        /// <summary>False when the code is taken (expired codes included: the caller deletes those first).</summary>
        Task<bool> InsertLinkCode(LinkCodeRecord link);

        Task<LinkCodeRecord> GetLinkCode(string code);

        /// <summary>Removes a code; false when it was already gone (so only one redeem can win).</summary>
        Task<bool> DeleteLinkCode(string code);

        Task<SaveRecord> GetSave(string accountId, int slot);

        /// <summary>The account's saves, ordered by slot.</summary>
        Task<List<SaveRecord>> ListSaves(string accountId);

        /// <summary>False when the slot already has a save.</summary>
        Task<bool> InsertSave(SaveRecord save);

        Task<bool> ReplaceSave(SaveRecord save, long expectedRevision);

        /// <summary>Moves a save out of its slot into the deleted saves (kept for recovery); false on a revision mismatch.</summary>
        Task<bool> ArchiveSave(SaveRecord save, long expectedRevision, long deletedAt);

        /// <summary>Stored answer of a write already handled under this idempotency key, or null.</summary>
        Task<string> GetIdempotent(string accountId, string key);

        Task PutIdempotent(string accountId, string key, string response, long createdAt);
    }
}
