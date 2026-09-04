using System.Net;
using System.Text.Json;
using CodexBridge.Core.Dashboard;

namespace CodexBridge.Core.Providers;

/// <summary>
/// Codex kota kaynağı. Codex CLI'ın bıraktığı OAuth token'ıyla
/// (<c>~/.codex/auth.json</c> → <c>tokens.access_token</c>) ChatGPT arka ucunun
/// <c>/backend-api/wham/usage</c> uç noktasını çağırır.
///
/// <para><b>Token yenilemiyoruz.</b> Claude'un aksine Codex token'ı uzun ömürlü ve
/// yenilemesini CLI kendi yapıyor. 401/403 gelirse kullanıcıyı <c>codex login</c>'e
/// yönlendirmek doğru davranış — araya girip CLI'ın dosyasını ezmek değil.</para>
///
/// <para><b>Token yakmaz:</b> bu bir ölçüm okuma uç noktasıdır, inference isteği değildir.</para>
///
/// <para>Not: ChatGPT <b>sohbet</b> mesaj kotası bu uç noktada yok ve OpenAI böyle bir uç nokta
/// yayınlamıyor — burada ölçülen yalnızca Codex kotasıdır.</para>
/// </summary>
public sealed class CodexUsageSource(
    HttpClient http,
    string? authPath = null,
    string? baseUrlOverride = null,
    TimeProvider? clock = null) : IProviderSource
{
    private const string DefaultBaseUrl = "https://chatgpt.com/backend-api";
    private const string UsagePath = "/wham/usage";

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private string AuthPath => authPath ?? AppPaths.CodexAuthFile;

    public string ProviderId => ProviderIds.Codex;

    public async Task<ProviderRow> FetchAsync(CancellationToken ct = default)
    {
        var creds = ReadCredentials();
        string url = (baseUrlOverride ?? DefaultBaseUrl).TrimEnd('/') + UsagePath;

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {creds.AccessToken}");
        req.Headers.TryAddWithoutValidation("Accept", "application/json");
        req.Headers.TryAddWithoutValidation("User-Agent", "CodexBridge");
        if (!string.IsNullOrEmpty(creds.AccountId))
            req.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", creds.AccountId);

        HttpResponseMessage resp;
        try
        {
            resp = await http.SendAsync(req, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ProviderSourceException(ProviderErrorKind.Network, "ChatGPT arka ucuna ulaşılamadı.", null, ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ProviderSourceException(ProviderErrorKind.Network, "Codex isteği zaman aşımına uğradı.", null, ex);
        }

        string body;
        using (resp)
        {
            if (resp.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw new ProviderSourceException(ProviderErrorKind.RateLimited,
                    "Codex hız sınırı.", HttpFailure.RetryAfter(resp, _clock.GetUtcNow()));
            }
            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new ProviderSourceException(ProviderErrorKind.AuthExpired,
                    "Codex oturumu geçersiz. Bir terminalde `codex login` çalıştır.");
            }
            if (!resp.IsSuccessStatusCode)
            {
                throw new ProviderSourceException(HttpFailure.Classify(resp.StatusCode),
                    $"Codex kullanım isteği HTTP {(int)resp.StatusCode} döndü.");
            }
            body = await resp.Content.ReadAsStringAsync(ct);
        }

        return BuildRow(body, creds.PlanLabel);
    }

    // ---- Kimlik dosyası ----

    private CodexCredentials ReadCredentials()
    {
        string path = AuthPath;
        if (!File.Exists(path))
        {
            throw new ProviderSourceException(ProviderErrorKind.NoCredentials,
                "Codex kimliği bulunamadı. Bir terminalde `codex login` çalıştır.");
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;

            if (!root.TryGetProperty("tokens", out var tokens) || tokens.ValueKind != JsonValueKind.Object)
            {
                // Yalnızca OPENAI_API_KEY varsa kullanıcı abonelik değil API anahtarı modunda:
                // o farklı bir ürün ve kota penceresi döndürmüyor.
                throw new ProviderSourceException(ProviderErrorKind.NoCredentials,
                    root.TryGetProperty("OPENAI_API_KEY", out _)
                        ? "Codex API anahtarı modunda; abonelik kotası okunamıyor. `codex login` ile abonelik hesabına giriş yap."
                        : "Codex kimlik dosyasında token yok. Bir terminalde `codex login` çalıştır.");
            }

            string? access = Str(tokens, "access_token");
            if (string.IsNullOrEmpty(access))
            {
                throw new ProviderSourceException(ProviderErrorKind.NoCredentials,
                    "Codex erişim token'ı yok. Bir terminalde `codex login` çalıştır.");
            }

            return new CodexCredentials(access, Str(tokens, "account_id"), Str(tokens, "plan_type"));
        }
        catch (JsonException ex)
        {
            throw new ProviderSourceException(ProviderErrorKind.Parse, "Codex kimlik dosyası okunamadı.", null, ex);
        }
        catch (IOException ex)
        {
            throw new ProviderSourceException(ProviderErrorKind.NoCredentials, "Codex kimlik dosyası açılamadı.", null, ex);
        }
    }

    private sealed record CodexCredentials(string AccessToken, string? AccountId, string? PlanLabel);

    // ---- Eşleme ----

    /// <summary>
    /// <c>/wham/usage</c> yanıtını pencerelere çevirir. Yanıt iki biçimde gelebiliyor:
    /// adlandırılmış (<c>primary_window</c> / <c>secondary_window</c>) veya dizi
    /// (<c>windows[]</c>). İkisi de destekleniyor; pencere rolü <c>limit_window_seconds</c>'a
    /// göre belirleniyor, o yoksa sıraya düşülüyor.
    /// </summary>
    private ProviderRow BuildRow(string body, string? plan)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(body); }
        catch (JsonException ex)
        {
            throw new ProviderSourceException(ProviderErrorKind.Parse, "Codex kullanım yanıtı çözümlenemedi.", null, ex);
        }

        using (doc)
        {
            var root = doc.RootElement;
            var container = Obj(root, "rate_limits") ?? Obj(root, "rate_limit") ?? root;

            var parsed = new List<(int? Minutes, RateWindow Window)>();

            if (Obj(container, "primary_window") is { } primary && ParseWindow(primary) is { } pw)
                parsed.Add(pw);
            if (Obj(container, "secondary_window") is { } secondary && ParseWindow(secondary) is { } sw)
                parsed.Add(sw);

            if (parsed.Count == 0 && container.TryGetProperty("windows", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in arr.EnumerateArray())
                    if (ParseWindow(el) is { } w) parsed.Add(w);
            }

            if (parsed.Count == 0)
            {
                throw new ProviderSourceException(ProviderErrorKind.Parse,
                    "Codex kullanım yanıtında kota penceresi yok. Özel bir arka uç yapılandırdıysan kota bilgisi gelmez.");
            }

            var windows = AssignKinds(parsed);
            return ProviderRowFactory.Create(ProviderIds.Codex, "oauth", windows, _clock.GetUtcNow(), plan: plan);
        }
    }

    /// <summary>Pencerelere <c>kind</c> atar: süresi bilinenler role göre, bilinmeyenler sıraya göre.</summary>
    private static List<RateWindow> AssignKinds(List<(int? Minutes, RateWindow Window)> parsed)
    {
        var result = new List<RateWindow>(parsed.Count);
        bool sessionTaken = false, weeklyTaken = false, monthlyTaken = false;

        foreach (var (minutes, w) in parsed)
        {
            string kind;
            string label;

            switch (minutes)
            {
                case <= 360 and > 0 when !sessionTaken:
                    kind = WindowKinds.Session; label = "Oturum"; sessionTaken = true; break;
                case >= 43200 when !monthlyTaken:
                    kind = WindowKinds.Monthly; label = "Aylık"; monthlyTaken = true; break;
                case >= 10080 when !weeklyTaken:
                    kind = WindowKinds.Weekly; label = "Haftalık"; weeklyTaken = true; break;
                default:
                    // Süre bilinmiyor ya da rol dolu: sırayla oturum → haftalık → diğer.
                    if (!sessionTaken) { kind = WindowKinds.Session; label = "Oturum"; sessionTaken = true; }
                    else if (!weeklyTaken) { kind = WindowKinds.Weekly; label = "Haftalık"; weeklyTaken = true; }
                    else { kind = "other"; label = "Diğer"; }
                    break;
            }

            result.Add(w with { Kind = kind, Label = label });
        }

        return result;
    }

    private (int? Minutes, RateWindow Window)? ParseWindow(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;

        double? used = Num(el, "used_percent") ?? Num(el, "usage_percent");
        if (used is null) return null;

        int? minutes = Num(el, "limit_window_seconds") is { } secs && secs > 0
            ? (int)(secs / 60)
            : null;

        DateTimeOffset? resetAt = null;
        if (Num(el, "reset_at") is { } epoch && epoch > 0)
            resetAt = DateTimeOffset.FromUnixTimeSeconds((long)epoch);
        else if (Num(el, "resets_in_seconds") is { } inSecs && inSecs > 0)
            resetAt = _clock.GetUtcNow().AddSeconds(inSecs);

        // kind/label AssignKinds tarafından atanır.
        return (minutes, RateWindowFactory.Create("pending", "", used.Value, resetAt));
    }

    private static JsonElement? Obj(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object
        && parent.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.Object
            ? v : null;

    private static double? Num(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d)
            ? d : null;

    private static string? Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
