using System.Collections.Generic;
using System.Threading.Tasks;
using Starline.Online;
using UnityEngine;

namespace Starline.Client
{
    /// <summary>
    /// The login methods available in this build and the signed-in account. The game cannot be
    /// played without signing in (decision 49); the login screen is the first screen.
    /// </summary>
    public static class AccountService
    {
        /// <summary>PlayerPrefs key of the last login method, tried again automatically at the next launch.</summary>
        private const string LastMethodPref = "StarlineLastLogin";

        private static readonly List<ILoginProvider> Registered = new List<ILoginProvider>();

        public static AuthSession Session { get; private set; }

        public static bool IsSignedIn => Session != null;

        /// <summary>Login method of the last successful login on this computer; empty after signing out.</summary>
        public static string LastMethod => PlayerPrefs.GetString(LastMethodPref, string.Empty);

        /// <summary>Runs before any provider registers (also when entering play mode without a domain reload).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Registered.Clear();
            Session = null;
            if (Application.isEditor || Debug.isDebugBuild)
            {
                Register(new DevLogin());
            }
        }

        /// <summary>Adds a login method, replacing one with the same id.</summary>
        public static void Register(ILoginProvider provider)
        {
            Registered.RemoveAll(p => p.Id == provider.Id);
            Registered.Add(provider);
        }

        /// <summary>The registered provider with this id, or null.</summary>
        public static ILoginProvider Find(string id) => Registered.Find(p => p.Id == id);

        public static bool HasAnyMethod => Registered.Count > 0;

        /// <summary>
        /// Signs in with the provider, then with the game server when this build has one: the server
        /// checks the Passport token (or takes the developer account name), ends the account's other
        /// sessions and returns the game's own session token (docs/server-design.md section 7).
        /// </summary>
        public static async Task<AuthSession> SignIn(ILoginProvider provider)
        {
            AuthSession session = await provider.SignInAsync();
            if (GameServerClient.IsAvailable)
            {
                session = await SignInToServer(session);
            }
            Session = session;
            PlayerPrefs.SetString(LastMethodPref, provider.Id);
            PlayerPrefs.Save();
            return session;
        }

        private static async Task<AuthSession> SignInToServer(AuthSession provided)
        {
            var request = new ServerRequest
            {
                op = ServerOps.Login,
                method = provided.Method == LoginMethods.Dev ? ServerOps.LoginMethodDev : ServerOps.LoginMethodUos,
                devAccount = provided.Method == LoginMethods.Dev ? provided.AccountId : null,
            };
            ServerResponse response;
            try
            {
                response = await GameServerClient.Call(request);
            }
            catch (ServerCallException e)
            {
                // The provider stays signed in: pressing the button again only retries the server.
                throw new LoginException(LoginException.ServerRefused, e.Describe());
            }
            GameServerClient.Session = response.session;
            string name = string.IsNullOrEmpty(response.displayName) ? provided.DisplayName : response.displayName;
            return new AuthSession(provided.Method, response.accountId, name, response.session);
        }

        /// <summary>Signs out; the next launch shows the login screen without logging in automatically.</summary>
        public static void SignOut()
        {
            if (Session == null)
            {
                return;
            }
            if (GameServerClient.IsAvailable && !string.IsNullOrEmpty(GameServerClient.Session))
            {
                // Best effort: the session also ends by itself at the next login or after a week unused.
                _ = GameServerClient.Call(new ServerRequest { op = ServerOps.Logout }).ContinueWith(t => _ = t.Exception);
            }
            Find(Session.Method)?.SignOut();
            Forget();
            PlayerPrefs.DeleteKey(LastMethodPref);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// The game server ended the session (signed in on another device, expired). The login method
        /// stays signed in and remembered, so logging in again is one click.
        /// </summary>
        public static void SessionLost() => Forget();

        private static void Forget()
        {
            GameServerClient.Session = null;
            Session = null;
        }
    }
}
