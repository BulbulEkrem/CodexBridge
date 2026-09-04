using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodexBridge.Core.Dashboard;

namespace CodexBridge.Core.Providers;

/// <summary>
/// Claude kota kaynağı. Kullanıcının <b>Claude Code OAuth token'ıyla</b>
/// (<c>~/.claude/.credentials.json</c>) Anthropic'in <c>/api/oauth/usage</c> uç noktasını çağırır.
/// API anahtarı gerekmez, tarayıcı çerezi gerekmez.
///
/// <para><b>Token yenileme:</b> Claude erişim token'ı ~8 saat yaşıyor, bu yüzden yenilemeyi
/// biz de yapmak zorundayız. Yenilenen token <b>yalnızca kendi DPAPI korumalı önbelleğimize</b>
/// yazılır — kullanıcının <c>.credentials.json</c> dosyasına asla dokunulmaz, çünkü Claude Code
/// aynı anda kendi yenilemesini yapıyor olabilir.</para>
///
/// <para><b>Token yakmaz:</b> bu uç nokta ölçüm okuma uç noktasıdır, inference isteği değildir;
/// aboneliğin kotasından bir şey düşmez.</para>
/// </summary>
public sealed class ClaudeUsageSource(
    HttpClient http,
    OAuthTokenCache tokens,
    string? credentialsPath = null,
    TimeProvider? clock = null) : IProviderSource
{
    private const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";
    private const string TokenUrl = "https://platform.claude.com/v1/oauth/token";
    private const string BetaHeader = "oauth-2025-04-20";

    /// <summary>Claude Code'un kendi genel OAuth istemci kimliği; gizli değildir.</summary>
    private const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";

    /// <summary>Token bu süre içinde dolacaksa şimdiden yenile.</summary>
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(5);

    /// <summary>Yenileme yanıtı <c>expires_in</c> vermezse varsayılan ömür.</summary>
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(8);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private string CredentialsPath => credentialsPath ?? AppPaths.ClaudeCredentialsFile;

    public string ProviderId => ProviderIds.Claude;

    public async Task<ProviderRow> FetchAsync(CancellationToken ct = default)
    {
        var file = ReadFileCredentials();
        string token = await ResolveTokenAsync(file, forceRefresh: false, ct);

        var (status, body) = await GetUsageAsync(token, ct);

        // 401: elimizdeki token bayatlamış olabilir (dosyadaki eski, önbellektekinin süresi dolmuş).
        // Bir kez zorla yenileyip tekrar dene.
        if (status == HttpStatusCode.Unauthorized && file.RefreshToken is not null)
        {
            token = await ResolveTokenAsync(file, forceRefresh: true, ct);
            (status, body) = await GetUsageAsync(token, ct);
        }

        if (status != HttpStatusCode.OK)
        {
            throw status == HttpStatusCode.Unauthorized || status == HttpStatusCode.Forbidden
                ? new ProviderSourceException(ProviderErrorKind.AuthExpired,
                    "Claude oturumu geçersiz. Bir terminalde `claude` ile tekrar giriş yap.")
                : new ProviderSourceException(HttpFailure.Classify(status),
                    $"Claude kullanım isteği HTTP {(int)status} döndü.");
        }

        return BuildRow(body, file.Tier);
    }

    // ---- HTTP ----

    private async Task<(HttpStatusCode, string)> GetUsageAsync(string token, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        req.Headers.TryAddWithoutValidation("Accept", "application/json");
        req.Headers.TryAddWithoutValidation("anthropic-beta", BetaHeader);
        req.Headers.TryAddWithoutValidation("User-Agent", "CodexBridge");

        HttpResponseMessage resp;
        try
        {
            resp = await http.SendAsync(req, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ProviderSourceException(ProviderErrorKind.Network, "Anthropic'e ulaşılamadı.", null, ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ProviderSourceException(ProviderErrorKind.Network, "Anthropic isteği zaman aşımına uğradı.", null, ex);
        }

        using (resp)
        {
            if (resp.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw new ProviderSourceException(ProviderErrorKind.RateLimited,
                    "Claude hız sınırı.", HttpFailure.RetryAfter(resp, _clock.GetUtcNow()));
            }
            string body = await resp.Content.ReadAsStringAsync(ct);
            return (resp.StatusCode, body);
        }
    }

    // ---- Token ----

    private async Task<string> ResolveTokenAsync(FileCredentials file, bool forceRefresh, CancellationToken ct)
    {
        var now = _clock.GetUtcNow();
        var cached = tokens.Get(ProviderIds.Claude);

        if (!forceRefresh)
        {
            if (cached is not null && !cached.IsExpiring(now, RefreshSkew))
                return cached.AccessToken;
            if (file.AccessToken is not null && !IsExpiring(file.ExpiresAt, now))
                return file.AccessToken;
        }

        string? refreshToken = cached?.RefreshToken ?? file.RefreshToken;
        if (refreshToken is null)
        {
            // Yenileyemiyoruz; elimizde ne varsa onu deneyelim, sunucu karar versin.
            return cached?.AccessToken ?? file.AccessToken
                ?? throw new ProviderSourceException(ProviderErrorKind.NoCredentials,
                    "Claude kimliği bulunamadı. Bir terminalde `claude` ile giriş yap.");
        }

        return await RefreshAsync(refreshToken, file.Scopes, ct);
    }

    private async Task<string> RefreshAsync(string refreshToken, string[]? scopes, CancellationToken ct)
    {
        var payload = new Dictionary<string, object>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = ClientId,
        };
        if (scopes is { Length: > 0 }) payload["scope"] = string.Join(' ', scopes);

        using var req = new HttpRequestMessage(HttpMethod.Post, TokenUrl)
        {
            Content = JsonContent.Create(payload),
        };
        req.Headers.TryAddWithoutValidation("anthropic-beta", BetaHeader);
        req.Headers.TryAddWithoutValidation("Accept", "application/json");
        req.Headers.TryAddWithoutValidation("User-Agent", "CodexBridge");

        HttpResponseMessage resp;
        try
        {
            resp = await http.SendAsync(req, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ProviderSourceException(ProviderErrorKind.Network,
                "Claude token yenileme sunucusuna ulaşılamadı.", null, ex);
        }

        using (resp)
        {
            if (resp.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw new ProviderSourceException(ProviderErrorKind.RateLimited,
                    "Claude token yenileme hız sınırı.", HttpFailure.RetryAfter(resp, _clock.GetUtcNow()));
            }
            if (!resp.IsSuccessStatusCode)
            {
                // Kalıcı hata: refresh token iptal edilmiş. Bayat önbelleği at.
                tokens.Clear(ProviderIds.Claude);
                throw new ProviderSourceException(ProviderErrorKind.AuthExpired,
                    "Claude oturumu yenilenemedi. Bir terminalde `claude` ile tekrar giriş yap.");
            }

            string body = await resp.Content.ReadAsStringAsync(ct);
            RefreshResponse? parsed;
            try { parsed = JsonSerializer.Deserialize<RefreshResponse>(body, Json); }
            catch (JsonException ex)
            {
                throw new ProviderSourceException(ProviderErrorKind.Parse,
                    "Claude token yenileme yanıtı çözümlenemedi.", null, ex);
            }

            string? access = parsed?.AccessToken?.Trim();
            if (string.IsNullOrEmpty(access))
                throw new ProviderSourceException(ProviderErrorKind.AuthExpired,
                    "Claude token yenileme boş token döndürdü.");

            var ttl = parsed!.ExpiresIn is { } secs && secs > 0
                ? TimeSpan.FromSeconds(secs)
                : DefaultTtl;

            tokens.Put(ProviderIds.Claude, new CachedToken(
                access,
                _clock.GetUtcNow() + ttl,
                parsed.RefreshToken?.Trim() is { Length: > 0 } rt ? rt : refreshToken));

            return access;
        }
    }

    private static bool IsExpiring(DateTimeOffset? expiresAt, DateTimeOffset now) =>
        expiresAt is { } exp && exp <= now + RefreshSkew;

    // ---- Kimlik dosyası ----

    private FileCredentials ReadFileCredentials()
    {
        string path = CredentialsPath;
        if (!File.Exists(path))
        {
            throw new ProviderSourceException(ProviderErrorKind.NoCredentials,
                "Claude kimliği bulunamadı. Bir terminalde `claude` ile giriş yap.");
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth))
                oauth = doc.RootElement;

            string? access = Str(oauth, "accessToken");
            string? refresh = Str(oauth, "refreshToken");

            DateTimeOffset? expiresAt = null;
            if (oauth.TryGetProperty("expiresAt", out var exp) && exp.TryGetDouble(out double ms))
                expiresAt = DateTimeOffset.FromUnixTimeMilliseconds((long)ms);

            string[]? scopes = null;
            if (oauth.TryGetProperty("scopes", out var sc) && sc.ValueKind == JsonValueKind.Array)
                scopes = sc.EnumerateArray().Select(x => x.GetString()).OfType<string>().ToArray();

            string? tier = Str(oauth, "subscriptionType") ?? Str(oauth, "rateLimitTier");

            if (access is null && refresh is null)
            {
                throw new ProviderSourceException(ProviderErrorKind.NoCredentials,
                    "Claude kimlik dosyasında token yok. Bir terminalde `claude` ile giriş yap.");
            }
            return new FileCredentials(access, refresh, expiresAt, scopes, tier);
        }
        catch (JsonException ex)
        {
            throw new ProviderSourceException(ProviderErrorKind.Parse,
                "Claude kimlik dosyası okunamadı.", null, ex);
        }
        catch (IOException ex)
        {
            throw new ProviderSourceException(ProviderErrorKind.NoCredentials,
                "Claude kimlik dosyası açılamadı.", null, ex);
        }
    }

    private static string? Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private sealed record FileCredentials(
        string? AccessToken, string? RefreshToken, DateTimeOffset? ExpiresAt, string[]? Scopes, string? Tier);

    // ---- Eşleme ----

    private ProviderRow BuildRow(string body, string? tier)
    {
        OAuthUsage usage;
        try
        {
            usage = JsonSerializer.Deserialize<OAuthUsage>(body, Json) ?? new OAuthUsage();
        }
        catch (JsonException ex)
        {
            throw new ProviderSourceException(ProviderErrorKind.Parse,
                "Claude kullanım yanıtı çözümlenemedi.", null, ex);
        }

        var now = _clock.GetUtcNow();
        var windows = new List<Dashboard.RateWindow>();
        Add(windows, WindowKinds.Session, "Oturum", usage.FiveHour);
        Add(windows, WindowKinds.Weekly, "Haftalık", usage.SevenDay);
        // Opus band'da gösterilmiyor ama snapshot'ta taşınıyor: bildirim ve widget kullanabilir.
        Add(windows, WindowKinds.Opus, "Haftalık · Opus", usage.SevenDayOpus);

        if (windows.Count == 0)
        {
            throw new ProviderSourceException(ProviderErrorKind.Parse,
                "Claude kullanım yanıtında kota penceresi yok.");
        }

        return ProviderRowFactory.Create(ProviderIds.Claude, "oauth", windows, now, plan: tier);
    }

    private static void Add(List<Dashboard.RateWindow> list, string kind, string label, OAuthWindow? w)
    {
        if (w?.Utilization is not { } u) return;
        // Kullanım 0–1 kesir olarak da 0–100 yüzde olarak da gelebiliyor.
        double pct = u <= 1.0 ? u * 100 : u;
        list.Add(RateWindowFactory.Create(kind, label, pct,
            DateTimeOffset.TryParse(w.ResetsAt, out var r) ? r : null));
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private sealed class OAuthUsage
    {
        [JsonPropertyName("five_hour")] public OAuthWindow? FiveHour { get; set; }
        [JsonPropertyName("seven_day")] public OAuthWindow? SevenDay { get; set; }
        [JsonPropertyName("seven_day_opus")] public OAuthWindow? SevenDayOpus { get; set; }
    }

    private sealed class OAuthWindow
    {
        [JsonPropertyName("utilization")] public double? Utilization { get; set; }
        [JsonPropertyName("resets_at")] public string? ResetsAt { get; set; }
    }

    private sealed class RefreshResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("expires_in")] public long? ExpiresIn { get; set; }
    }
}
