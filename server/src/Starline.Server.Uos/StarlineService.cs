using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Starline.Server.Data.CrudSave;
using Newtonsoft.Json.Linq;
using Starline.Server.Game;
using Unity.UOS.Func.Stateless.Core.Attributes;
using Unity.UOS.Func.Stateless.Core.Context;

namespace Starline.Cloud
{
    /// <summary>
    /// The game server as one UOS cloud function class. The client calls <see cref="Call"/> with a
    /// ServerRequest as JSON and gets a ServerResponse as JSON (Starline.Online, docs/server-design.md
    /// section 7). The client's Passport token arrives through FuncContext (header X-UOS-Token).
    /// </summary>
    [CloudService]
    public class StarlineService
    {
        private static readonly SemaphoreSlim OpenLock = new SemaphoreSlim(1, 1);
        private static readonly DateTime StartedAt = DateTime.UtcNow;
        private static GameServer _server;
        private static CrudSaveStore _store;

        [CloudFunc]
        public async Task<string> Call(string request)
        {
            GameServer server = await Server();
            return await server.HandleJson(request, PassportUserId);
        }

        /// <summary>
        /// Self-check after a deployment (server/README.md): call it from the console's "调试函数" with an
        /// empty body. Changes nothing; reports the server version, which environment variables are set
        /// (never their values), whether the database answers and whether Passport tokens can be checked.
        /// </summary>
        [CloudFunc]
        public async Task<string> Status()
        {
            var items = new JArray();
            bool allOk = true;
            void Item(string name, bool ok, long ms, string detail)
            {
                allOk &= ok;
                items.Add(new JObject { ["name"] = name, ["ok"] = ok, ["ms"] = ms, ["detail"] = detail });
            }

            bool devLogin = DevLoginAllowed();
            Item("服务端版本", true, 0, GameServer.Version);
            Item("开发者登录", true, 0, devLogin ? "接受（只该出现在开发用的 UOS 应用上）" : "不接受");
            Item("环境变量", true, 0, string.Join("，",
                Describe("STARLINE_DEV_LOGIN"), Describe("STARLINE_DB_ID"), Describe("STARLINE_DB_PASSWORD"),
                Describe("STARLINE_WX_APPID"), Describe("STARLINE_WX_SECRET"),
                Describe("UOS_APP_ID"), Describe("UOS_APP_SERVICE_SECRET")));

            var watch = Stopwatch.StartNew();
            try
            {
                await Server();
                long openMs = watch.ElapsedMilliseconds;
                watch.Restart();
                await _store.FindIdentity("status:check");
                Item("数据库", true, watch.ElapsedMilliseconds, "连接和建索引 " + openMs + " ms（只在这个实例第一次调用时发生），读一次 " + watch.ElapsedMilliseconds + " ms");
            }
            catch (Exception e)
            {
                Item("数据库", false, watch.ElapsedMilliseconds, e.GetBaseException().Message);
            }

            bool canValidate = typeof(FuncContext).GetMethod("ValidateToken", Type.EmptyTypes) != null
                && typeof(FuncContext).GetMethod("GetUserId", Type.EmptyTypes) != null;
            Item("Passport 令牌校验", canValidate, 0, canValidate
                ? "FuncContext.ValidateToken / GetUserId 可用"
                : "SDK 里没有 FuncContext.ValidateToken / GetUserId，UOS 账号登录会失败，请升级 Func Stateless SDK");

            bool wechat = WechatLogin.FromEnvironment() != null;
            Item("微信登录", true, 0, wechat
                ? "已配置（STARLINE_WX_APPID、STARLINE_WX_SECRET）"
                : "没配置：小游戏的微信登录会失败。在云函数环境变量里填小游戏的 AppID 和 AppSecret");

            var report = new JObject
            {
                ["allOk"] = allOk,
                ["serverTime"] = DateTime.UtcNow.ToString("o"),
                ["instanceStartedAt"] = StartedAt.ToString("o"),
                ["items"] = items,
            };
            return report.ToString();
        }

        private static string Describe(string variable) =>
            variable + (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(variable)) ? " 未设置" : " 已设置");

        /// <summary>
        /// The developer login is for the development UOS app (STARLINE_DEV_LOGIN=1) and for the Func
        /// Stateless panel's local mode, where this code runs inside the Tuanjie editor. On UOS the
        /// function runs on Tencent Cloud SCF (TENCENTCLOUD_RUNENV=SCF, the variable UOS's CRUD SDK
        /// checks too), so the editor rule can never open the developer login on the real server.
        /// </summary>
        private static bool DevLoginAllowed()
        {
            if (Environment.GetEnvironmentVariable("STARLINE_DEV_LOGIN") == "1")
            {
                return true;
            }
#if UNITY_EDITOR
            return Environment.GetEnvironmentVariable("TENCENTCLOUD_RUNENV") == null;
#else
            return false;
#endif
        }

        /// <summary>One server per function instance: the database connection is reused by warm calls.</summary>
        private static async Task<GameServer> Server()
        {
            if (_server != null)
            {
                return _server;
            }
            await OpenLock.WaitAsync();
            try
            {
                if (_server == null)
                {
                    // Database id and password stay empty while the app has one database and UOS keeps its password.
                    CrudSaveStore store = await CrudSaveStore.OpenUos(
                        Environment.GetEnvironmentVariable("STARLINE_DB_ID") ?? "",
                        Environment.GetEnvironmentVariable("STARLINE_DB_PASSWORD") ?? "");
                    await store.EnsureIndexes();
                    _store = store;
                    _server = new GameServer(store, new ServerOptions
                    {
                        AllowDevLogin = DevLoginAllowed(),
                        WechatOpenId = WechatLogin.FromEnvironment(Console.WriteLine) is WechatLogin wechat ? wechat.OpenId : null,
                    });
                }
                return _server;
            }
            finally
            {
                OpenLock.Release();
            }
        }

        /// <summary>
        /// Passport user id of the caller's token, or null. FuncContext.ValidateToken checks the token
        /// (a JWT) and stores the user id for GetUserId (UOS docs, "开发指南(C#)", FuncContext). The
        /// NuGet SDK this project compiles against predates both methods and the docs do not give their
        /// return types, so they are looked up at run time; the SDK installed in Tuanjie has them.
        /// </summary>
        private static async Task<string> PassportUserId()
        {
            if (string.IsNullOrEmpty(FuncContext.GetToken()))
            {
                return null;
            }
            MethodInfo validate = typeof(FuncContext).GetMethod("ValidateToken", Type.EmptyTypes);
            MethodInfo getUserId = typeof(FuncContext).GetMethod("GetUserId", Type.EmptyTypes);
            if (validate == null || getUserId == null)
            {
                throw new InvalidOperationException("FuncContext.ValidateToken / GetUserId 不存在：请把 Func Stateless SDK 升级到带 Passport 校验的版本。");
            }
            try
            {
                object result = validate.Invoke(null, null);
                if (result is Task task)
                {
                    await task;
                    result = task.GetType().GetProperty("Result")?.GetValue(task);
                }
                if (result is bool valid && !valid)
                {
                    return null;
                }
            }
            catch (Exception)
            {
                // An invalid or expired token: the login is refused.
                return null;
            }
            return getUserId.Invoke(null, null) as string;
        }
    }
}
