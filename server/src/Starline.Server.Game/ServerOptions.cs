using System;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Starline.Server.Game
{
    public sealed class ServerOptions
    {
        /// <summary>
        /// Accept the developer login (any account name, no password). Only for the development UOS
        /// app: the cloud function turns it on with the environment variable STARLINE_DEV_LOGIN=1.
        /// </summary>
        public bool AllowDevLogin;

        /// <summary>
        /// Exchanges a wx.login code for the player's openid (null when WeChat refuses it). Null when the
        /// server has no WeChat mini game configured; WeChat logins then fail. See <see cref="WechatLogin"/>.
        /// </summary>
        public Func<string, Task<string>> WechatOpenId;

        /// <summary>A session unused for this long must sign in again (the Passport SDK does that without asking the player).</summary>
        public long SessionIdleMs = 7L * 24 * 3600 * 1000;

        /// <summary>Server time in Unix milliseconds (UTC); replaced in tests.</summary>
        public Func<long> Clock = () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        /// <summary>Random hex string of the given byte length; replaced in tests.</summary>
        public Func<int, string> RandomHex = bytes =>
        {
            byte[] buffer = new byte[bytes];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(buffer);
            }
            return BitConverter.ToString(buffer).Replace("-", "").ToLowerInvariant();
        };
    }
}
