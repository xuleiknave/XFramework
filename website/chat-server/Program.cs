// 星线官网留言板后端：访客留言、开发者回复、防刷、敏感词过滤、后台管理。
// 部署在 IIS 的 /api 应用下（ASP.NET Core Module 进程内托管），数据存 SQLite。
using System.Text.Json;
using Starline.Chat;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

var dataDir = builder.Configuration["DataDir"]
    ?? Environment.GetEnvironmentVariable("STARLINE_CHAT_DATA")
    ?? Path.Combine(AppContext.BaseDirectory, "data");
Directory.CreateDirectory(dataDir);

var store = new ChatStore(Path.Combine(dataDir, "chat.db"));
store.Init();
var adminPassword = store.EnsureAdminPassword(Path.Combine(dataDir, "admin-password.txt"));
var filter = new ContentFilter(Path.Combine(AppContext.BaseDirectory, "sensitive-words.txt"), store);
var limiter = new RateLimiter();
var auth = new AdminAuth(store);

builder.Services.AddSingleton(store);
var app = builder.Build();
// 本地调试时没有 IIS 的应用路径，手动去掉 /api 前缀；在 IIS 下这一行不起作用
app.UsePathBase("/api");

app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["Cache-Control"] = "no-store";
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    await next();
});

// ---------- 访客 ----------

app.MapGet("/messages", (HttpContext ctx, long? before, int? limit) =>
{
    var items = store.ListPublic(before, Math.Clamp(limit ?? 30, 1, 50));
    return Results.Ok(new
    {
        open = store.GetSetting("open", "1") == "1",
        notice = store.GetSetting("notice", ""),
        formToken = auth.IssueFormToken(),
        items,
    });
});

app.MapPost("/messages", (HttpContext ctx, PostMessage req) =>
{
    var ip = ClientIp.Get(ctx);
    if (store.GetSetting("open", "1") != "1") return Fail("留言板暂时关闭，请稍后再来。");
    // 蜜罐字段：页面上隐藏，正常用户不会填
    if (!string.IsNullOrEmpty(req.Website)) return Results.Ok(new { status = "pending" });
    var tokenErr = auth.CheckFormToken(req.T);
    if (tokenErr != null) return Fail(tokenErr);
    if (store.IsBanned(ip)) return Fail("你的留言权限已被暂停。", 403);

    var name = Text.Clean(req.Name, singleLine: true);
    var content = Text.Clean(req.Content, singleLine: false);
    if (name.Length is < 1 or > 20) return Fail("昵称请填写 1 到 20 个字。");
    if (content.Length is < 2 or > 500) return Fail("留言请写 2 到 500 个字。");
    if (ContentFilter.IsReservedName(name)) return Fail("这个昵称不能用，换一个吧。");

    var wait = limiter.Check(ip);
    if (wait != null) return Fail(wait, 429);
    if (store.IsDuplicate(ip, content)) return Fail("这条留言已经发过了。");

    var flag = filter.Check(name + "\n" + content);
    var mode = store.GetSetting("moderation", "flagged");
    var status = flag == null && mode != "all" ? MessageStatus.Public : MessageStatus.Pending;
    var id = store.Insert(name, content, ip, status, flag);
    limiter.Record(ip);
    return Results.Ok(new { id, status = status == MessageStatus.Public ? "public" : "pending" });
});

// ---------- 后台 ----------

app.MapPost("/admin/login", (HttpContext ctx, LoginRequest req) =>
{
    var ip = ClientIp.Get(ctx);
    if (!limiter.CanTryLogin(ip)) return Fail("尝试次数过多，请 15 分钟后再试。", 429);
    if (!store.VerifyAdminPassword(req.Password ?? ""))
    {
        limiter.RecordLoginFailure(ip);
        return Fail("密码不对。", 401);
    }
    return Results.Ok(new { token = auth.IssueAdminToken() });
});

var admin = app.MapGroup("/admin").AddEndpointFilter(async (ctx, next) =>
{
    var h = ctx.HttpContext.Request.Headers.Authorization.ToString();
    var token = h.StartsWith("Bearer ") ? h[7..] : "";
    return auth.CheckAdminToken(token) ? await next(ctx) : Fail("请先登录。", 401);
});

