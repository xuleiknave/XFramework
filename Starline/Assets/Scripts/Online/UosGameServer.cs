// Game server on UOS Func Stateless. Compiled only after the Func Stateless SDK is installed through
// UOS Launcher, the cloud function sources are copied in (tools/uos/sync_cloud_service.py) and
// STARLINE_UOS_FUNC is added to Scripting Define Symbols (server/README.md). Like UosPassportLogin it
// lives in Assets (Assembly-CSharp), which sees the SDK and the cloud function class StarlineService.
#if STARLINE_UOS_FUNC
using System.Threading.Tasks;
using Starline.Client;
using Starline.Cloud;
using Unity.UOS.Func.Stateless.Core.Context;
using UnityEngine;
#if STARLINE_UOS_PASSPORT
using Unity.UOS.Auth;
#endif

namespace Starline.Online
{
    /// <summary>
    /// Calls the cloud function StarlineService.Call. In the Func Stateless panel's local mode the call
    /// runs the server code inside the editor; in remote mode the SDK sends it to UOS. The caller's
    /// Passport token goes along in FuncContext (header X-UOS-Token), where the server checks it.
    /// </summary>
    public sealed class UosGameServer : IServerTransport
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register() => GameServerClient.Register(new UosGameServer());

        public async Task<string> Call(string requestJson)
        {
#if STARLINE_UOS_PASSPORT
            // Kept fresh by the Passport SDK after login; empty for the developer login.
            FuncContext.SetToken(AuthTokenManager.GetAccessToken() ?? string.Empty);
#endif
            return await new StarlineService().Call(requestJson);
        }
    }
}
#endif
