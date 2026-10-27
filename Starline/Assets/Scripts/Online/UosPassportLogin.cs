// UOS Passport login. Compiled only after the Passport Login SDK is installed through UOS Launcher
// and STARLINE_UOS_PASSPORT is added to Player Settings > Scripting Define Symbols (README, "UOS 登录").
// It lives in Assets (Assembly-CSharp), which sees the SDK's assemblies without naming them, and
// registers itself with the game's AccountService at startup.
#if STARLINE_UOS_PASSPORT
using System.Threading.Tasks;
using Passport;
using Starline.Client;
using Unity.Passport.Runtime;
using Unity.Passport.Runtime.UI;
using UnityEngine;

namespace Starline.Online
{
    /// <summary>
    /// Signs in with UOS Passport through its own login panel (PassportUI): the login channels
    /// (phone, WeChat, ...) are chosen in the UOS console. The panel opens only when the player
    /// presses "UOS 账号登录" (InvokeLoginManually).
    /// </summary>
    public sealed class UosPassportLogin : ILoginProvider
    {
        private bool _initialized;
        private TaskCompletionSource<AuthSession> _pending;

        public string Id => LoginMethods.Uos;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register() => AccountService.Register(new UosPassportLogin());

        public async Task<AuthSession> SignInAsync()
        {
            // Pressed again after the panel was closed without an answer: keep waiting for the same login.
            _pending ??= new TaskCompletionSource<AuthSession>();
            Task<AuthSession> result = _pending.Task;
            try
            {
                if (!_initialized)
                {
                    var config = new PassportUIConfig
                    {
                        InvokeLoginManually = true,
                        Theme = PassportUITheme.Dark,
                    };
                    await PassportUI.Init(config, OnPassportEvent);
                    _initialized = true;
                }
                PassportUI.Login();
            }
            catch (System.Exception e)
            {
                _pending = null;
                throw new LoginException(LoginException.Failed, e.Message);
            }
            return await result;
        }

        public void SignOut()
        {
            if (_initialized)
            {
                PassportUI.Logout();
            }
        }

        private void OnPassportEvent(PassportEvent passportEvent)
        {
            switch (passportEvent)
            {
                case PassportEvent.RejectedTos:
                    Finish(null, new LoginException(LoginException.Cancelled));
                    break;
                case PassportEvent.Completed:
                    // The game server will check the Passport token and issue its own (docs/server-design.md,
                    // section 7); until then the session only records that the player signed in with UOS.
                    Finish(new AuthSession(LoginMethods.Uos, null, null), null);
                    break;
            }
        }

        private void Finish(AuthSession session, LoginException error)
        {
            TaskCompletionSource<AuthSession> pending = _pending;
            _pending = null;
            if (pending == null)
            {
                return;
            }
            if (error != null)
            {
                pending.TrySetException(error);
            }
            else
            {
                pending.TrySetResult(session);
            }
        }
    }
}
#endif
