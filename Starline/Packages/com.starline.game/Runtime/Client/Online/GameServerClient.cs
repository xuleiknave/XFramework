using System;
using System.Threading.Tasks;
using Starline.Online;
using UnityEngine;

namespace Starline.Client
{
    /// <summary>
    /// Sends one request to the game server and returns its answer as JSON. The UOS cloud function
    /// adapter (Assets/Scripts/Online/UosGameServer.cs) registers one when the Func Stateless SDK is
    /// installed; it fails with any exception when the server cannot be reached.
    /// </summary>
    public interface IServerTransport
    {
        Task<string> Call(string requestJson);
    }

    /// <summary>A call the server refused (<see cref="Code"/> from <see cref="ServerErrors"/>) or could not be reached.</summary>
    public sealed class ServerCallException : Exception
    {
        /// <summary><see cref="Code"/> when the server could not be reached.</summary>
        public const string Network = "network";

        public ServerCallException(string code, string detail = null) : base(detail ?? code)
        {
            Code = code;
        }

        public string Code { get; }

        /// <summary>The server's answer when it refused the call (some refusals carry data, e.g. <see cref="ServerErrors.LinkChoice"/>).</summary>
        public ServerResponse Response { get; set; }

        /// <summary>The account must sign in again (another device, expired or unknown session).</summary>
        public bool NeedsLogin => Code == ServerErrors.Unauthorized || Code == ServerErrors.SessionReplaced || Code == ServerErrors.SessionExpired;

        /// <summary>Text for the player.</summary>
        public string Describe()
        {
            switch (Code)
            {
                case Network:
                    return Loc.Ui("server.error.network");
                case ServerErrors.BadRequest:
                    return Loc.Ui("server.error.bad_request");
                case ServerErrors.Unauthorized:
                    return Loc.Ui("server.error.unauthorized");
                case ServerErrors.SessionReplaced:
                    return Loc.Ui("server.error.session_replaced");
                case ServerErrors.SessionExpired:
                    return Loc.Ui("server.error.session_expired");
                case ServerErrors.LoginFailed:
                    return Loc.Ui("server.error.login_failed");
                case ServerErrors.DevLoginDisabled:
                    return Loc.Ui("server.error.dev_login_disabled");
                case ServerErrors.InvalidName:
                    return Loc.Ui("server.error.invalid_name");
                case ServerErrors.BadSlot:
                    return Loc.Ui("server.error.bad_slot");
                case ServerErrors.SlotTaken:
                    return Loc.Ui("server.error.slot_taken");
                case ServerErrors.SlotEmpty:
                    return Loc.Ui("server.error.slot_empty");
                case ServerErrors.Conflict:
                    return Loc.Ui("server.error.conflict");
                case ServerErrors.LinkInvalid:
                    return Loc.Ui("server.error.link_invalid");
                case ServerErrors.LinkSelf:
                    return Loc.Ui("server.error.link_self");
                case ServerErrors.LinkLimited:
                    return Loc.Ui("server.error.link_limited");
                default:
                    return Loc.Ui("server.error.server_error", Code);
            }
        }
    }

    /// <summary>
    /// The game server (UOS cloud functions, docs/server-design.md). Without a registered transport the
    /// game runs offline as before: local saves, and the login only says who is playing.
    /// </summary>
    public static class GameServerClient
    {
        /// <summary>Writes are sent again this often (with the same idempotency key) when the network fails.</summary>
        private const int WriteAttempts = 3;

        public static IServerTransport Transport { get; private set; }

        public static bool IsAvailable => Transport != null;

        /// <summary>Session token from the last login; sent with every call.</summary>
        public static string Session { get; set; }

        /// <summary>Raised when a call finds the session gone (signed in elsewhere, expired); the game returns to the login screen.</summary>
        public static event Action<ServerCallException> SessionLost;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Transport = null;
            Session = null;
            SessionLost = null;
        }

        public static void Register(IServerTransport transport) => Transport = transport;

        /// <summary>
        /// Sends a request and returns the answer; throws <see cref="ServerCallException"/> when the server
        /// refuses it or cannot be reached. Writes get an idempotency key and are retried on network errors.
        /// </summary>
        public static async Task<ServerResponse> Call(ServerRequest request)
        {
            if (Transport == null)
            {
                throw new ServerCallException(ServerCallException.Network, "no game server in this build");
            }
            if (request.op != ServerOps.Login)
            {
                request.session = Session;
            }
            bool write = ServerOps.IsWrite(request.op);
            if (write && string.IsNullOrEmpty(request.key))
            {
                request.key = Guid.NewGuid().ToString("N");
            }
            string json = JsonUtility.ToJson(request);
            for (int attempt = 1; ; attempt++)
            {
                string answer;
                try
                {
                    answer = await Transport.Call(json);
                }
                catch (Exception e)
                {
                    if (write && attempt < WriteAttempts)
                    {
                        await Task.Delay(500 * attempt);
                        continue;
                    }
                    Debug.LogWarning("Starline：调用服务器失败（" + request.op + "）：" + e.Message);
                    throw new ServerCallException(ServerCallException.Network, e.Message);
                }
                ServerResponse response = string.IsNullOrEmpty(answer) ? null : JsonUtility.FromJson<ServerResponse>(answer);
                if (response == null)
                {
                    throw new ServerCallException(ServerErrors.ServerError, "empty answer to " + request.op);
                }
                if (!ServerErrors.IsOk(response.code))
                {
                    var error = new ServerCallException(response.code) { Response = response };
                    if (request.op != ServerOps.Login && error.NeedsLogin)
                    {
                        SessionLost?.Invoke(error);
                    }
                    throw error;
                }
                return response;
            }
        }
    }
}
