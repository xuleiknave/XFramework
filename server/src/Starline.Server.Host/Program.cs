using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Starline.Server.Data;
using Starline.Server.Data.CrudSave;
using Starline.Server.Game;

namespace Starline.Server.Host
{
    /// <summary>
    /// The game server as a long-running HTTP process (a UOS "服务器程序镜像" on dotnet-runtime 9.0).
    /// Same rules and data as the cloud function: POST /call takes a ServerRequest as JSON and answers a
    /// ServerResponse; GET /status is the self-check. See server/README.md.
    /// </summary>
    internal static class Program
    {
        private const int MaxBodyBytes = 64 * 1024;
        private static DateTime _startedAt;

        private static async Task<int> Main(string[] args)
        {
            _startedAt = DateTime.UtcNow;
            HostOptions options;
            try
            {
                options = HostOptions.Parse(args);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("参数错误：" + e.Message);
                return 2;
            }

            IServerStore store;
            string storeName;
            try
            {
                (store, storeName) = await OpenStore(options);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("连接数据库失败：" + e.GetBaseException().Message);
                return 1;
            }
            var server = new GameServer(store, new ServerOptions { AllowDevLogin = options.DevLogin });

            var listener = new HttpListener();
            listener.Prefixes.Add("http://+:" + options.Port + "/");
            listener.Start();
            Log("星线服务端 " + GameServer.Version + " 在端口 " + options.Port + " 上监听；数据库：" + storeName
                + "；开发者登录：" + (options.DevLogin ? "接受" : "不接受"));

            using var stopping = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                stopping.Cancel();
            };
            AppDomain.CurrentDomain.ProcessExit += (_, __) => stopping.Cancel();
            stopping.Token.Register(() =>
            {
                Log("正在停止");
                listener.Stop();
            });

            while (!stopping.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch (Exception) when (stopping.IsCancellationRequested)
                {
                    break;
                }
                catch (HttpListenerException e)
                {
                    Log("接收请求失败：" + e.Message);
                    continue;
                }
                _ = Task.Run(() => Handle(context, server, store, storeName, options));
            }
            return 0;
        }

        private static async Task<(IServerStore, string)> OpenStore(HostOptions options)
        {
            if (options.Memory)
            {
                return (new MemoryServerStore(), "内存（重启后数据清空，只用于试用）");
            }
            CrudSaveStore store;
            string name;
            if (!string.IsNullOrEmpty(options.Mongo))
            {
                store = CrudSaveStore.OpenLocal(options.Mongo, options.DatabaseName);
                name = "MongoDB（连接字符串），库 " + options.DatabaseName;
            }
            else
            {
                // UOS CRUD: needs UOS_APP_ID and UOS_APP_SERVICE_SECRET in the environment.
                store = await CrudSaveStore.OpenUos(options.DatabaseId, options.DatabasePassword);
                name = "UOS CRUD 的 MongoDB" + (options.DatabaseId.Length > 0 ? "（" + options.DatabaseId + "）" : "（自动选择）");
            }
            await store.EnsureIndexes();
            return (store, name);
        }

        private static async Task Handle(HttpListenerContext context, GameServer server, IServerStore store, string storeName, HostOptions options)
        {
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;
            try
            {
                string path = request.Url?.AbsolutePath.TrimEnd('/') ?? "";
                if (request.HttpMethod == "POST" && path == "/call")
                {
                    if (request.ContentLength64 > MaxBodyBytes)
                    {
                        await Write(response, 413, "text/plain", "too large");
                        return;
                    }
                    string body;
                    using (var reader = new StreamReader(request.InputStream, Encoding.UTF8))
                    {
                        body = await reader.ReadToEndAsync();
                    }
                    string answer = await server.HandleJson(body, () => PassportUserId(request));
                    await Write(response, 200, "application/json", answer);
                }
                else if (request.HttpMethod == "GET" && path == "/status")
                {
                    await Write(response, 200, "application/json", await Status(store, storeName, options));
                }
                else if (request.HttpMethod == "GET" && path == "")
                {
                    await Write(response, 200, "text/plain", "Starline server " + GameServer.Version);
                }
                else
                {
                    await Write(response, 404, "text/plain", "not found");
                }
            }
            catch (Exception e)
            {
                Log("处理 " + request.HttpMethod + " " + request.Url?.AbsolutePath + " 出错：" + e);
                try
                {
                    await Write(response, 500, "application/json", "{\"code\":\"server_error\"}");
                }
                catch (Exception)
                {
                    // The client is gone.
                }
            }
        }

        private static bool _passportWarned;

        /// <summary>
        /// Passport user id of the caller's token (header X-UOS-Token). The cloud function checks it with
        /// FuncContext.ValidateToken, which only exists inside UOS cloud functions; UOS's docs do not say
        /// how a self-hosted process should check it, so the UOS login is refused here until that is known.
        /// </summary>
        private static Task<string> PassportUserId(HttpListenerRequest request)
        {
            if (!_passportWarned)
            {
                _passportWarned = true;
                Log("UOS 账号登录在服务器程序版里还不能用：还不知道怎么在服务器上校验 Passport 令牌，先用开发者登录（server/README.md）");
            }
            return Task.FromResult<string>(null);
        }

        private static async Task<string> Status(IServerStore store, string storeName, HostOptions options)
        {
            var watch = Stopwatch.StartNew();
            bool dbOk = true;
            string dbDetail;
            try
            {
                await store.FindIdentity("status:check");
                dbDetail = storeName + "，读一次 " + watch.ElapsedMilliseconds + " ms";
            }
            catch (Exception e)
            {
                dbOk = false;
                dbDetail = e.GetBaseException().Message;
            }
            var report = new JObject
            {
                ["allOk"] = dbOk,
                ["version"] = GameServer.Version,
                ["serverTime"] = DateTime.UtcNow.ToString("o"),
                ["startedAt"] = _startedAt.ToString("o"),
                ["runtime"] = Environment.Version.ToString(),
                ["port"] = options.Port,
                ["devLogin"] = options.DevLogin,
                ["uosLogin"] = false,
                ["database"] = new JObject { ["ok"] = dbOk, ["ms"] = watch.ElapsedMilliseconds, ["detail"] = dbDetail },
            };
            return report.ToString();
        }

        private static async Task Write(HttpListenerResponse response, int status, string contentType, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            response.StatusCode = status;
            response.ContentType = contentType + "; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            response.Close();
        }

        private static void Log(string message) => Console.WriteLine(DateTime.UtcNow.ToString("u") + " " + message);
    }
}
