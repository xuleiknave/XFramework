using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Starline.Online;
using Starline.Server.Data;

namespace Starline.Server.Game
{
    /// <summary>
    /// The game server's rules behind one entry point: a <see cref="ServerRequest"/> in, a
    /// <see cref="ServerResponse"/> out (docs/server-design.md sections 4 and 7). The UOS cloud function
    /// only parses the call and tells who the Passport token belongs to; everything else is here.
    /// </summary>
    public sealed class GameServer
    {
        /// <summary>Shown by the Status cloud function; bump it with each deployment that changes behavior.</summary>
        public const string Version = "S1 · 2026-10-28.3";

        /// <summary>Conditional writes retried this often before answering <see cref="ServerErrors.Conflict"/>.</summary>
        private const int WriteAttempts = 3;
        /// <summary>Play time a report may add beyond the time that really passed (clock and network slack).</summary>
        private const long PlaySlackMs = 60_000;
        private const int MaxRunsPerReport = 5;

        private static readonly JsonSerializerSettings Json = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };

        private readonly IServerStore _store;
        private readonly ServerOptions _options;

        public GameServer(IServerStore store, ServerOptions options = null)
        {
            _store = store;
            _options = options ?? new ServerOptions();
        }

        /// <summary>
        /// Handles one call given as JSON. <paramref name="passportUser"/> returns the Passport user id
        /// of the caller's token (null when the token is missing or invalid); it is only asked on a UOS login.
        /// </summary>
        public async Task<string> HandleJson(string requestJson, Func<Task<string>> passportUser)
        {
            ServerRequest request;
            try
            {
                request = JsonConvert.DeserializeObject<ServerRequest>(requestJson ?? "");
            }
            catch (JsonException)
            {
                request = null;
            }
            ServerResponse response = request == null ? Error(ServerErrors.BadRequest) : await Handle(request, passportUser);
            return JsonConvert.SerializeObject(response, Json);
        }

        public async Task<ServerResponse> Handle(ServerRequest request, Func<Task<string>> passportUser)
        {
            try
            {
                if (request.op == ServerOps.Login)
                {
                    return await Login(request, passportUser);
                }
                AccountRecord account = await Authenticate(request.session);
                if (!ServerOps.IsWrite(request.op))
                {
                    return await Read(account, request);
                }
                if (string.IsNullOrEmpty(request.key) || request.key.Length > 64)
                {
                    throw new ServerException(ServerErrors.BadRequest, "missing idempotency key");
                }
                string earlier = await _store.GetIdempotent(account.Id, request.key);
                if (earlier != null)
                {
                    return JsonConvert.DeserializeObject<ServerResponse>(earlier);
                }
                ServerResponse response = await Write(account, request);
                if (ServerErrors.IsOk(response.code))
                {
                    await _store.PutIdempotent(account.Id, request.key, JsonConvert.SerializeObject(response, Json), Now);
                }
                return response;
            }
            catch (ServerException e)
            {
                return Error(e.Code);
            }
        }

        private long Now => _options.Clock();

        private ServerResponse Error(string code) => new ServerResponse { code = code, serverTime = Now };

        private ServerResponse Ok() => new ServerResponse { code = ServerErrors.Ok, serverTime = Now };

        private async Task<ServerResponse> Read(AccountRecord account, ServerRequest request)
        {
            switch (request.op)
            {
                case ServerOps.Logout:
                    await Logout(account);
                    return Ok();
                case ServerOps.ListSaves:
                    List<SaveRecord> saves = await _store.ListSaves(account.Id);
                    ServerResponse response = Ok();
                    response.saves = saves.Select(ToInfo).ToArray();
                    return response;
                default:
                    throw new ServerException(ServerErrors.BadRequest, "unknown op " + request.op);
            }
        }

