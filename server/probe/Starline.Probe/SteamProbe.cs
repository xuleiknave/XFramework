using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;

namespace Starline.Probe
{
    /// <summary>
    /// Can the cloud function reach Steam's Web API? Logging in with Steam needs the server to
    /// call ISteamUserAuth/AuthenticateUserTicket. These two endpoints need no key, so they only
    /// test the network path; the publisher endpoint is the one real logins will use.
    /// </summary>
    public static class SteamProbe
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        private static readonly string[] Urls =
        {
            "https://api.steampowered.com/ISteamWebAPIUtil/GetServerInfo/v1/",
            "https://partner.steam-api.com/ISteamWebAPIUtil/GetServerInfo/v1/",
        };

        public static async Task Run(ProbeReport report)
        {
            foreach (string url in Urls)
            {
                // Two attempts: the first may include DNS and TLS setup.
                for (int attempt = 1; attempt <= 2; attempt++)
                {
                    var watch = Stopwatch.StartNew();
                    try
                    {
                        using HttpResponseMessage response = await Http.GetAsync(url);
                        string body = await response.Content.ReadAsStringAsync();
                        report.Add("访问 " + new Uri(url).Host + "（第 " + attempt + " 次）", response.IsSuccessStatusCode,
                            watch.ElapsedMilliseconds, "HTTP " + (int)response.StatusCode + "：" + Trim(body));
                    }
                    catch (Exception e)
                    {
                        report.Add("访问 " + new Uri(url).Host + "（第 " + attempt + " 次）", false,
                            watch.ElapsedMilliseconds, e.GetType().Name + "：" + Trim(e.Message));
                    }
                }
            }
        }

        private static string Trim(string s) => s.Length > 160 ? s.Substring(0, 160) + "…" : s;
    }
}
