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
        /// <summary>Signs in (method: <see cref="LoginMethodUos"/>, <see cref="LoginMethodWechat"/> or <see cref="LoginMethodDev"/>); ends the account's other sessions.</summary>
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

        /// <summary>
        /// A one-time link code for this account (linkCode, linkExpiresAt): entered on another device,
        /// it puts that device's login on this account, so the mini game and the app share saves.
        /// </summary>
        public const string CreateLinkCode = "link.create";
        /// <summary>
        /// Moves the caller's login onto the account of a link code (code, keep). When both accounts have
        /// saves the answer is <see cref="ServerErrors.LinkChoice"/> until keep says which saves stay;
        /// the others go to the deleted saves. Answers like a login: the caller's new session on that account.
        /// </summary>
        public const string RedeemLinkCode = "link.redeem";

        /// <summary>UOS Passport: the server checks the Passport token sent along with the call.</summary>
        public const string LoginMethodUos = "uos";
        /// <summary>WeChat mini game: code is the one-time code from wx.login, which the server exchanges for the openid.</summary>
        public const string LoginMethodWechat = "wechat";
        public const string LoginMethodDev = "dev";

        /// <summary>Values of <see cref="ServerRequest.keep"/> for <see cref="RedeemLinkCode"/>.</summary>
        public const string KeepLinked = "linked";
        public const string KeepMine = "mine";

        /// <summary>Ops that change data and therefore need an idempotency key.</summary>
        public static bool IsWrite(string op) =>
            op == CreateSave || op == DeleteSave || op == ReportPlay || op == CreateLinkCode || op == RedeemLinkCode;
    }

    /// <summary>Rules the client checks for display and the server enforces.</summary>
    public static class ServerLimits
    {
        public const int SlotCount = 3;
        public const int MaxCommanderName = 8;
        public const int MaxDevAccount = 16;
        /// <summary>Digits of a link code.</summary>
        public const int LinkCodeLength = 6;
        public const long LinkCodeLifetimeMs = 5 * 60 * 1000;
        /// <summary>Wrong link codes an account may enter per <see cref="LinkFailureWindowMs"/>.</summary>
        public const int MaxLinkFailures = 5;
        public const long LinkFailureWindowMs = 10 * 60 * 1000;
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
        /// <summary>The link code is wrong, used or expired.</summary>
        public const string LinkInvalid = "link_invalid";
        /// <summary>The link code belongs to the caller's own account.</summary>
        public const string LinkSelf = "link_self";
        /// <summary>Too many wrong link codes; try again in a few minutes.</summary>
        public const string LinkLimited = "link_limited";
        /// <summary>
        /// Both accounts have saves: the answer lists the caller's saves (saves) and the other account's
        /// (linkedSaves); send the redeem again with keep = <see cref="ServerOps.KeepLinked"/> or <see cref="ServerOps.KeepMine"/>.
        /// </summary>
        public const string LinkChoice = "link_choice";
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
        /// <summary>wx.login code (WeChat login), or the link code (link.redeem).</summary>
        public string code;

        // link.redeem
        /// <summary><see cref="ServerOps.KeepLinked"/>, <see cref="ServerOps.KeepMine"/>, or empty while not asked.</summary>
        public string keep;

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

        // link.*
        public string linkCode;
        public long linkExpiresAt;
        /// <summary>With <see cref="ServerErrors.LinkChoice"/>: the saves of the account the code belongs to.</summary>
        public SaveInfo[] linkedSaves;
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