        private Task<ServerResponse> Write(AccountRecord account, ServerRequest request)
        {
            switch (request.op)
            {
                case ServerOps.CreateSave:
                    return CreateSave(account, request.slot, request.name, request.key);
                case ServerOps.DeleteSave:
                    return DeleteSave(account, request.slot);
                case ServerOps.ReportPlay:
                    return ReportPlay(account, request);
                case ServerOps.CreateLinkCode:
                    return CreateLinkCode(account);
                case ServerOps.RedeemLinkCode:
                    return RedeemLinkCode(account, request);
                default:
                    throw new ServerException(ServerErrors.BadRequest, "unknown op " + request.op);
            }
        }

        // ------------------------------------------------------------------
        // Login and sessions

        private async Task<ServerResponse> Login(ServerRequest request, Func<Task<string>> passportUser)
        {
            string identity;
            string displayName;
            switch (request.method)
            {
                case ServerOps.LoginMethodUos:
                    string userId = passportUser == null ? null : await passportUser();
                    if (string.IsNullOrEmpty(userId))
                    {
                        throw new ServerException(ServerErrors.LoginFailed);
                    }
                    identity = "uos:" + userId;
                    displayName = "";
                    break;
                case ServerOps.LoginMethodWechat:
                    string code = (request.code ?? "").Trim();
                    string openId = code.Length == 0 || code.Length > 128 || _options.WechatOpenId == null
                        ? null
                        : await _options.WechatOpenId(code);
                    if (string.IsNullOrEmpty(openId))
                    {
                        throw new ServerException(ServerErrors.LoginFailed);
                    }
                    identity = "wx:" + openId;
                    displayName = "";
                    break;
                case ServerOps.LoginMethodDev:
                    if (!_options.AllowDevLogin)
                    {
                        throw new ServerException(ServerErrors.DevLoginDisabled);
                    }
                    string name = (request.devAccount ?? "").Trim();
                    if (name.Length == 0 || name.Length > ServerLimits.MaxDevAccount)
                    {
                        throw new ServerException(ServerErrors.BadRequest, "dev account name");
                    }
                    identity = "dev:" + name;
                    displayName = name;
                    break;
                default:
                    throw new ServerException(ServerErrors.BadRequest, "login method");
            }

            AccountRecord account = await FindOrCreateAccount(identity, displayName);
            return await StartSession(account, displayName);
        }

        /// <summary>A new session on the account, answered like a login.</summary>
        private async Task<ServerResponse> StartSession(AccountRecord account, string displayName)
        {
            for (int attempt = 0; attempt < WriteAttempts; attempt++)
            {
                long expected = account.Revision;
                // A new secret ends the account's other sessions: one device at a time (server-design.md section 1).
                account.SessionSecret = _options.RandomHex(16);
                account.SessionExpiresAt = Now + _options.SessionIdleMs;
                account.LastLoginAt = Now;
                if (displayName.Length > 0)
                {
                    account.DisplayName = displayName;
                }
                account.Revision = expected + 1;
                if (await _store.ReplaceAccount(account, expected))
                {
                    ServerResponse response = Ok();
                    response.accountId = account.Id;
                    response.session = account.Id + "." + account.SessionSecret;
                    response.displayName = account.DisplayName;
                    return response;
                }
                account = await _store.GetAccount(account.Id);
            }
            throw new ServerException(ServerErrors.Conflict);
        }

        private async Task<AccountRecord> FindOrCreateAccount(string identity, string displayName)
        {
            string accountId = await _store.FindIdentity(identity);
            if (accountId == null)
            {
                string newId = "a" + _options.RandomHex(8);
                // Link first: when two logins race, only one link wins and both use its account.
                if (await _store.InsertIdentity(identity, newId))
                {
                    var created = new AccountRecord
                    {
                        Id = newId,
                        Revision = 0,
                        Identity = identity,
                        DisplayName = displayName,
                        CreatedAt = Now,
                    };
                    await _store.InsertAccount(created);
                }
                accountId = await _store.FindIdentity(identity);
            }
            AccountRecord account = await _store.GetAccount(accountId);
            if (account == null)
            {
                // The link was written but the account insert was lost (crash between the two writes): create it now.
                account = new AccountRecord { Id = accountId, Identity = identity, DisplayName = displayName, CreatedAt = Now };
                await _store.InsertAccount(account);
                account = await _store.GetAccount(accountId);
            }
            return account;
        }

