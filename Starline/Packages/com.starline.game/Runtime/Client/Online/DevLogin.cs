using System.Threading.Tasks;
using UnityEngine;

namespace Starline.Client
{
    /// <summary>
    /// Developer login for the editor and development builds: any account name, no password and no
    /// server, so the game can be played before the UOS app is set up. Becomes the server's
    /// "dev" login once the login endpoints exist (stage S1 in docs/server-design.md).
    /// </summary>
    public sealed class DevLogin : ILoginProvider
    {
        public const int MaxAccountLength = 16;
        private const string AccountPref = "StarlineDevAccount";

        public string Id => LoginMethods.Dev;

        /// <summary>Account name typed on the login screen, remembered for the next launch.</summary>
        public string Account
        {
            get => PlayerPrefs.GetString(AccountPref, LoginMethods.Dev);
            set => PlayerPrefs.SetString(AccountPref, (value ?? string.Empty).Trim());
        }

        public Task<AuthSession> SignInAsync()
        {
            string account = Account;
            if (account.Length == 0)
            {
                return Task.FromException<AuthSession>(new LoginException(LoginException.EmptyAccount));
            }
            PlayerPrefs.Save();
            return Task.FromResult(new AuthSession(LoginMethods.Dev, account, account));
        }

        public void SignOut()
        {
        }
    }
}
