using CodexBridge.Core.Dashboard;
using CodexBridge.Core.Sources;
using CodexBridge.Host;
using CodexBridge.Host.Push;

// dashboard/v1 HTTP host — telefon istemcisi (iOS/Android widget) buraya bağlanır.
// macOS/Linux'ta `codexbar serve`, Windows'ta bu host aynı şemayı konuşur → tek telefon
// uygulaması üç OS'a birden bağlanır. Faz 7: host, eşik geçişlerinde telefona push iter.

var options = BridgeHostOptions.FromEnvironment(args);
if (options.Validate() is { } error)
{
    Console.Error.WriteLine($"CodexBridge host yapılandırma hatası: {error}");
    return 1;
}

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://{options.Host}:{options.Port}");
builder.Logging.ClearProviders().AddConsole();

// Veri kaynağı: fake (Faz 1/3) veya başka bir dashboard/v1 host'undan oku (http; çok-makine).
IUsageSource source = options.SourceKind == "http"
    ? new HttpUsageSource(new HttpClient(), options.SourceUrl!, options.SourceToken)
    : new FakeUsageSource();

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(new SnapshotCache(source, TimeSpan.FromSeconds(options.RefreshIntervalSeconds)));

// Faz 7 push: cihaz deposu + dispatcher (APNs/FCM yapılandırılıysa gerçek, yoksa loga düşer) + arka plan servisi.
builder.Services.AddSingleton<IDeviceRegistry>(new JsonFileDeviceRegistry(options.DevicesPath));
builder.Services.AddSingleton<LoggingPushDispatcher>();
builder.Services.AddSingleton<IPushDispatcher>(sp =>
{
    var lf = sp.GetRequiredService<ILoggerFactory>();
    var list = new List<IPushDispatcher>();
    var apns = ApnsOptions.FromEnvironment();
    if (apns.IsConfigured) list.Add(new ApnsPushDispatcher(apns, lf.CreateLogger<ApnsPushDispatcher>()));
    var fcm = FcmOptions.FromEnvironment();
    if (fcm.IsConfigured) list.Add(new FcmPushDispatcher(fcm, lf.CreateLogger<FcmPushDispatcher>()));
    return new CompositePushDispatcher(list, sp.GetRequiredService<LoggingPushDispatcher>(),
        lf.CreateLogger<CompositePushDispatcher>());
});
if (options.PushEnabled)
    builder.Services.AddHostedService<PushNotificationService>();

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

// --- Faz 7: cihaz kayıt uç noktaları (telefon push token'ını host'a bildirir) ---

// POST /dashboard/v1/devices — { token, platform: "apns"|"fcm", label? }
app.MapPost("/dashboard/v1/devices", async (HttpContext ctx, IDeviceRegistry registry, BridgeHostOptions opts, CancellationToken ct) =>
{
    ctx.Response.Headers.CacheControl = "no-store";
    if (!Authorize(ctx, opts))
    {
        ctx.Response.Headers.WWWAuthenticate = "Bearer";
        return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);
    }

    DeviceRegisterRequest? req;
    try { req = await ctx.Request.ReadFromJsonAsync<DeviceRegisterRequest>(ct); }
    catch { return Results.BadRequest(new { error = "gövde JSON çözümlenemedi" }); }

    if (req is null || string.IsNullOrWhiteSpace(req.Token))
        return Results.BadRequest(new { error = "token gerekli" });
    if (!Enum.TryParse<PushPlatform>(req.Platform, ignoreCase: true, out var platform))
        return Results.BadRequest(new { error = "platform apns|fcm olmalı" });

    await registry.AddAsync(new DeviceRegistration
    {
        Token = req.Token.Trim(),
        Platform = platform,
        Label = string.IsNullOrWhiteSpace(req.Label) ? null : req.Label!.Trim(),
        RegisteredAt = DateTimeOffset.UtcNow,
    }, ct);
    return Results.Json(new { status = "registered" });
});

// DELETE /dashboard/v1/devices — { token }
app.MapDelete("/dashboard/v1/devices", async (HttpContext ctx, IDeviceRegistry registry, BridgeHostOptions opts, CancellationToken ct) =>
{
    ctx.Response.Headers.CacheControl = "no-store";
    if (!Authorize(ctx, opts))
    {
        ctx.Response.Headers.WWWAuthenticate = "Bearer";
        return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);
    }

    DeviceRegisterRequest? req;
    try { req = await ctx.Request.ReadFromJsonAsync<DeviceRegisterRequest>(ct); }
    catch { return Results.BadRequest(new { error = "gövde JSON çözümlenemedi" }); }

    if (req is null || string.IsNullOrWhiteSpace(req.Token))
        return Results.BadRequest(new { error = "token gerekli" });

    bool removed = await registry.RemoveAsync(req.Token.Trim(), ct);
    return Results.Json(new { status = removed ? "unregistered" : "not-found" });
});

app.Logger.LogInformation("CodexBridge host: http://{Host}:{Port} (kaynak: {Source}, token: {Token}, push: {Push})",
    options.Host, options.Port, options.SourceKind,
    options.HasToken ? "ayarlı" : "YOK → dashboard 401",
    options.PushEnabled ? "açık" : "kapalı");

app.Run();
return 0;

// --- yardımcılar ---
static bool Authorize(HttpContext ctx, BridgeHostOptions opts)
{
    // Fails closed: token ayarlı değilse korumalı rotalar daima reddedilir.
    if (!opts.HasToken) return false;
    var header = ctx.Request.Headers.Authorization.ToString();
    const string prefix = "Bearer ";
    if (!header.StartsWith(prefix, StringComparison.Ordinal)) return false;
    return opts.TokenMatches(header[prefix.Length..].Trim());
}

partial class Program
{
    internal const string HostVersion = "codexbridge-host-0.2";
}

/// <summary>Cihaz kayıt/silme istek gövdesi.</summary>
internal sealed record DeviceRegisterRequest(string? Token, string? Platform, string? Label);
