using System;

namespace Starline.Online
{
    /// <summary>
    /// Messages between the client and the game server (UOS cloud functions, docs/server-design.md
    /// section 7). Plain fields so Unity's JsonUtility (client) and Newtonsoft.Json (server) read the
    /// same JSON. Every call is one <see cref="ServerRequest"/> answered by one <see cref="ServerResponse"/>.
    /// </summary>
    public static class ServerOps
    {
        /// <summary>Signs in (method: <see cref="LoginMethodUos"/> or <see cref="LoginMethodDev"/>); ends the account's other sessions.</summary>
        public const string Login = "auth.login";
        public const string Logout = "auth.logout";
        /// <summary>All save slots of the account.</summary>
        public const string ListSaves = "saves.list";
        /// <summary>New commander in an empty slot (slot, name).</summary>
        public const string CreateSave = "saves.create";
        /// <summary>Deletes the save in a slot (kept on the server for 30 days).</summary>
        public const string DeleteSave = "saves.delete";
        /// <summary>Play time and expedition counts since the last report (slot, playMs, runsStarted, runsWon).</summary>
        public const string ReportPlay = "saves.report";

        public const string LoginMethodUos = "uos";
        public const string LoginMethodDev = "dev";

        /// <summary>Ops that change data and therefore need an idempotency key.</summary>
        public static bool IsWrite(string op) => op == CreateSave || op == DeleteSave || op == ReportPlay;
    }

    /// <summary>Rules the client checks for display and the server enforces.</summary>
    public static class ServerLimits
    {
        public const int SlotCount = 3;
        public const int MaxCommanderName = 8;
        public const int MaxDevAccount = 16;
    }

    /// <summary>Values of <see cref="ServerResponse.code"/>; the client shows UI.csv "server.error.&lt;code&gt;".</summary>
    public static class ServerErrors
    {
        public const string Ok = "";
        public const string BadRequest = "bad_request";
        /// <summary>No session, or a session the server does not know: sign in again.</summary>
        public const string Unauthorized = "unauthorized";
        /// <summary>The account signed in on another device.</summary>
        public const string SessionReplaced = "session_replaced";
        public const string SessionExpired = "session_expired";
        public const string LoginFailed = "login_failed";
        public const string DevLoginDisabled = "dev_login_disabled";
        public const string InvalidName = "invalid_name";
        public const string BadSlot = "bad_slot";
        public const string SlotTaken = "slot_taken";
        public const string SlotEmpty = "slot_empty";
        /// <summary>Too many concurrent changes to the same data; safe to retry.</summary>
        public const string Conflict = "conflict";
        public const string ServerError = "server_error";

        public static bool IsOk(string code) => string.IsNullOrEmpty(code);
    }

    [Serializable]
    public sealed class ServerRequest
    {
        public string op;
        /// <summary>Session token from the login response; empty for <see cref="ServerOps.Login"/>.</summary>
        public string session;
        /// <summary>Client-generated id of a write; the same key sent again returns the first answer.</summary>
        public string key;

        // auth.login
        public string method;
        /// <summary>Account name for the developer login (accepted only when the server allows it).</summary>
        public string devAccount;

        // saves.*
        public int slot;
        public string name;
        public long playMs;
        public int runsStarted;
        public int runsWon;
    }

    [Serializable]
    public sealed class ServerResponse
    {
        /// <summary><see cref="ServerErrors.Ok"/> (empty) on success.</summary>
        public string code;
        /// <summary>Server time, Unix milliseconds (UTC).</summary>
        public long serverTime;

        // auth.login
        public string accountId;
        public string session;
        public string displayName;

        // saves.*
        public SaveInfo[] saves;
        public SaveInfo save;
    }

    /// <summary>One save slot as the server stores it. Times are Unix milliseconds (UTC).</summary>
    [Serializable]
    public sealed class SaveInfo
    {
        public int slot;
        public string name;
        public long revision;
        public long createdAt;
        public long lastPlayedAt;
        public long playMs;
        public int runsStarted;
        public int runsWon;
    }
}
