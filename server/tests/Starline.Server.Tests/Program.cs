using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Starline.Online;
using Starline.Server.Data;
using Starline.Server.Data.CrudSave;
using Starline.Server.Game;

namespace Starline.Server.Tests
{
    /// <summary>Same minimal runner as tests/Starline.Battle.Tests: each test is registered with Run in Main.</summary>
    internal static class Program
    {
        private static int _failures;
        private static Func<IServerStore> _newStore = () => new MemoryServerStore();
        private static long _now;
        private static int _random;

        private static async Task<int> Main(string[] args)
        {
            int mongo = Array.IndexOf(args, "--mongo");
            string connection = mongo >= 0 && mongo + 1 < args.Length ? args[mongo + 1] : null;
            for (int pass = 0; pass < (connection == null ? 1 : 2); pass++)
            {
                if (pass == 1)
                {
                    Console.WriteLine();
                    Console.WriteLine("—— MongoDB（" + connection + "）——");
                    _newStore = () => CrudSaveStore.OpenLocal(connection, "starline_test_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                }
                await Run("登录：开发者登录默认关闭", DevLoginDisabledByDefault);
                await Run("登录：同一身份再次登录还是同一个账号", SameIdentitySameAccount);
                await Run("登录：UOS 令牌无效时拒绝", UosLoginNeedsValidToken);
                await Run("登录：新的登录让旧会话失效（同时只能在一台设备上）", NewLoginReplacesSession);
                await Run("登录：长时间不用的会话过期，退出登录后会话失效", SessionExpiresAndLogout);
                await Run("存档：新建、列出、存档位被占用、名字和存档位不合法", CreateAndListSaves);
                await Run("存档：同一个请求重发只执行一次", WritesAreIdempotent);
                await Run("存档：删除后存档位空出来，旧存档留在已删除里", DeleteFreesSlot);
                await Run("存档：游戏时长按实际经过的时间封顶", PlayTimeIsCapped);
                await Run("存档：条件写冲突时重读重做", ConflictsAreRetried);
                await Run("消息：JSON 往返、坏请求、没带会话", JsonEntryPoint);
            }
            Console.WriteLine();
            Console.WriteLine(_failures == 0 ? "全部通过" : _failures + " 项失败");
            return _failures == 0 ? 0 : 1;
        }

        private static async Task Run(string name, Func<Task> test)
        {
            try
            {
                _now = 1_800_000_000_000;
                _random = 0;
                await test();
                Console.WriteLine("✓ " + name);
            }
            catch (Exception e)
            {
                _failures++;
                Console.WriteLine("✗ " + name + "：" + e.Message);
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new Exception(message);
            }
        }

        private static void Expect(ServerResponse response, string code, string what)
        {
            Assert(response.code == code, what + "：期望 “" + code + "”，实际 “" + response.code + "”");
        }

        private static ServerOptions Options(bool dev = true) => new ServerOptions
        {
            AllowDevLogin = dev,
            Clock = () => _now,
            RandomHex = bytes => (++_random).ToString("x").PadLeft(bytes * 2, '0'),
        };

        private static async Task<string> DevLogin(GameServer server, string account = "tester")
        {
            ServerResponse r = await server.Handle(new ServerRequest { op = ServerOps.Login, method = ServerOps.LoginMethodDev, devAccount = account }, null);
            Expect(r, ServerErrors.Ok, "开发者登录");
            return r.session;
        }

        private static ServerRequest Write(string session, string op, int slot, string name = null) =>
            new ServerRequest { op = op, session = session, key = Guid.NewGuid().ToString(), slot = slot, name = name };

        // ------------------------------------------------------------------

        private static async Task DevLoginDisabledByDefault()
        {
            var server = new GameServer(_newStore(), Options(dev: false));
            ServerResponse r = await server.Handle(new ServerRequest { op = ServerOps.Login, method = ServerOps.LoginMethodDev, devAccount = "x" }, null);
            Expect(r, ServerErrors.DevLoginDisabled, "正式环境的开发者登录");
        }

        private static async Task SameIdentitySameAccount()
        {
            var server = new GameServer(_newStore(), Options());
            var login = new ServerRequest { op = ServerOps.Login, method = ServerOps.LoginMethodUos };
            ServerResponse first = await server.Handle(login, () => Task.FromResult("passport-user-1"));
            ServerResponse again = await server.Handle(login, () => Task.FromResult("passport-user-1"));
            ServerResponse other = await server.Handle(login, () => Task.FromResult("passport-user-2"));
            Expect(first, ServerErrors.Ok, "UOS 登录");
            Assert(first.accountId == again.accountId, "同一个 Passport 用户应是同一个账号");
            Assert(first.accountId != other.accountId, "不同 Passport 用户应是不同账号");
            ServerResponse dev = await server.Handle(new ServerRequest { op = ServerOps.Login, method = ServerOps.LoginMethodDev, devAccount = "passport-user-1" }, null);
            Assert(dev.accountId != first.accountId, "开发者账号不能冒用 Passport 账号");
            Assert(dev.displayName == "passport-user-1", "开发者登录的显示名是账号名");
        }

        private static async Task UosLoginNeedsValidToken()
        {
            var server = new GameServer(_newStore(), Options());
            ServerResponse r = await server.Handle(new ServerRequest { op = ServerOps.Login, method = ServerOps.LoginMethodUos }, () => Task.FromResult<string>(null));
            Expect(r, ServerErrors.LoginFailed, "令牌无效");
            r = await server.Handle(new ServerRequest { op = ServerOps.Login, method = "steam" }, null);
            Expect(r, ServerErrors.BadRequest, "未知登录方式");
        }

        private static async Task NewLoginReplacesSession()
        {
            var server = new GameServer(_newStore(), Options());
            string first = await DevLogin(server);
            string second = await DevLogin(server);
            Assert(first != second, "每次登录应发新会话");
            Expect(await server.Handle(new ServerRequest { op = ServerOps.ListSaves, session = first }, null), ServerErrors.SessionReplaced, "旧会话");
            Expect(await server.Handle(new ServerRequest { op = ServerOps.ListSaves, session = second }, null), ServerErrors.Ok, "新会话");
            Expect(await server.Handle(new ServerRequest { op = ServerOps.ListSaves, session = "nobody.123" }, null), ServerErrors.Unauthorized, "不存在的账号");
        }

        private static async Task SessionExpiresAndLogout()
        {
            var server = new GameServer(_newStore(), Options());
            string session = await DevLogin(server);
            _now += new ServerOptions().SessionIdleMs - 1000;
            Expect(await server.Handle(new ServerRequest { op = ServerOps.ListSaves, session = session }, null), ServerErrors.Ok, "快到期时还能用（并续期）");
            _now += new ServerOptions().SessionIdleMs - 1000;
            Expect(await server.Handle(new ServerRequest { op = ServerOps.ListSaves, session = session }, null), ServerErrors.Ok, "续期后还能用");
            _now += new ServerOptions().SessionIdleMs + 1000;
            Expect(await server.Handle(new ServerRequest { op = ServerOps.ListSaves, session = session }, null), ServerErrors.SessionExpired, "过期");
            session = await DevLogin(server);
            Expect(await server.Handle(new ServerRequest { op = ServerOps.Logout, session = session }, null), ServerErrors.Ok, "退出登录");
            Expect(await server.Handle(new ServerRequest { op = ServerOps.ListSaves, session = session }, null), ServerErrors.Unauthorized, "退出后");
        }

        private static async Task CreateAndListSaves()
        {
            var server = new GameServer(_newStore(), Options());
            string session = await DevLogin(server);
            ServerResponse created = await server.Handle(Write(session, ServerOps.CreateSave, 2, "  艾琳  "), null);
            Expect(created, ServerErrors.Ok, "新建存档");
            Assert(created.save.name == "艾琳" && created.save.slot == 2 && created.save.createdAt == _now, "存档内容：" + JsonConvert.SerializeObject(created.save));
            Expect(await server.Handle(Write(session, ServerOps.CreateSave, 0, "第二位"), null), ServerErrors.Ok, "第二个存档");
            Expect(await server.Handle(Write(session, ServerOps.CreateSave, 2, "又一位"), null), ServerErrors.SlotTaken, "占用的存档位");
            Expect(await server.Handle(Write(session, ServerOps.CreateSave, 1, "一二三四五六七八九"), null), ServerErrors.InvalidName, "9 个字的名字");
            Expect(await server.Handle(Write(session, ServerOps.CreateSave, 1, "   "), null), ServerErrors.InvalidName, "空名字");
            Expect(await server.Handle(Write(session, ServerOps.CreateSave, ServerLimits.SlotCount, "越界"), null), ServerErrors.BadSlot, "越界的存档位");
            ServerResponse list = await server.Handle(new ServerRequest { op = ServerOps.ListSaves, session = session }, null);
            Assert(list.saves.Select(s => s.slot).SequenceEqual(new[] { 0, 2 }), "列表按存档位排序");

            string otherSession = await DevLogin(server, "someone-else");
            ServerResponse otherList = await server.Handle(new ServerRequest { op = ServerOps.ListSaves, session = otherSession }, null);
            Assert(otherList.saves.Length == 0, "看不到别的账号的存档");
        }

        private static async Task WritesAreIdempotent()
        {
            var server = new GameServer(_newStore(), Options());
            string session = await DevLogin(server);
            ServerRequest create = Write(session, ServerOps.CreateSave, 0, "重发");
            ServerResponse first = await server.Handle(create, null);
            ServerResponse again = await server.Handle(create, null);
            Expect(again, ServerErrors.Ok, "重发的新建（不是“存档位被占用”）");
            Assert(again.save.revision == first.save.revision, "重发返回第一次的结果");

            _now += 30_000;
            ServerRequest report = Write(session, ServerOps.ReportPlay, 0);
            report.playMs = 20_000;
            report.runsStarted = 1;
            await server.Handle(report, null);
            ServerResponse twice = await server.Handle(report, null);
            Assert(twice.save.playMs == 20_000 && twice.save.runsStarted == 1, "重发的时长只算一次：" + JsonConvert.SerializeObject(twice.save));
            Expect(await server.Handle(new ServerRequest { op = ServerOps.CreateSave, session = session, slot = 1, name = "无键" }, null), ServerErrors.BadRequest, "修改请求必须带幂等键");

            // Executed, but the answer was not recorded (crash in between): the resent create still succeeds.
            IServerStore store = _newStore();
            server = new GameServer(store, Options());
            session = await DevLogin(server);
            ServerRequest lost = Write(session, ServerOps.CreateSave, 1, "丢了回复");
            string account = session.Substring(0, session.IndexOf('.'));
            await store.InsertSave(new SaveRecord { AccountId = account, Slot = 1, Revision = 1, Name = lost.name, CreateKey = lost.key });
            Expect(await server.Handle(lost, null), ServerErrors.Ok, "没记下回复的新建重发");
            Expect(await server.Handle(Write(session, ServerOps.CreateSave, 1, "别的请求"), null), ServerErrors.SlotTaken, "别的请求仍是存档位被占用");
        }

        private static async Task DeleteFreesSlot()
        {
            IServerStore store = _newStore();
            var server = new GameServer(store, Options());
            string session = await DevLogin(server);
            await server.Handle(Write(session, ServerOps.CreateSave, 1, "旧的"), null);
            Expect(await server.Handle(Write(session, ServerOps.DeleteSave, 1), null), ServerErrors.Ok, "删除");
            Expect(await server.Handle(Write(session, ServerOps.DeleteSave, 1), null), ServerErrors.SlotEmpty, "删除空存档位");
            ServerResponse recreated = await server.Handle(Write(session, ServerOps.CreateSave, 1, "新的"), null);
            Expect(recreated, ServerErrors.Ok, "空出的存档位可以新建");
            if (store is MemoryServerStore memory)
            {
                Assert(memory.Archived.Count == 1 && memory.Archived[0].Name == "旧的", "删除的存档保留在已删除里");
            }
        }

        private static async Task PlayTimeIsCapped()
        {
            var server = new GameServer(_newStore(), Options());
            string session = await DevLogin(server);
            await server.Handle(Write(session, ServerOps.CreateSave, 0, "计时"), null);
            _now += 10 * 60_000;
            ServerRequest report = Write(session, ServerOps.ReportPlay, 0);
            report.playMs = 5L * 3600_000;
            report.runsStarted = 50;
            report.runsWon = 50;
            ServerResponse r = await server.Handle(report, null);
            Assert(r.save.playMs == 11 * 60_000, "10 分钟最多记 10 分钟 + 1 分钟余量，实际 " + r.save.playMs);
            Assert(r.save.runsStarted == 5 && r.save.runsWon == 5, "一次最多记 5 场远征");
            Assert(r.save.lastPlayedAt == _now, "更新最近游玩时间");

            ServerRequest negative = Write(session, ServerOps.ReportPlay, 0);
            negative.playMs = -1000;
            negative.runsWon = 3;
            r = await server.Handle(negative, null);
            Assert(r.save.playMs == 11 * 60_000 && r.save.runsWon == 5, "负数不减少；胜场不超过出发次数");
        }

        /// <summary>Fails the first conditional write of each save, as if another request wrote in between.</summary>
        private sealed class FlakyStore : IServerStore
        {
            private readonly IServerStore _inner;
            private readonly HashSet<string> _failed = new HashSet<string>();
            public int Conflicts;

            public FlakyStore(IServerStore inner) => _inner = inner;

            public async Task<bool> ReplaceSave(SaveRecord save, long expectedRevision)
            {
                if (_failed.Add(save.Id + "@" + expectedRevision))
                {
                    Conflicts++;
                    return false;
                }
                return await _inner.ReplaceSave(save, expectedRevision);
            }

            public Task<AccountRecord> GetAccount(string id) => _inner.GetAccount(id);
            public Task<bool> InsertAccount(AccountRecord account) => _inner.InsertAccount(account);
            public Task<bool> ReplaceAccount(AccountRecord account, long expectedRevision) => _inner.ReplaceAccount(account, expectedRevision);
            public Task<string> FindIdentity(string identity) => _inner.FindIdentity(identity);
            public Task<bool> InsertIdentity(string identity, string accountId) => _inner.InsertIdentity(identity, accountId);
            public Task<SaveRecord> GetSave(string accountId, int slot) => _inner.GetSave(accountId, slot);
            public Task<List<SaveRecord>> ListSaves(string accountId) => _inner.ListSaves(accountId);
            public Task<bool> InsertSave(SaveRecord save) => _inner.InsertSave(save);
            public Task<bool> ArchiveSave(SaveRecord save, long expectedRevision, long deletedAt) => _inner.ArchiveSave(save, expectedRevision, deletedAt);
            public Task<string> GetIdempotent(string accountId, string key) => _inner.GetIdempotent(accountId, key);
            public Task PutIdempotent(string accountId, string key, string response, long createdAt) => _inner.PutIdempotent(accountId, key, response, createdAt);
        }

        private static async Task ConflictsAreRetried()
        {
            var store = new FlakyStore(_newStore());
            var server = new GameServer(store, Options());
            string session = await DevLogin(server);
            await server.Handle(Write(session, ServerOps.CreateSave, 0, "冲突"), null);
            _now += 60_000;
            ServerRequest report = Write(session, ServerOps.ReportPlay, 0);
            report.playMs = 30_000;
            ServerResponse r = await server.Handle(report, null);
            Expect(r, ServerErrors.Ok, "冲突一次后重做成功");
            Assert(store.Conflicts == 1 && r.save.playMs == 30_000 && r.save.revision == 2, "只加一次时长：" + JsonConvert.SerializeObject(r.save));

            // Real conflicts in parallel: every report is kept.
            IServerStore shared = _newStore();
            server = new GameServer(shared, Options());
            session = await DevLogin(server);
            await server.Handle(Write(session, ServerOps.CreateSave, 0, "并发"), null);
            _now += 3600_000;
            var reports = Enumerable.Range(0, 3).Select(_ =>
            {
                ServerRequest q = Write(session, ServerOps.ReportPlay, 0);
                q.playMs = 1000;
                return server.Handle(q, null);
            }).ToArray();
            ServerResponse[] results = await Task.WhenAll(reports);
            ServerResponse list = await server.Handle(new ServerRequest { op = ServerOps.ListSaves, session = session }, null);
            long okCount = results.Count(x => x.code == ServerErrors.Ok);
            Assert(list.saves[0].playMs == okCount * 1000, "成功的请求都记上了：" + okCount + " 次，" + list.saves[0].playMs + " 毫秒");
        }

        private static async Task JsonEntryPoint()
        {
            var server = new GameServer(_newStore(), Options());
            string login = await server.HandleJson("{\"op\":\"auth.login\",\"method\":\"dev\",\"devAccount\":\"json\"}", null);
            var response = JsonConvert.DeserializeObject<ServerResponse>(login);
            Expect(response, ServerErrors.Ok, "JSON 登录");
            Assert(!login.Contains("\"saves\""), "空字段不输出：" + login);
            response = JsonConvert.DeserializeObject<ServerResponse>(await server.HandleJson("not json", null));
            Expect(response, ServerErrors.BadRequest, "坏 JSON");
            response = JsonConvert.DeserializeObject<ServerResponse>(await server.HandleJson("{\"op\":\"saves.list\"}", null));
            Expect(response, ServerErrors.Unauthorized, "没带会话");
            response = JsonConvert.DeserializeObject<ServerResponse>(await server.HandleJson("{\"op\":\"saves.nope\",\"session\":\"" + response.session + "\"}", null));
            Expect(response, ServerErrors.Unauthorized, "未知操作也要先登录");
        }
    }
}
