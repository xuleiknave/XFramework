namespace Starline.Client
{
    /// <summary>The signed-in account: which login method was used and who the player is.</summary>
    public sealed class AuthSession
    {
        public AuthSession(string method, string accountId, string displayName, string accessToken = null, string credential = null)
        {
            Method = method;
            AccountId = accountId;
            DisplayName = displayName;
            AccessToken = accessToken;
            Credential = credential;
        }

        /// <summary>Id of the login method (<see cref="LoginMethods"/>).</summary>
        public string Method { get; }

        /// <summary>Game server account id; without a game server, the id given by the login method (null when it does not tell).</summary>
        public string AccountId { get; }

        /// <summary>Name to show on the main menu; null when the login method has none.</summary>
        public string DisplayName { get; }

        /// <summary>Game server session token; null in builds without the game server (no Func Stateless SDK).</summary>
        public string AccessToken { get; }

        /// <summary>One-time proof the login method hands to the game server (the wx.login code); null for the others.</summary>
        public string Credential { get; }
    }
}
