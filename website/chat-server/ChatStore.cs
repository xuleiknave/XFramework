using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace Starline.Chat;

public enum MessageStatus { Pending = 0, Public = 1, Hidden = 2 }

public record PublicMessage(long Id, string Name, string Content, long CreatedAt, bool IsDev, string? Reply, long? ReplyAt);

public record AdminMessage(long Id, string Name, string Content, long CreatedAt, bool IsDev, string? Reply, long? ReplyAt,
    string Status, string Ip, string? Flag, bool Banned);

/// <summary>SQLite 存储。每次调用开一个连接（连接池复用），WAL 模式下读写可以并发。</summary>
public sealed class ChatStore
{
    readonly string _cs;

    public ChatStore(string path)
    {
        _cs = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Shared }.ToString();
    }

    SqliteConnection Open()
    {
        var c = new SqliteConnection(_cs);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA busy_timeout = 5000;";
        cmd.ExecuteNonQuery();
        return c;
    }

    static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    int Exec(string sql, params (string, object?)[] args)
    {
        using var c = Open();
        using var cmd = Cmd(c, sql, args);
        return cmd.ExecuteNonQuery();
    }

    static SqliteCommand Cmd(SqliteConnection c, string sql, (string, object?)[] args)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
        return cmd;
    }

    object? Scalar(string sql, params (string, object?)[] args)
    {
        using var c = Open();
        using var cmd = Cmd(c, sql, args);
        return cmd.ExecuteScalar();
    }

    public void Init()
    {
        Exec("""
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS messages (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                content TEXT NOT NULL,
                ip TEXT NOT NULL,
                created_at INTEGER NOT NULL,
                status INTEGER NOT NULL,
                flag TEXT,
                is_dev INTEGER NOT NULL DEFAULT 0,
                reply TEXT,
                reply_at INTEGER
            );
            CREATE INDEX IF NOT EXISTS ix_messages_status ON messages(status, id);
            CREATE INDEX IF NOT EXISTS ix_messages_ip ON messages(ip, created_at);
            CREATE TABLE IF NOT EXISTS settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS bans (ip TEXT PRIMARY KEY, reason TEXT, created_at INTEGER NOT NULL);
            """);
    }

    // ---------- 设置 ----------

    public string GetSetting(string key, string fallback) =>
        Scalar("SELECT value FROM settings WHERE key = $k", ("$k", key)) as string ?? fallback;

    public void SetSetting(string key, string value) =>
        Exec("INSERT INTO settings(key, value) VALUES($k, $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value", ("$k", key), ("$v", value));

    // ---------- 管理员密码（PBKDF2） ----------

    /// <summary>第一次启动时生成随机密码写到 <paramref name="file"/>，返回该密码；已有密码时返回 null。</summary>
    public string? EnsureAdminPassword(string file)
    {
        if (GetSetting("admin_hash", "") != "") return null;
        var pwd = Environment.GetEnvironmentVariable("STARLINE_CHAT_ADMIN_PASSWORD");
        if (string.IsNullOrEmpty(pwd))
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
            pwd = new string(Enumerable.Range(0, 16).Select(_ => chars[RandomNumberGenerator.GetInt32(chars.Length)]).ToArray());
        }
        SetAdminPassword(pwd);
        File.WriteAllText(file, pwd);
        return pwd;
    }

    public void SetAdminPassword(string pwd)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(pwd, salt, 200_000, HashAlgorithmName.SHA256, 32);
        SetSetting("admin_hash", Convert.ToBase64String(salt) + ":" + Convert.ToBase64String(hash));
    }

    public bool VerifyAdminPassword(string pwd)
    {
        var parts = GetSetting("admin_hash", "").Split(':');
        if (parts.Length != 2) return false;
        var salt = Convert.FromBase64String(parts[0]);
        var hash = Rfc2898DeriveBytes.Pbkdf2(pwd, salt, 200_000, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(hash, Convert.FromBase64String(parts[1]));
    }

    // ---------- 留言 ----------

    public long Insert(string name, string content, string ip, MessageStatus status, string? flag, bool isDev = false) =>
        (long)Scalar("""
            INSERT INTO messages(name, content, ip, created_at, status, flag, is_dev)
            VALUES($n, $c, $ip, $t, $s, $f, $d) RETURNING id
            """, ("$n", name), ("$c", content), ("$ip", ip), ("$t", Now()), ("$s", (int)status), ("$f", flag), ("$d", isDev ? 1 : 0))!;

    public bool IsDuplicate(string ip, string content) =>
        Scalar("SELECT 1 FROM messages WHERE ip = $ip AND content = $c AND created_at > $t LIMIT 1",
            ("$ip", ip), ("$c", content), ("$t", Now() - 86_400_000)) != null;

    public List<PublicMessage> ListPublic(long? before, int limit)
    {
        using var c = Open();
        using var cmd = Cmd(c, """
            SELECT id, name, content, created_at, is_dev, reply, reply_at FROM messages
            WHERE status = 1 AND id < $b ORDER BY id DESC LIMIT $l
            """, [("$b", before ?? long.MaxValue), ("$l", limit)]);
        using var r = cmd.ExecuteReader();
        var list = new List<PublicMessage>();
        while (r.Read())
            list.Add(new PublicMessage(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetInt64(3), r.GetInt64(4) == 1,
                r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : r.GetInt64(6)));
        return list;
    }

    public List<AdminMessage> ListAdmin(string? status, long? before, string? q, int limit)
    {
        var where = status switch { "pending" => "m.status = 0", "public" => "m.status = 1", "hidden" => "m.status = 2", _ => "1 = 1" };
        if (!string.IsNullOrWhiteSpace(q)) where += " AND (m.content LIKE $q OR m.name LIKE $q OR m.ip = $qip)";
        using var c = Open();
        using var cmd = Cmd(c, $"""
            SELECT m.id, m.name, m.content, m.created_at, m.is_dev, m.reply, m.reply_at, m.status, m.ip, m.flag,
                   EXISTS(SELECT 1 FROM bans b WHERE b.ip = m.ip)
            FROM messages m WHERE {where} AND m.id < $b ORDER BY m.id DESC LIMIT $l
            """, [("$b", before ?? long.MaxValue), ("$l", limit), ("$q", $"%{q}%"), ("$qip", q?.Trim())]);
        using var r = cmd.ExecuteReader();
        var list = new List<AdminMessage>();
        while (r.Read())
            list.Add(new AdminMessage(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetInt64(3), r.GetInt64(4) == 1,
                r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : r.GetInt64(6),
                ((MessageStatus)r.GetInt32(7)).ToString().ToLowerInvariant(), r.GetString(8),
                r.IsDBNull(9) ? null : r.GetString(9), r.GetInt64(10) == 1));
        return list;
    }

    public Dictionary<string, long> Counts()
    {
        using var c = Open();
        using var cmd = Cmd(c, "SELECT status, COUNT(*) FROM messages GROUP BY status", []);
        using var r = cmd.ExecuteReader();
        var d = new Dictionary<string, long> { ["pending"] = 0, ["public"] = 0, ["hidden"] = 0 };
        while (r.Read()) d[((MessageStatus)r.GetInt32(0)).ToString().ToLowerInvariant()] = r.GetInt64(1);
        return d;
    }

    public bool SetStatus(long id, MessageStatus s) => Exec("UPDATE messages SET status = $s WHERE id = $id", ("$s", (int)s), ("$id", id)) > 0;

    public bool SetReply(long id, string? reply) =>
        Exec("UPDATE messages SET reply = $r, reply_at = $t WHERE id = $id", ("$r", reply), ("$t", reply == null ? null : Now()), ("$id", id)) > 0;

    public bool Delete(long id) => Exec("DELETE FROM messages WHERE id = $id", ("$id", id)) > 0;

    public string? GetIp(long id) => Scalar("SELECT ip FROM messages WHERE id = $id", ("$id", id)) as string;

    public void HideAllFrom(string ip) => Exec("UPDATE messages SET status = 2 WHERE ip = $ip", ("$ip", ip));

    // ---------- 封禁 ----------

    public bool IsBanned(string ip) => Scalar("SELECT 1 FROM bans WHERE ip = $ip", ("$ip", ip)) != null;

    public void Ban(string ip, string reason) =>
        Exec("INSERT OR REPLACE INTO bans(ip, reason, created_at) VALUES($ip, $r, $t)", ("$ip", ip), ("$r", reason), ("$t", Now()));

    public void Unban(string ip) => Exec("DELETE FROM bans WHERE ip = $ip", ("$ip", ip));

    public List<object> ListBans()
    {
        using var c = Open();
        using var cmd = Cmd(c, "SELECT ip, reason, created_at FROM bans ORDER BY created_at DESC", []);
        using var r = cmd.ExecuteReader();
        var list = new List<object>();
        while (r.Read()) list.Add(new { ip = r.GetString(0), reason = r.IsDBNull(1) ? "" : r.GetString(1), createdAt = r.GetInt64(2) });
        return list;
    }
}
