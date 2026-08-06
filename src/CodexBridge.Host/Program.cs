using CodexBridge.Core.Dashboard;
using CodexBridge.Core.Sources;
using CodexBridge.Host;

// dashboard/v1 HTTP host — telefon istemcisi (iOS/Android widget) buraya bağlanır.
// macOS/Linux'ta `codexbar serve`, Windows'ta bu host aynı şemayı konuşur → tek telefon
// uygulaması üç OS'a birden bağlanır.

var options = BridgeHostOptions.FromEnvironment(args);
if (options.Validate() is { } error)
{
    Console.Error.WriteLine($"CodexBridge host yapılandırma hatası: {error}");
    return 1;
}

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://{options.Host}:{options.Port}");
builder.Logging.ClearProviders().AddConsole();

// Faz 1/3: sahte veri kaynağı. Faz 2'de WinCodexBarSource, Faz 5'te kendi katmanımız takılır.
IUsageSource source = new FakeUsageSource();
builder.Services.AddSingleton(new SnapshotCache(source, TimeSpan.FromSeconds(options.RefreshIntervalSeconds)));
builder.Services.AddSingleton(options);

var app = builder.Build();

if (!options.IsLoopback)
    app.Logger.LogWarning("Loopback dışı bind: bearer token her istekte DÜZ METİN geçer. TLS proxy/Tailscale önerilir.");

// DNS-rebinding koruması: loopback bind'de yalnızca loopback Host başlığını kabul et.
app.Use(async (ctx, next) =>
{
    if (options.IsLoopback)
    {
        var host = ctx.Request.Host.Host;
        if (!BridgeHostOptions.IsLoopbackHost(host))
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            await ctx.Response.WriteAsync("bad host");
            return;
        }
    }
    await next();
});

// /health — daima açık, yalnızca durum + sürüm (liveness probe).
app.MapGet("/health", () => Results.Json(new { status = "ok", version = HostVersion }));

// /dashboard/v1/snapshot — bearer korumalı, token yoksa kapalı-başarısız (fails closed).
app.MapGet("/dashboard/v1/snapshot", async (HttpContext ctx, SnapshotCache cache, BridgeHostOptions opts, CancellationToken ct) =>
{
    ctx.Response.Headers.CacheControl = "no-store";

    if (!Authorize(ctx, opts))
    {
        ctx.Response.Headers.WWWAuthenticate = "Bearer";
        return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);
    }

    DashboardSnapshot snapshot = await cache.GetAsync(ct);
    // host.refreshIntervalSeconds'ı gerçek ayardan bildir.
    snapshot = snapshot with { Host = snapshot.Host with { RefreshIntervalSeconds = opts.RefreshIntervalSeconds } };
    return Results.Content(snapshot.ToJson(), "application/json; charset=utf-8");
});

app.Logger.LogInformation("CodexBridge host: http://{Host}:{Port} (token: {Token})",
    options.Host, options.Port, options.HasToken ? "ayarlı" : "YOK → dashboard 401");

app.Run();
return 0;

// --- yardımcılar ---
static bool Authorize(HttpContext ctx, BridgeHostOptions opts)
{
    // Fails closed: token ayarlı değilse dashboard rotası daima reddedilir.
    if (!opts.HasToken) return false;
    var header = ctx.Request.Headers.Authorization.ToString();
    const string prefix = "Bearer ";
    if (!header.StartsWith(prefix, StringComparison.Ordinal)) return false;
    return opts.TokenMatches(header[prefix.Length..].Trim());
}

partial class Program
{
    internal const string HostVersion = "codexbridge-host-0.1";
}
