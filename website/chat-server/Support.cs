using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Starline.Chat;

public static class Text
{
    /// <summary>去掉控制字符和零宽字符，单行文本把换行换成空格，多行文本最多保留一个空行。</summary>
    public static string Clean(string? s, bool singleLine)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.Replace("\r\n", "\n"))
        {
            if (ch == '\n') { sb.Append(singleLine ? ' ' : '\n'); continue; }
            var cat = char.GetUnicodeCategory(ch);
            if (cat is UnicodeCategory.Control or UnicodeCategory.Format) continue;
            sb.Append(ch);
        }
        var r = sb.ToString().Trim();
        r = Regex.Replace(r, @"\n{3,}", "\n\n");
        return singleLine ? Regex.Replace(r, @"\s{2,}", " ") : r;
    }
}

/// <summary>取访客真实 IP。只有请求确实来自 Cloudflare 时才信任 CF-Connecting-IP，直接访问服务器 IP 的请求无法伪造。</summary>
public static class ClientIp
{
    static readonly IPNetwork[] Cloudflare =
    [
        .. new[]
        {
            "173.245.48.0/20", "103.21.244.0/22", "103.22.200.0/22", "103.31.4.0/22", "141.101.64.0/18",
            "108.162.192.0/18", "190.93.240.0/20", "188.114.96.0/20", "197.234.240.0/22", "198.41.128.0/17",
            "162.158.0.0/15", "104.16.0.0/13", "104.24.0.0/14", "172.64.0.0/13", "131.0.72.0/22",
            "2400:cb00::/32", "2606:4700::/32", "2803:f800::/32", "2405:b500::/32", "2405:8100::/32",
            "2a06:98c0::/29", "2c0f:f248::/32",
        }.Select(IPNetwork.Parse),
    ];

    public static string Get(HttpContext ctx)
    {
        var remote = ctx.Connection.RemoteIpAddress;
        if (remote == null) return "unknown";
        if (remote.IsIPv4MappedToIPv6) remote = remote.MapToIPv4();
        var fromCf = Cloudflare.Any(n => n.Contains(remote));
        var trusted = fromCf || IPAddress.IsLoopback(remote);
        if (trusted && IPAddress.TryParse(ctx.Request.Headers["CF-Connecting-IP"].ToString(), out var real))
            return real.ToString();
        return remote.ToString();
    }
}

/// <summary>内存里的限频：同一 IP 两条留言至少隔 20 秒、每小时 6 条、每天 20 条；全站每小时 200 条。</summary>
public sealed class RateLimiter
{
    readonly ConcurrentDictionary<string, List<DateTime>> _posts = new();
    readonly ConcurrentDictionary<string, List<DateTime>> _loginFails = new();
    readonly List<DateTime> _global = [];

    public string? Check(string ip)
    {
        var now = DateTime.UtcNow;
        lock (_global)
        {
            _global.RemoveAll(t => now - t > TimeSpan.FromHours(1));
            if (_global.Count >= 200) return "现在留言的人太多了，请过一会儿再试。";
        }
        var list = _posts.GetOrAdd(ip, _ => []);
        lock (list)
        {
            list.RemoveAll(t => now - t > TimeSpan.FromDays(1));
            if (list.Count > 0 && now - list[^1] < TimeSpan.FromSeconds(20))
                return $"发得太快了，请 {20 - (int)(now - list[^1]).TotalSeconds} 秒后再试。";
            if (list.Count(t => now - t < TimeSpan.FromHours(1)) >= 6) return "一小时内最多留言 6 条，请稍后再来。";
            if (list.Count >= 20) return "今天的留言次数用完了，明天再来吧。";
        }
        return null;
    }

    public void Record(string ip)
    {
        var now = DateTime.UtcNow;
        lock (_global) _global.Add(now);
        var list = _posts.GetOrAdd(ip, _ => []);
        lock (list) list.Add(now);
    }

    public bool CanTryLogin(string ip)
    {
        var list = _loginFails.GetOrAdd(ip, _ => []);
        lock (list)
        {
            list.RemoveAll(t => DateTime.UtcNow - t > TimeSpan.FromMinutes(15));
            return list.Count < 5;
        }
    }

    public void RecordLoginFailure(string ip)
    {
        var list = _loginFails.GetOrAdd(ip, _ => []);
        lock (list) list.Add(DateTime.UtcNow);
    }
}

