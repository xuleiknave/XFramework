using System;

namespace Starline.Server.Game
{
    /// <summary>A request the server refuses; <see cref="Code"/> is one of Starline.Online.ServerErrors.</summary>
    public sealed class ServerException : Exception
    {
        public ServerException(string code, string detail = null) : base(detail ?? code)
        {
            Code = code;
        }

        public string Code { get; }
    }
}