        /// <summary>The account of a session token "&lt;account id&gt;.&lt;secret&gt;"; refreshes its idle timeout.</summary>
        private async Task<AccountRecord> Authenticate(string session)
        {
            int dot = session == null ? -1 : session.IndexOf('.');
            if (dot <= 0)
            {
                throw new ServerException(ServerErrors.Unauthorized);
            }
            AccountRecord account = await _store.GetAccount(session.Substring(0, dot));
            if (account == null || string.IsNullOrEmpty(account.SessionSecret))
            {
                throw new ServerException(ServerErrors.Unauthorized);
            }
            if (!FixedTimeEquals(account.SessionSecret, session.Substring(dot + 1)))
            {
                throw new ServerException(ServerErrors.SessionReplaced);
            }
            if (account.SessionExpiresAt < Now)
            {
                throw new ServerException(ServerErrors.SessionExpired);
            }
            // Refresh the idle timeout at most once an hour, so most calls stay a single read.
            if (account.SessionExpiresAt - Now < _options.SessionIdleMs - 3600_000)
            {
                long expected = account.Revision;
                account.SessionExpiresAt = Now + _options.SessionIdleMs;
                account.Revision = expected + 1;
                if (!await _store.ReplaceAccount(account, expected))
                {
                    account = await _store.GetAccount(account.Id);
                }
            }
            return account;
        }

