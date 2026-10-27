namespace Starline.Server.Data
{
    /// <summary>
    /// One player account. Times are Unix milliseconds (UTC). <see cref="Revision"/> grows by one on
    /// every write and guards against lost updates (conditional writes, server-database.md section 11).
    /// </summary>
    public sealed class AccountRecord
    {
        public string Id;
        public long Revision;
        /// <summary>Login identity this account was created from, e.g. "uos:&lt;Passport user id&gt;" or "dev:&lt;name&gt;".</summary>
        public string Identity;
        public string DisplayName;
        public long CreatedAt;
        public long LastLoginAt;
        /// <summary>Secret part of the only valid session; a new login replaces it (one device at a time).</summary>
        public string SessionSecret;
        public long SessionExpiresAt;

        public AccountRecord Clone() => (AccountRecord)MemberwiseClone();
    }
}