/// <summary>后台登录令牌和留言表单令牌，都用存在数据库里的密钥做 HMAC 签名。</summary>
public sealed class AdminAuth(ChatStore store)
{
    byte[] _secret = LoadSecret(store);

    static byte[] LoadSecret(ChatStore store)
    {
        var s = store.GetSetting("token_secret", "");
        if (s == "")
        {
            s = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            store.SetSetting("token_secret", s);
        }
        return Convert.FromBase64String(s);
    }

    public void RotateSecret()
    {
        store.SetSetting("token_secret", "");
        _secret = LoadSecret(store);
    }

    string Sign(string payload) => Convert.ToHexString(HMACSHA256.HashData(_secret, Encoding.UTF8.GetBytes(payload)));

    bool Verify(string token, string prefix, out long value)
    {
        value = 0;
        var dot = token.IndexOf('.');
        if (dot < 0) return false;
        var payload = token[..dot];
        if (!payload.StartsWith(prefix) || !long.TryParse(payload[prefix.Length..], out value)) return false;
        var expected = Encoding.ASCII.GetBytes(Sign(payload));
        return CryptographicOperations.FixedTimeEquals(expected, Encoding.ASCII.GetBytes(token[(dot + 1)..]));
    }

    static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public string IssueAdminToken()
    {
        var payload = "a" + (Now() + 12 * 3600);
        return payload + "." + Sign(payload);
    }

    public bool CheckAdminToken(string token) => Verify(token, "a", out var exp) && exp > Now();

    public string IssueFormToken()
    {
        var payload = "f" + Now();
        return payload + "." + Sign(payload);
    }

    /// <summary>表单令牌在打开页面时发放：太快（少于 3 秒）提交多半是机器人，超过 6 小时要刷新页面。</summary>
    public string? CheckFormToken(string? token)
    {
        if (string.IsNullOrEmpty(token) || !Verify(token, "f", out var issued)) return "页面已过期，请刷新后再留言。";
        var age = Now() - issued;
        if (age < 3) return "写得太快了，请稍等几秒再提交。";
        if (age > 6 * 3600) return "页面已过期，请刷新后再留言。";
        return null;
    }
}

/// <summary>敏感词和广告过滤。命中的留言不会直接拒绝，而是进入“待审核”，由开发者在后台决定。</summary>
public sealed class ContentFilter
{
    readonly string _file;
    readonly ChatStore _store;
    string[] _words = [];

    static readonly Regex Ad = new(
        @"(https?://|www\.|\.(com|cn|net|top|xyz|vip|cc|me)\b|加\s*(微|v|q|群)|(微信|vx|wx|qq|扣扣|电报|telegram)\s*[:：号]?\s*[a-z0-9_-]{5,}|1[3-9]\d{9}|\d{8,})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly string[] Reserved = ["开发者", "开发组", "官方", "管理员", "客服", "运营", "gm", "admin"];

    public ContentFilter(string file, ChatStore store)
    {
        _file = file;
        _store = store;
        Reload();
    }

    public void Reload()
    {
        var builtIn = File.Exists(_file) ? File.ReadAllLines(_file) : [];
        var extra = _store.GetSetting("extra_words", "").Split('\n');
        _words = builtIn.Concat(extra)
            .Select(w => w.Trim())
            .Where(w => w.Length > 0 && !w.StartsWith('#'))
            .Select(Normalize)
            .Where(w => w.Length > 0)
            .Distinct()
            .ToArray();
    }

    /// <summary>全角转半角、转小写、去掉空格和符号，防止用“法 轮”“f.u.c.k”之类绕过。</summary>
    public static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.Normalize(NormalizationForm.FormKC).ToLowerInvariant())
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
        return sb.ToString();
    }

    /// <summary>返回命中原因；没问题返回 null。</summary>
    public string? Check(string text)
    {
        var hit = _words.FirstOrDefault(Normalize(text).Contains);
        if (hit != null) return "敏感词：" + hit;
        if (Ad.IsMatch(text.Normalize(NormalizationForm.FormKC))) return "疑似广告或联系方式";
        return null;
    }

    public static bool IsReservedName(string name)
    {
        var n = Normalize(name);
        return Reserved.Any(n.Contains);
    }
}
