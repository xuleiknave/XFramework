using System.Collections.Generic;
using System.Threading.Tasks;
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

        public static async Task<AuthSession> SignIn(ILoginProvider provider)
        {
            AuthSession session = await provider.SignInAsync();
            Session = session;
            PlayerPrefs.SetString(LastMethodPref, provider.Id);
            PlayerPrefs.Save();
            return session;
        }

        /// <summary>Signs out; the next launch shows the login screen without logging in automatically.</summary>
        public static void SignOut()
        {
            if (Session == null)
            {
                return;
            }
            Find(Session.Method)?.SignOut();
            Session = null;
            PlayerPrefs.DeleteKey(LastMethodPref);
            PlayerPrefs.Save();
        }
    }
}
