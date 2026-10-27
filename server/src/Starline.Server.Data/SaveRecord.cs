namespace Starline.Server.Data
{
    /// <summary>One save slot of an account; the whole save is one document (CRUD Save stage).</summary>
    public sealed class SaveRecord
    {
        public string AccountId;
        public int Slot;
        public long Revision;
        public string Name;
        public long CreatedAt;
        public long LastPlayedAt;
        public long PlayMs;
        public int RunsStarted;
        public int RunsWon;
        /// <summary>Server time of the last play report, to cap the play time a report may add.</summary>
        public long LastReportAt;
        /// <summary>Idempotency key of the request that created the save: a resent create still succeeds.</summary>
        public string CreateKey;

        public string Id => IdOf(AccountId, Slot);

        public static string IdOf(string accountId, int slot) => accountId + ":" + slot;

        public SaveRecord Clone() => (SaveRecord)MemberwiseClone();
    }
}