        private async Task Logout(AccountRecord account)
        {
            long expected = account.Revision;
            account.SessionSecret = "";
            account.Revision = expected + 1;
            await _store.ReplaceAccount(account, expected);
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }
            int diff = 0;
            for (int i = 0; i < a.Length; i++)
            {
                diff |= a[i] ^ b[i];
            }
            return diff == 0;
        }

        // ------------------------------------------------------------------
        // Link codes: one account for the mini game and the app (server-design.md section 1)

        private async Task<ServerResponse> CreateLinkCode(AccountRecord account)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                string code = RandomDigits(ServerLimits.LinkCodeLength);
                LinkCodeRecord existing = await _store.GetLinkCode(code);
                if (existing != null && existing.ExpiresAt < Now)
                {
                    await _store.DeleteLinkCode(code);
                }
                var link = new LinkCodeRecord { Code = code, AccountId = account.Id, ExpiresAt = Now + ServerLimits.LinkCodeLifetimeMs };
                if (await _store.InsertLinkCode(link))
                {
                    ServerResponse response = Ok();
                    response.linkCode = code;
                    response.linkExpiresAt = link.ExpiresAt;
                    return response;
                }
            }
            throw new ServerException(ServerErrors.Conflict);
        }

        private string RandomDigits(int count)
        {
            string hex = _options.RandomHex(8);
            ulong value = Convert.ToUInt64(hex, 16);
            ulong modulo = 1;
            for (int i = 0; i < count; i++)
            {
                modulo *= 10;
            }
            return (value % modulo).ToString().PadLeft(count, '0');
        }

        /// <summary>
        /// Moves the caller's logins onto the code's account, keeping one side's saves (the other side's go to
        /// the deleted saves, kept for recovery), and answers with the caller's new session on that account.
        /// Every step can run again: a retry after a failure in the middle finishes the move.
        /// </summary>
        private async Task<ServerResponse> RedeemLinkCode(AccountRecord me, ServerRequest request)
        {
            if (me.LinkFailures >= ServerLimits.MaxLinkFailures && Now - me.LinkFailuresSince < ServerLimits.LinkFailureWindowMs)
            {
                throw new ServerException(ServerErrors.LinkLimited);
            }
            string code = (request.code ?? "").Trim();
            LinkCodeRecord link = code.Length == ServerLimits.LinkCodeLength && code.All(c => c >= '0' && c <= '9')
                ? await _store.GetLinkCode(code)
                : null;
            AccountRecord target = link == null || link.ExpiresAt < Now ? null : await _store.GetAccount(link.AccountId);
            if (target == null || target.MergedInto != null)
            {
                await CountLinkFailure(me);
                throw new ServerException(ServerErrors.LinkInvalid);
            }
            if (target.Id == me.Id)
            {
                throw new ServerException(ServerErrors.LinkSelf);
            }

            List<SaveRecord> mine = await _store.ListSaves(me.Id);
            List<SaveRecord> theirs = await _store.ListSaves(target.Id);
            bool keepMine;
            if (mine.Count == 0 || theirs.Count == 0)
            {
                keepMine = mine.Count > 0;
            }
            else if (request.keep == ServerOps.KeepMine || request.keep == ServerOps.KeepLinked)
            {
                keepMine = request.keep == ServerOps.KeepMine;
            }
            else
            {
                ServerResponse choice = Error(ServerErrors.LinkChoice);
                choice.saves = mine.Select(ToInfo).ToArray();
                choice.linkedSaves = theirs.Select(ToInfo).ToArray();
                return choice;
            }

            if (keepMine)
            {
                foreach (SaveRecord save in theirs)
                {
                    await ArchiveSlot(target.Id, save.Slot);
                }
                foreach (SaveRecord save in mine)
                {
                    SaveRecord moved = save.Clone();
                    moved.AccountId = target.Id;
                    moved.Revision = 1;
                    // A retry after the copy but before the archive finds the copy already there.
                    await _store.InsertSave(moved);
                    await ArchiveSlot(me.Id, save.Slot);
                }
            }
            else
            {
                foreach (SaveRecord save in mine)
                {
                    await ArchiveSlot(me.Id, save.Slot);
                }
            }
            foreach (string identity in await _store.ListIdentities(me.Id))
            {
                await _store.MoveIdentity(identity, me.Id, target.Id);
            }
            await RetireAccount(me, target.Id);
            await _store.DeleteLinkCode(code);
            return await StartSession(target, "");
        }

        private async Task CountLinkFailure(AccountRecord account)
        {
            for (int attempt = 0; attempt < WriteAttempts; attempt++)
            {
                long expected = account.Revision;
                if (Now - account.LinkFailuresSince >= ServerLimits.LinkFailureWindowMs)
                {
                    account.LinkFailures = 0;
                    account.LinkFailuresSince = Now;
                }
                account.LinkFailures++;
                account.Revision = expected + 1;
                if (await _store.ReplaceAccount(account, expected))
                {
                    return;
                }
                account = await _store.GetAccount(account.Id);
            }
        }

        private async Task RetireAccount(AccountRecord account, string mergedInto)
        {
            for (int attempt = 0; attempt < WriteAttempts; attempt++)
            {
                long expected = account.Revision;
                account.SessionSecret = "";
                account.MergedInto = mergedInto;
                account.Revision = expected + 1;
                if (await _store.ReplaceAccount(account, expected))
                {
                    return;
                }
                account = await _store.GetAccount(account.Id);
            }
            throw new ServerException(ServerErrors.Conflict);
        }

        /// <summary>Moves the save in a slot to the deleted saves; nothing when the slot is already empty.</summary>
        private async Task ArchiveSlot(string accountId, int slot)
        {
            for (int attempt = 0; attempt < WriteAttempts; attempt++)
            {
                SaveRecord save = await _store.GetSave(accountId, slot);
                if (save == null || await _store.ArchiveSave(save, save.Revision, Now))
                {
                    return;
                }
            }
            throw new ServerException(ServerErrors.Conflict);
        }

        // ------------------------------------------------------------------
        // Saves

        private static void CheckSlot(int slot)
        {
            if (slot < 0 || slot >= ServerLimits.SlotCount)
            {
                throw new ServerException(ServerErrors.BadSlot);
            }
        }

        /// <summary>Trimmed commander name, or an <see cref="ServerErrors.InvalidName"/> error (same rule as the client's dialog).</summary>
        public static string CheckName(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0 || name.Length > ServerLimits.MaxCommanderName || name.Any(char.IsControl))
            {
                throw new ServerException(ServerErrors.InvalidName);
            }
            return name;
        }

        private async Task<ServerResponse> CreateSave(AccountRecord account, int slot, string name, string key)
        {
            CheckSlot(slot);
            var save = new SaveRecord
            {
                AccountId = account.Id,
                Slot = slot,
                Revision = 1,
                Name = CheckName(name),
                CreatedAt = Now,
                LastPlayedAt = Now,
                LastReportAt = Now,
                CreateKey = key,
            };
            if (!await _store.InsertSave(save))
            {
                // Created by this same request, whose answer was lost before it was recorded.
                SaveRecord existing = await _store.GetSave(account.Id, slot);
                if (existing == null || existing.CreateKey != key)
                {
                    throw new ServerException(ServerErrors.SlotTaken);
                }
                save = existing;
            }
            ServerResponse response = Ok();
            response.save = ToInfo(save);
            return response;
        }

        private async Task<ServerResponse> DeleteSave(AccountRecord account, int slot)
        {
            CheckSlot(slot);
            for (int attempt = 0; attempt < WriteAttempts; attempt++)
            {
                SaveRecord save = await _store.GetSave(account.Id, slot);
                if (save == null)
                {
                    throw new ServerException(ServerErrors.SlotEmpty);
                }
                if (await _store.ArchiveSave(save, save.Revision, Now))
                {
                    return Ok();
                }
            }
            throw new ServerException(ServerErrors.Conflict);
        }

        /// <summary>
        /// Adds play time and expedition counts. Until expeditions run on the server (stage S4) these come
        /// from the client, so they are only capped: play time by the time that really passed since the
        /// last report and since this login, runs by a few per report. They are shown, never rewarded.
        /// </summary>
        private async Task<ServerResponse> ReportPlay(AccountRecord account, ServerRequest request)
        {
            CheckSlot(request.slot);
            for (int attempt = 0; attempt < WriteAttempts; attempt++)
            {
                SaveRecord save = await _store.GetSave(account.Id, request.slot);
                if (save == null)
                {
                    throw new ServerException(ServerErrors.SlotEmpty);
                }
                long expected = save.Revision;
                long since = Math.Max(save.LastReportAt, account.LastLoginAt);
                long maxPlay = Math.Max(0, Now - since) + PlaySlackMs;
                save.PlayMs += Math.Min(Math.Max(0, request.playMs), maxPlay);
                save.RunsStarted += Math.Min(Math.Max(0, request.runsStarted), MaxRunsPerReport);
                save.RunsWon = Math.Min(save.RunsWon + Math.Min(Math.Max(0, request.runsWon), MaxRunsPerReport), save.RunsStarted);
                save.LastPlayedAt = Now;
                save.LastReportAt = Now;
                save.Revision = expected + 1;
                if (await _store.ReplaceSave(save, expected))
                {
                    ServerResponse response = Ok();
                    response.save = ToInfo(save);
                    return response;
                }
            }
            throw new ServerException(ServerErrors.Conflict);
        }

        private static SaveInfo ToInfo(SaveRecord s) => new SaveInfo
        {
            slot = s.Slot,
            name = s.Name,
            revision = s.Revision,
            createdAt = s.CreatedAt,
            lastPlayedAt = s.LastPlayedAt,
            playMs = s.PlayMs,
            runsStarted = s.RunsStarted,
            runsWon = s.RunsWon,
        };
    }
}
