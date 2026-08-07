using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodexBridge.Core.Notifications;
using Microsoft.Extensions.Logging;

namespace CodexBridge.Host.Push;

/// <summary>FCM yapılandırması. Service account JSON dosyası; boşsa dispatcher pasiftir.</summary>
public sealed record FcmOptions
{
    /// <summary>Firebase service account anahtar dosyası (JSON) yolu.</summary>
    public string? ServiceAccountJsonPath { get; init; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ServiceAccountJsonPath)
        && File.Exists(ServiceAccountJsonPath);

    public static FcmOptions FromEnvironment() => new()
    {
        ServiceAccountJsonPath = Environment.GetEnvironmentVariable("CODEXBRIDGE_FCM_SERVICE_ACCOUNT") is { Length: > 0 } v ? v : null,
    };
}

/// <summary>
/// Faz 7: Android'e FCM HTTP v1 API ile bildirim gönderir. Service account'tan RS256 imzalı
/// assertion ile OAuth2 access token alınır (~55 dk önbelleklenir). UNREGISTERED/404 → cihaz düşer.
/// </summary>
public sealed class FcmPushDispatcher : IPushDispatcher, IDisposable
{
    private const string Scope = "https://www.googleapis.com/auth/firebase.messaging";

    private readonly ILogger<FcmPushDispatcher> _log;
    private readonly HttpClient _http;
    private readonly ServiceAccount _account;
    private readonly RSA _key;
    private readonly SemaphoreSlim _tokenGate = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiry = DateTimeOffset.MinValue;

    public FcmPushDispatcher(FcmOptions opts, ILogger<FcmPushDispatcher> log, HttpMessageHandler? handler = null)
    {
        _log = log;
        _http = new HttpClient(handler ?? new SocketsHttpHandler());
        _account = JsonSerializer.Deserialize<ServiceAccount>(File.ReadAllText(opts.ServiceAccountJsonPath!))
                   ?? throw new InvalidOperationException("FCM service account JSON çözümlenemedi.");
        _key = RSA.Create();
        _key.ImportFromPem(_account.PrivateKey);
    }

    public bool Supports(PushPlatform platform) => platform == PushPlatform.Fcm;

    public async Task<PushResult> SendAsync(DeviceRegistration device, NotificationEvent ev, CancellationToken ct = default)
    {
        string accessToken = await GetAccessTokenAsync(ct);
        var message = new
        {
            message = new
            {
                token = device.Token,
                notification = new { title = ev.Title, body = ev.Body },
                data = new Dictionary<string, string>
                {
                    ["providerId"] = ev.ProviderId,
                    ["kind"] = ev.Kind.ToString(),
                },
                android = new { priority = "high" },
            },
        };

        using var req = new HttpRequestMessage(HttpMethod.Post,
            $"https://fcm.googleapis.com/v1/projects/{_account.ProjectId}/messages:send")
        {
            Content = new StringContent(JsonSerializer.Serialize(message), System.Text.Encoding.UTF8, "application/json"),
        };
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + accessToken);

        using var resp = await _http.SendAsync(req, ct);
        if (resp.IsSuccessStatusCode) return PushResult.Success();

        string detail = await resp.Content.ReadAsStringAsync(ct);
        if (resp.StatusCode is HttpStatusCode.NotFound || detail.Contains("UNREGISTERED", StringComparison.OrdinalIgnoreCase))
            return PushResult.Gone($"FCM {(int)resp.StatusCode}: {detail}");
        return PushResult.Fail($"FCM {(int)resp.StatusCode}: {detail}");
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_accessToken is { } cached && DateTimeOffset.UtcNow < _accessTokenExpiry)
            return cached;

        await _tokenGate.WaitAsync(ct);
        try
        {
            if (_accessToken is { } again && DateTimeOffset.UtcNow < _accessTokenExpiry)
                return again;

            long now = Jwt.UnixNow();
            var header = new { alg = "RS256", typ = "JWT" };
            var claims = new
            {
                iss = _account.ClientEmail,
                scope = Scope,
                aud = _account.TokenUri,
                iat = now,
                exp = now + 3600,
            };
            string assertion = Jwt.SignRs256(header, claims, _key);

            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ["assertion"] = assertion,
            });
            using var resp = await _http.PostAsync(_account.TokenUri, form, ct);
            string json = await resp.Content.ReadAsStringAsync(ct);
            resp.EnsureSuccessStatusCode();

            var tok = JsonSerializer.Deserialize<TokenResponse>(json)
                      ?? throw new InvalidOperationException("FCM token yanıtı çözümlenemedi.");
            _accessToken = tok.AccessToken;
            // 60 sn güvenlik payıyla önbellekle.
            _accessTokenExpiry = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, tok.ExpiresIn - 60));
            return _accessToken!;
        }
        finally { _tokenGate.Release(); }
    }

    public void Dispose()
    {
        _http.Dispose();
        _key.Dispose();
        _tokenGate.Dispose();
    }

    private sealed record ServiceAccount
    {
        [JsonPropertyName("client_email")] public string ClientEmail { get; init; } = "";
        [JsonPropertyName("private_key")] public string PrivateKey { get; init; } = "";
        [JsonPropertyName("token_uri")] public string TokenUri { get; init; } = "https://oauth2.googleapis.com/token";
        [JsonPropertyName("project_id")] public string ProjectId { get; init; } = "";
    }

    private sealed record TokenResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; init; } = "";
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; init; } = 3600;
    }
}
