using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Starline.Client
{
    /// <summary>
    /// The game server as a plain HTTP service (the UOS server program image, server/src/Starline.Server.Host):
    /// each call is POST &lt;url&gt;/call with the request JSON. Used when StarlineData/server.json (or the
    /// -server launch argument) gives an address; it then takes precedence over the cloud function.
    /// </summary>
    public sealed class HttpGameServer : IServerTransport
    {
        private const int TimeoutSeconds = 15;

        private readonly string _callUrl;

        public HttpGameServer(string baseUrl)
        {
            _callUrl = baseUrl.TrimEnd('/') + "/call";
        }

        /// <summary>Runs after the UOS cloud function adapter registers itself, so a configured address wins.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Register()
        {
            string url = ConfiguredUrl();
            if (!string.IsNullOrEmpty(url))
            {
                GameServerClient.Register(new HttpGameServer(url));
                Debug.Log("Starline：游戏服务器 " + url);
            }
        }

        /// <summary>The -server launch argument, else "url" in StarlineData/server.json; empty when neither is set.</summary>
        public static string ConfiguredUrl()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] == "-server")
                {
                    return args[i + 1].Trim();
                }
            }
            TextAsset asset = Resources.Load<TextAsset>(ClientData.ResourceFolder + "/server");
            return asset == null ? string.Empty : (JsonUtility.FromJson<ServerConfig>(asset.text)?.url ?? string.Empty).Trim();
        }

        public Task<string> Call(string requestJson)
        {
            var request = new UnityWebRequest(_callUrl, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(requestJson)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = TimeoutSeconds,
            };
            request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
            var result = new TaskCompletionSource<string>();
            request.SendWebRequest().completed += _ =>
            {
                // The server answers 200 with a ServerResponse, also for refused requests; anything else is a network failure.
                if (request.result == UnityWebRequest.Result.Success)
                {
                    result.TrySetResult(request.downloadHandler.text);
                }
                else
                {
                    result.TrySetException(new Exception(request.responseCode + " " + request.error));
                }
                request.Dispose();
            };
            return result.Task;
        }

        [Serializable]
        public sealed class ServerConfig
        {
            public string url;
        }
    }
}
