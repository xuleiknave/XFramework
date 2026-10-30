using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Starline.Server.Host
{
    /// <summary>
    /// Tells UOS Multiverse that the server is up (Ready) and keeps it marked healthy (Health). Without
    /// Ready the startconfig test fails with "Game server is not ready ... sdk.ready()".
    ///
    /// Multiverse runs an Agones SDK sidecar next to the container: UOS's own C# SDK (NuGet
    /// UOS.Multiverse.SDK) reads AGONES_SDK_GRPC_HOST / AGONES_SDK_GRPC_PORT. That SDK pulls in Grpc.Core
    /// and its native library, so this talks to the sidecar directly instead: first the Agones REST
    /// gateway (POST /ready, POST /health on AGONES_SDK_HTTP_PORT, default 9358), and if that does not
    /// answer, the same calls over plain gRPC (HTTP/2 without TLS on AGONES_SDK_GRPC_PORT, default 9357).
    /// Off when no AGONES_SDK_* variable is set, so the process still runs on its own.
    /// </summary>
    public sealed class MultiverseSdk
    {
        private static readonly TimeSpan HealthEvery = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds(1);
        private const int ReadyAttempts = 30;

        private readonly Action<string> _log;
        private readonly string _host;
        private readonly int _httpPort;
        private readonly int _grpcPort;
        private readonly HttpClient _http;
        private bool _useGrpc;

        public static bool Present =>
            Env("AGONES_SDK_HTTP_PORT") != null || Env("AGONES_SDK_GRPC_PORT") != null;

        public MultiverseSdk(Action<string> log)
        {
            // gRPC without TLS needs HTTP/2 "prior knowledge", which .NET only allows behind this switch.
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
            _log = log;
            _host = Env("AGONES_SDK_GRPC_HOST") ?? "localhost";
            _httpPort = int.TryParse(Env("AGONES_SDK_HTTP_PORT"), out int h) ? h : 9358;
            _grpcPort = int.TryParse(Env("AGONES_SDK_GRPC_PORT"), out int g) ? g : 9357;
            _http = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { Timeout = Timeout.InfiniteTimeSpan };
        }

        public string Description => (_useGrpc ? "gRPC " + _host + ":" + _grpcPort : "HTTP " + _host + ":" + _httpPort);

        /// <summary>Marks the server Ready (retrying while the sidecar starts), then sends Health until stopped.</summary>
        public async Task<bool> Run(CancellationToken stopping)
        {
            if (!await Ready(stopping))
            {
                return false;
            }
            _ = Task.Run(() => HealthLoop(stopping));
            return true;
        }

        private async Task<bool> Ready(CancellationToken stopping)
        {
            string lastError = "";
            for (int attempt = 1; attempt <= ReadyAttempts && !stopping.IsCancellationRequested; attempt++)
            {
                try
                {
                    await RestCall("/ready", stopping);
                    _useGrpc = false;
                    _log("Multiverse：已报告就绪（" + Description + "）");
                    return true;
                }
                catch (Exception e) when (!stopping.IsCancellationRequested)
                {
                    lastError = "HTTP " + _httpPort + "：" + e.GetBaseException().Message;
                }
                try
                {
                    await GrpcUnary("Ready", stopping);
                    _useGrpc = true;
                    _log("Multiverse：已报告就绪（" + Description + "）");
                    return true;
                }
                catch (Exception e) when (!stopping.IsCancellationRequested)
                {
                    lastError += "；gRPC " + _grpcPort + "：" + e.GetBaseException().Message;
                }
                if (attempt == 1)
                {
                    _log("Multiverse：边车还没响应，继续重试（" + lastError + "）");
                }
                try
                {
                    await Task.Delay(RetryEvery, stopping);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            _log("Multiverse：报告就绪失败（" + lastError + "）");
            return false;
        }

        private async Task HealthLoop(CancellationToken stopping)
        {
            bool failing = false;
            while (!stopping.IsCancellationRequested)
            {
                try
                {
                    if (_useGrpc)
                    {
                        // Health is a client stream: one long request that sends an empty message every few seconds.
                        await GrpcHealthStream(stopping);
                    }
                    else
                    {
                        await RestCall("/health", stopping);
                        await Task.Delay(HealthEvery, stopping);
                    }
                    if (failing)
                    {
                        failing = false;
                        _log("Multiverse：健康上报恢复");
                    }
                }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception e)
                {
                    if (!failing)
                    {
                        failing = true;
                        _log("Multiverse：健康上报失败，稍后重试（" + e.GetBaseException().Message + "）");
                    }
                    try
                    {
                        await Task.Delay(RetryEvery, stopping);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
        }

        private async Task RestCall(string path, CancellationToken stopping)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            var content = new StringContent("{}", Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await _http.PostAsync("http://" + _host + ":" + _httpPort + path, content, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException("状态码 " + (int)response.StatusCode);
            }
        }

        private async Task GrpcUnary(string method, CancellationToken stopping)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            // An empty agones.dev.sdk.Empty message: no compression flag, length 0.
            var content = new ByteArrayContent(new byte[5]);
            using HttpResponseMessage response = await _http.SendAsync(GrpcRequest(method, content), timeout.Token);
            await response.Content.ReadAsByteArrayAsync(timeout.Token);
            CheckGrpc(response);
        }

        private async Task GrpcHealthStream(CancellationToken stopping)
        {
            using HttpResponseMessage response = await _http.SendAsync(GrpcRequest("Health", new HealthPings(stopping)), stopping);
            await response.Content.ReadAsByteArrayAsync(stopping);
            CheckGrpc(response);
        }

        private HttpRequestMessage GrpcRequest(string method, HttpContent content)
        {
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/grpc");
            var request = new HttpRequestMessage(HttpMethod.Post, "http://" + _host + ":" + _grpcPort + "/agones.dev.sdk.SDK/" + method)
            {
                Version = HttpVersion.Version20,
                VersionPolicy = HttpVersionPolicy.RequestVersionExact,
                Content = content,
            };
            request.Headers.TryAddWithoutValidation("TE", "trailers");
            return request;
        }

        private static void CheckGrpc(HttpResponseMessage response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException("状态码 " + (int)response.StatusCode);
            }
            string status = Header(response, "grpc-status");
            if (status != null && status != "0")
            {
                throw new HttpRequestException("grpc-status " + status + " " + Header(response, "grpc-message"));
            }
        }

        private static string Header(HttpResponseMessage response, string name)
        {
            if (response.TrailingHeaders.TryGetValues(name, out var values) || response.Headers.TryGetValues(name, out values))
            {
                foreach (string v in values)
                {
                    return v;
                }
            }
            return null;
        }

        /// <summary>Request body of the Health stream: an empty message every few seconds until stopped.</summary>
        private sealed class HealthPings : HttpContent
        {
            private readonly CancellationToken _stopping;

            public HealthPings(CancellationToken stopping) => _stopping = stopping;

            protected override async Task SerializeToStreamAsync(Stream stream, TransportContext context)
            {
                var empty = new byte[5];
                while (!_stopping.IsCancellationRequested)
                {
                    await stream.WriteAsync(empty, 0, empty.Length, _stopping);
                    await stream.FlushAsync(_stopping);
                    await Task.Delay(HealthEvery, _stopping);
                }
            }

            protected override bool TryComputeLength(out long length)
            {
                length = 0;
                return false;
            }
        }

        private static string Env(string name)
        {
            string value = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrEmpty(value) ? null : value;
        }
    }
}
