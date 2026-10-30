using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Starline.Server.Game
{
    /// <summary>
    /// WeChat mini game login: the client calls wx.login and sends the one-time code; the server asks
    /// WeChat's code2Session API for the player's openid (WeChat docs "小游戏登录"). Needs the mini game's
    /// AppID and AppSecret, which come from environment variables and never go into the repository.
    /// </summary>
    public sealed class WechatLogin
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        private readonly string _appId;
        private readonly string _secret;
        private readonly Action<string> _log;

        public WechatLogin(string appId, string secret, Action<string> log = null)
        {
            _appId = appId;
            _secret = secret;
            _log = log;
        }

        /// <summary>From STARLINE_WX_APPID and STARLINE_WX_SECRET; null when either is missing.</summary>
        public static WechatLogin FromEnvironment(Action<string> log = null)
        {
            string appId = Environment.GetEnvironmentVariable("STARLINE_WX_APPID");
            string secret = Environment.GetEnvironmentVariable("STARLINE_WX_SECRET");
            return string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(secret) ? null : new WechatLogin(appId, secret, log);
        }

        /// <summary>The openid of the player who got this wx.login code, or null when WeChat refuses it.</summary>
        public async Task<string> OpenId(string code)
        {
            string url = "https://api.weixin.qq.com/sns/jscode2session?appid=" + Uri.EscapeDataString(_appId)
                + "&secret=" + Uri.EscapeDataString(_secret)
                + "&js_code=" + Uri.EscapeDataString(code)
                + "&grant_type=authorization_code";
            JObject answer;
            try
            {
                answer = JObject.Parse(await Http.GetStringAsync(url));
            }
            catch (Exception e)
            {
                _log?.Invoke("微信 code2Session 调用失败：" + e.GetBaseException().Message);
                return null;
            }
            string openId = (string)answer["openid"];
            if (string.IsNullOrEmpty(openId))
            {
                // errcode 40029: wrong or used code; 40163: code used twice; 45011: too many calls.
                _log?.Invoke("微信 code2Session 拒绝：" + (string)answer["errcode"] + " " + (string)answer["errmsg"]);
                return null;
            }
            return openId;
        }
    }
}
