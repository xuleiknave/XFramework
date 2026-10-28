using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Starline.Client
{
    /// <summary>
    /// First screen after launch: sign in before the main menu (decision 49). One button per
    /// registered login method; the method used last time starts by itself. While a login runs the
    /// buttons are locked and "cancel" gives them back, in case the provider's panel was closed
    /// without telling us.
    /// </summary>
    public sealed class LoginScreen : UiScreen
    {
        private readonly Button _uos;
        private readonly Button _dev;
        private readonly Button _cancel;
        private readonly TextField _devAccount;
        private readonly Label _status;
        private readonly VisualElement _card;
        private readonly VisualElement _spinner;
        private readonly IVisualElementScheduledItem _spin;
        private readonly ILoginProvider _autoLogin;

        /// <summary>Bumped by every login and by "cancel": a failure from an older login is not shown.</summary>
        private int _attempt;
        private bool _busy;
        private bool _exited;

        public override bool UsesMenuScene => true;

        /// <param name="message">Shown instead of "choose a login method", e.g. why the game server ended the session.</param>
        public LoginScreen(GameApp app, bool autoLogin, string message = null) : base(app, "Login")
        {
            _status = Find<Label>("status");
            _card = Find<VisualElement>("card");
            _spinner = Find<VisualElement>("spinner");
            float angle = 0f;
            _spin = _spinner.schedule.Execute(() =>
            {
                angle = (angle + 8f) % 360f;
                _spinner.style.rotate = new Rotate(new Angle(angle));
            }).Every(16);
            _uos = Button("login-uos", () => SignIn(AccountService.Find(LoginMethods.Uos)));
            _dev = Button("login-dev", () => SignIn(AccountService.Find(LoginMethods.Dev)));
            _cancel = Button("cancel", Cancel);
            _devAccount = Find<TextField>("dev-account");
            _devAccount.maxLength = DevLogin.MaxAccountLength;
            _devAccount.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    SignIn(AccountService.Find(LoginMethods.Dev));
                }
            }, TrickleDown.TrickleDown);
            Button("settings", App.ShowSettings);
            Button("quit", App.Quit);
            Find<Label>("version").text = Loc.Ui("menu.version", Application.version);

            Show(_uos, AccountService.Find(LoginMethods.Uos) != null);
            var devLogin = AccountService.Find(LoginMethods.Dev) as DevLogin;
            Show(Find<VisualElement>("dev-section"), devLogin != null);
            if (devLogin != null)
            {
                _devAccount.value = devLogin.Account;
            }
            SetBusy(false);
            if (message != null)
            {
                SetStatus(message, true);
            }
            else
            {
                SetStatus(AccountService.HasAnyMethod ? Loc.Ui("login.choose") : Loc.Ui("login.none"), !AccountService.HasAnyMethod);
            }
            if (autoLogin)
            {
                _autoLogin = AccountService.Find(AccountService.LastMethod);
            }
        }

        public override void Enter()
        {
            if (_autoLogin != null)
            {
                SignIn(_autoLogin);
            }
        }

        public override void Exit() => _exited = true;

        /// <summary>After switching language on the settings screen: the same buttons, without logging in again by itself.</summary>
        public override UiScreen Rebuild() => _busy ? null : new LoginScreen(App, autoLogin: false);

        /// <summary>Name of a login method, e.g. on the main menu next to the account.</summary>
        public static string MethodName(string method)
        {
            switch (method)
            {
                case LoginMethods.Uos:
                    return Loc.Ui("login.method_uos");
                case LoginMethods.Dev:
                    return Loc.Ui("login.method_dev");
                default:
                    return method;
            }
        }

        private async void SignIn(ILoginProvider provider)
        {
            if (provider == null || _busy)
            {
                return;
            }
            if (provider is DevLogin dev)
            {
                dev.Account = _devAccount.value;
            }
            int attempt = ++_attempt;
            SetBusy(true);
            SetStatus(Loc.Ui("login.signing_in"), false);
            try
            {
                await AccountService.SignIn(provider);
                SetStatus(Loc.Ui("login.loading_saves"), false);
                await App.LoadSaves();
            }
            catch (ServerCallException e)
            {
                Fail(attempt, Loc.Ui(LoginException.ServerRefused, e.Describe()));
                return;
            }
            catch (LoginException e)
            {
                Fail(attempt, Loc.Ui(e.ReasonKey, e.Detail ?? string.Empty));
                return;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Fail(attempt, Loc.Ui(LoginException.Failed, e.Message));
                return;
            }
            // A login that finishes after "cancel" still counts: the player did sign in.
            if (!_exited)
            {
                App.ShowMainMenu();
            }
        }

        private void Fail(int attempt, string reason)
        {
            if (_exited || attempt != _attempt)
            {
                return;
            }
            SetBusy(false);
            SetStatus(reason, true);
        }

        private void Cancel()
        {
            _attempt++;
            SetBusy(false);
            SetStatus(Loc.Ui(LoginException.Cancelled), false);
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _uos.SetEnabled(!busy);
            _dev.SetEnabled(!busy);
            _devAccount.SetEnabled(!busy);
            Show(_cancel, busy);
            Show(_spinner, busy);
            _card.EnableInClassList("login-card--busy", busy);
            if (busy)
            {
                _spin.Resume();
            }
            else
            {
                _spin.Pause();
            }
        }

        private void SetStatus(string text, bool bad)
        {
            _status.text = text;
            _status.EnableInClassList("bad", bad);
        }

        private static void Show(VisualElement element, bool visible) =>
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
