using System;
using System.Threading.Tasks;

namespace Starline.Client
{
    /// <summary>Ids of the login methods; the login screen has one button for each.</summary>
    public static class LoginMethods
    {
        /// <summary>UOS Passport (the account service of Tuanjie's online services).</summary>
        public const string Uos = "uos";
        /// <summary>Developer login without a password, only in the editor and development builds.</summary>
        public const string Dev = "dev";
    }

    /// <summary>
    /// One way to sign in. Providers register themselves with <see cref="AccountService.Register"/>;
    /// a provider whose SDK is not installed simply never registers, and its button stays hidden.
    /// </summary>
    public interface ILoginProvider
    {
        /// <summary>One of <see cref="LoginMethods"/>.</summary>
        string Id { get; }

        /// <summary>
        /// Signs the player in, showing the provider's own login panel when it needs one.
        /// Fails with a <see cref="LoginException"/> when the player cancels or the login is refused.
        /// </summary>
        Task<AuthSession> SignInAsync();

        void SignOut();
    }

    /// <summary>A login that failed or was cancelled; the login screen shows <see cref="ReasonKey"/>.</summary>
    public sealed class LoginException : Exception
    {
        public const string Cancelled = "login.cancelled";
        public const string Failed = "login.failed";
        public const string EmptyAccount = "login.empty_account";

        public LoginException(string reasonKey, string detail = null) : base(detail ?? reasonKey)
        {
            ReasonKey = reasonKey;
            Detail = detail;
        }

        /// <summary>UI.csv key of the reason; its {0} is <see cref="Detail"/>.</summary>
        public string ReasonKey { get; }

        public string Detail { get; }
    }
}
