namespace Starline.Client
{
    /// <summary>The signed-in account: which login method was used and who the player is.</summary>
    public sealed class AuthSession
    {
        public AuthSession(string method, string accountId, string displayName, string accessToken = null)
        {
            Method = method;
            AccountId = accountId;
            DisplayName = displayName;
            AccessToken = accessToken;
        }

        /// <summary>Id of the login method (<see cref="LoginMethods"/>).</summary>
        public string Method { get; }

        /// <summary>Account id given by the login method; null when it does not tell (the game server assigns the player id).</summary>
        public string AccountId { get; }

        /// <summary>Name to show on the main menu; null when the login method has none.</summary>
        public string DisplayName { get; }

        /// <summary>Token for the game server; null until the server's login endpoints exist (stage S1 in docs/server-design.md).</summary>
        public string AccessToken { get; }
    }
}