admin.MapGet("/messages", (string? status, long? before, string? q) =>
    Results.Ok(new { items = store.ListAdmin(status, before, q, 50), counts = store.Counts() }));

admin.MapPost("/messages/{id:long}/status", (long id, StatusRequest req) =>
    store.SetStatus(id, req.Status switch
    {
        "public" => MessageStatus.Public,
        "hidden" => MessageStatus.Hidden,
        "pending" => MessageStatus.Pending,
        _ => throw new BadHttpRequestException("status"),
    }) ? Results.Ok() : Results.NotFound());

admin.MapPost("/messages/{id:long}/reply", (long id, ReplyRequest req) =>
{
    var reply = Text.Clean(req.Reply, singleLine: false);
    if (reply.Length > 1000) return Fail("回复最多 1000 字。");
    return store.SetReply(id, reply.Length == 0 ? null : reply) ? Results.Ok() : Results.NotFound();
});

admin.MapDelete("/messages/{id:long}", (long id) => store.Delete(id) ? Results.Ok() : Results.NotFound());

admin.MapPost("/announce", (ReplyRequest req) =>
{
    var content = Text.Clean(req.Reply, singleLine: false);
    if (content.Length is < 1 or > 1000) return Fail("公告请写 1 到 1000 个字。");
    return Results.Ok(new { id = store.Insert("开发者", content, "admin", MessageStatus.Public, null, isDev: true) });
});

admin.MapPost("/bans", (BanRequest req) =>
{
    var ip = req.Ip;
    if (string.IsNullOrEmpty(ip) && req.MessageId is long mid) ip = store.GetIp(mid);
    if (string.IsNullOrEmpty(ip) || ip == "admin") return Fail("找不到要封禁的 IP。");
    store.Ban(ip, req.Reason ?? "");
    if (req.HideAll) store.HideAllFrom(ip);
    return Results.Ok(new { ip });
});
admin.MapGet("/bans", () => Results.Ok(store.ListBans()));
admin.MapDelete("/bans/{ip}", (string ip) => { store.Unban(ip); return Results.Ok(); });

admin.MapGet("/settings", () => Results.Ok(new
{
    open = store.GetSetting("open", "1") == "1",
    moderation = store.GetSetting("moderation", "flagged"),
    notice = store.GetSetting("notice", ""),
    extraWords = store.GetSetting("extra_words", ""),
}));
admin.MapPost("/settings", (SettingsRequest req) =>
{
    if (req.Open is bool open) store.SetSetting("open", open ? "1" : "0");
    if (req.Moderation is "flagged" or "all") store.SetSetting("moderation", req.Moderation);
    if (req.Notice != null) store.SetSetting("notice", Text.Clean(req.Notice, singleLine: false));
    if (req.ExtraWords != null) { store.SetSetting("extra_words", req.ExtraWords.Trim()); filter.Reload(); }
    return Results.Ok();
});
admin.MapPost("/password", (PasswordRequest req) =>
{
    if (!store.VerifyAdminPassword(req.Old ?? "")) return Fail("原密码不对。", 401);
    if ((req.New ?? "").Length < 10) return Fail("新密码至少 10 位。");
    store.SetAdminPassword(req.New!);
    auth.RotateSecret(); // 让旧登录全部失效
    File.Delete(Path.Combine(dataDir, "admin-password.txt"));
    return Results.Ok();
});

app.MapGet("/health", () => Results.Ok(new { ok = true, firstRunPassword = adminPassword != null }));

app.Run();

static IResult Fail(string message, int code = 400) => Results.Json(new { error = message }, statusCode: code);

record PostMessage(string? Name, string? Content, string? T, string? Website);
record LoginRequest(string? Password);
record StatusRequest(string? Status);
record ReplyRequest(string? Reply);
record BanRequest(string? Ip, long? MessageId, string? Reason, bool HideAll);
record SettingsRequest(bool? Open, string? Moderation, string? Notice, string? ExtraWords);
record PasswordRequest(string? Old, string? New);
