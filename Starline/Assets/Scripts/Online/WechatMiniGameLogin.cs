// WeChat mini game login. Compiled only in the mini game build: install the WeChat mini game SDK
// (Tuanjie's mini game conversion, namespace WeChatWASM) and add STARLINE_WECHAT to Scripting Define
// Symbols for the mini game platform. Like UosPassportLogin it lives in Assets (Assembly-CSharp) and
// registers itself with the game's AccountService at startup.
#if STARLINE_WECHAT
using System.Threading.Tasks;
using Starline.Client;
using UnityEngine;
using WeChatWASM;

namespace Starline.Online
{
    /// <summary>
    /// Signs in with wx.login: WeChat returns a one-time code without asking the player anything, and the
    /// game server exchanges it for the player's openid (code2Session, server/README.md). The login
    /// screen starts it by itself, so a mini game player never presses a login button.
    /// </summary>
    public sealed class WechatMiniGameLogin : ILoginProvider
    {
        public string Id => LoginMethods.Wechat;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register() => AccountService.Register(new WechatMiniGameLogin());

        public Task<AuthSession> SignInAsync()
        {
            var result = new TaskCompletionSource<AuthSession>();
            WX.Login(new LoginOption
            {
                success = res => result.TrySetResult(new AuthSession(LoginMethods.Wechat, null, null, credential: res.code)),
                fail = err => result.TrySetException(new LoginException(LoginException.Failed, err.errMsg)),
            });
            return result.Task;
        }

        public void SignOut()
        {
            // Nothing to end on WeChat's side: the next wx.login gives a new code for the same player.
        }
    }
}
#endif
