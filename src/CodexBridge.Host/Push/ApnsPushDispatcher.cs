using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodexBridge.Core.Notifications;
using Microsoft.Extensions.Logging;

namespace CodexBridge.Host.Push;

/// <summary>APNs yapılandırması (token tabanlı, .p8 anahtarı). Alanlar boşsa dispatcher pasiftir.</summary>
public sealed record ApnsOptions
{
    /// <summary>Apple Developer key ID (10 karakter).</summary>
    public string? KeyId { get; init; }
    /// <summary>Apple Developer team ID (10 karakter).</summary>
    public string? TeamId { get; init; }
    /// <summary>Uygulama bundle ID → apns-topic.</summary>
    public string? BundleId { get; init; }
    /// <summary>AuthKey_XXXX.p8 dosya yolu (PKCS#8 EC özel anahtar).</summary>
    public string? P8KeyPath { get; init; }
    /// <summary>true → api.sandbox.push.apple.com (geliştirme derlemeleri).</summary>
    public bool UseSandbox { get; init; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(KeyId)
        && !string.IsNullOrWhiteSpace(TeamId)
        && !string.IsNullOrWhiteSpace(BundleId)
        && !string.IsNullOrWhiteSpace(P8KeyPath) && File.Exists(P8KeyPath);

    public string Host => UseSandbox ? "api.sandbox.push.apple.com" : "api.push.apple.com";

    public static ApnsOptions FromEnvironment() => new()
    {
        KeyId = Env("CODEXBRIDGE_APNS_KEY_ID"),
        TeamId = Env("CODEXBRIDGE_APNS_TEAM_ID"),
        BundleId = Env("CODEXBRIDGE_APNS_BUNDLE_ID"),
        P8KeyPath = Env("CODEXBRIDGE_APNS_P8_PATH"),
        UseSandbox = Environment.GetEnvironmentVariable("CODEXBRIDGE_APNS_SANDBOX") is "1" or "true" or "TRUE",
    };

    private static string? Env(string k)
        => Environment.GetEnvironmentVariable(k) is { Length: > 0 } v ? v : null;
}

/// <summary>
/// Faz 7: iOS'a APNs HTTP/2 ile bildirim gönderir. Provider token (ES256 JWT) ~40 dk önbelleklenir
/// (Apple 20–60 dk arası yeniler). 410 → cihaz artık yok, kayıttan düşer.
/// </summary>
public sealed class ApnsPushDispatcher : IPushDispatcher, IDisposable
{
    private readonly ApnsOptions _opts;
    private readonly ILogger<ApnsPushDispatcher> _log;
    private readonly HttpClient _http;
    private readonly ECDsa _key;
    private readonly SemaphoreSlim _tokenGate = new(1, 1);
    private string? _cachedToken;
    private DateTimeOffset _tokenIssuedAt = DateTimeOffset.MinValue;

    public ApnsPushDispatcher(ApnsOptions opts, ILogger<ApnsPushDispatcher> log, HttpMessageHandler? handler = null)
    {
        _opts = opts;
        _log = log;
        _http = new HttpClient(handler ?? new SocketsHttpHandler()) { DefaultRequestVersion = HttpVersion.Version20, DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact };
        _key = ECDsa.Create();
        _key.ImportFromPem(File.ReadAllText(_opts.P8KeyPath!));
    }

    public bool Supports(PushPlatform platform) => platform == PushPlatform.Apns;

    public async Task<PushResult> SendAsync(DeviceRegistration device, NotificationEvent ev, CancellationToken ct = default)
    {
        string token = await GetProviderTokenAsync(ct);
        var body = BuildPayload(ev);

        using var req = new HttpRequestMessage(HttpMethod.Post, $"https://{_opts.Host}/3/device/{device.Token}")
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("bearer", token);
        req.Headers.TryAddWithoutValidation("apns-topic", _opts.BundleId);
        req.Headers.TryAddWithoutValidation("apns-push-type", "alert");
        req.Headers.TryAddWithoutValidation("apns-priority", "10");

        using var resp = await _http.SendAsync(req, ct);
        if (resp.IsSuccessStatusCode) return PushResult.Success();

        string detail = await resp.Content.ReadAsStringAsync(ct);
        // 410 Gone / "Unregistered" → cihaz token'ı geçersiz.
        if (resp.StatusCode == HttpStatusCode.Gone || detail.Contains("Unregistered", StringComparison.OrdinalIgnoreCase))
            return PushResult.Gone($"APNs {(int)resp.StatusCode}: {detail}");
        return PushResult.Fail($"APNs {(int)resp.StatusCode}: {detail}");
    }

    private string BuildPayload(NotificationEvent ev)
    {
        var payload = new Dictionary<string, object?>
        {
            ["aps"] = new Dictionary<string, object?>
            {
                ["alert"] = new { title = ev.Title, body = ev.Body },
                ["sound"] = "default",
                ["thread-id"] = ev.ProviderId,
            },
            ["providerId"] = ev.ProviderId,
            ["kind"] = ev.Kind.ToString(),
        };
        return JsonSerializer.Serialize(payload);
    }

    private async Task<string> GetProviderTokenAsync(CancellationToken ct)
    {
        if (_cachedToken is { } cached && DateTimeOffset.UtcNow - _tokenIssuedAt < TimeSpan.FromMinutes(40))
            return cached;

        await _tokenGate.WaitAsync(ct);
        try
        {
            if (_cachedToken is { } again && DateTimeOffset.UtcNow - _tokenIssuedAt < TimeSpan.FromMinutes(40))
                return again;

            var header = new { alg = "ES256", kid = _opts.KeyId };
            var claims = new { iss = _opts.TeamId, iat = Jwt.UnixNow() };
            _cachedToken = Jwt.SignEs256(header, claims, _key);
            _tokenIssuedAt = DateTimeOffset.UtcNow;
            return _cachedToken;
        }
        finally { _tokenGate.Release(); }
    }

    public void Dispose()
    {
        _http.Dispose();
        _key.Dispose();
        _tokenGate.Dispose();
    }
}
