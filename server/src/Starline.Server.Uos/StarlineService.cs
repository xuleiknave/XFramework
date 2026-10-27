using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Starline.Server.Data.CrudSave;
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
        private static GameServer _server;

        [CloudFunc]
        public async Task<string> Call(string request)
        {
            GameServer server = await Server();
            return await server.HandleJson(request, PassportUserId);
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
                    var options = new ServerOptions { AllowDevLogin = Environment.GetEnvironmentVariable("STARLINE_DEV_LOGIN") == "1" };
                    _server = new GameServer(store, options);
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
