using System.Text.Json;
using System.Text.Json.Serialization;
using CodexBridge.Core.Dashboard;

namespace CodexBridge.Core.Sources;

/// <summary>
/// Claude kullanımını <b>API anahtarı olmadan</b> çeker: yereldeki Claude Code OAuth token'ıyla
/// (<c>~/.claude/.credentials.json</c> → <c>claudeAiOauth.accessToken</c>) Anthropic'in
/// <c>/api/oauth/usage</c> uç noktasını çağırır. Üst akış CodexBar'ın
/// <c>ClaudeOAuthUsageFetcher.swift</c>'inin C# karşılığı.
///
/// Token kullanıcının kendi makinesinde kalır, yalnızca Anthropic'e (sahibine) gider; loglanmaz.
/// </summary>
public sealed class ClaudeOAuthSource(HttpClient http, string? credentialsPath = null) : IUsageSource
{
    private const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";
    private const string BetaHeader = "oauth-2025-04-20";

    private string CredentialsPath => credentialsPath
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", ".credentials.json");

    public async Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        var (token, subscription) = ReadToken();
        var now = DateTimeOffset.UtcNow;

        using var req = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        req.Headers.TryAddWithoutValidation("Accept", "application/json");
        req.Headers.TryAddWithoutValidation("anthropic-beta", BetaHeader);
        req.Headers.TryAddWithoutValidation("User-Agent", "CodexBridge/0.1");

        using var resp = await http.SendAsync(req, ct);
        string body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"OAuth usage HTTP {(int)resp.StatusCode}: {Truncate(body)}");

        var usage = JsonSerializer.Deserialize<OAuthUsage>(body, Json) ?? new OAuthUsage();

        var windows = new List<RateWindow>();
        AddWindow(windows, "session", "5 saat", usage.FiveHour);
        AddWindow(windows, "weekly", "7 gün", usage.SevenDay);
        AddWindow(windows, "tertiary", "7 gün · Opus", usage.SevenDayOpus);

        double max = 0;
        foreach (var w in windows) max = Math.Max(max, w.UsedPercent ?? 0);
        var level = max >= 90 ? StatusLevel.Critical : max >= 75 ? StatusLevel.Warning : StatusLevel.Ok;

        var row = new ProviderRow
        {
            Id = "claude",
            Name = "Claude",
            Enabled = true,
            Source = "oauth",
            Status = new ProviderStatus { Level = level, UpdatedAt = now },
            Identity = subscription is null ? null : new ProviderIdentity { Plan = subscription },
            Windows = windows,
            Display = new DisplayHints { AccentColor = "#D97757", SortKey = 0, Priority = "normal" },
            UpdatedAt = now,
        };

        return new DashboardSnapshot
        {
            SchemaVersion = 1,
            GeneratedAt = now,
            StaleAfterSeconds = 180,
            Host = new HostInfo { CodexBarVersion = "codexbridge-claude-oauth", RefreshIntervalSeconds = 0 },
            Providers = [row],
        };
    }

    private static void AddWindow(List<RateWindow> list, string kind, string label, OAuthWindow? w)
    {
        if (w?.Utilization is not { } u) return;
        // utilization 0–1 kesir ise yüzdeye çevir; zaten 0–100 ise olduğu gibi.
        double pct = u <= 1.0 ? u * 100 : u;
        list.Add(new RateWindow
        {
            Kind = kind, Label = label,
            UsedPercent = Math.Round(pct, 1),
            RemainingPercent = Math.Round(Math.Clamp(100 - pct, 0, 100), 1),
            ResetAt = DateTimeOffset.TryParse(w.ResetsAt, out var r) ? r : null,
        });
    }

    private (string token, string? subscription) ReadToken()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(CredentialsPath));
        var oauth = doc.RootElement.GetProperty("claudeAiOauth");
        string token = oauth.GetProperty("accessToken").GetString()
            ?? throw new InvalidOperationException("accessToken bulunamadı");
        string? sub = oauth.TryGetProperty("subscriptionType", out var s) ? s.GetString() : null;
        return (token, sub);
    }

    private static string Truncate(string s) => s.Length > 300 ? s[..300] + "…" : s;

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private sealed class OAuthUsage
    {
        [JsonPropertyName("five_hour")] public OAuthWindow? FiveHour { get; set; }
        [JsonPropertyName("seven_day")] public OAuthWindow? SevenDay { get; set; }
        [JsonPropertyName("seven_day_opus")] public OAuthWindow? SevenDayOpus { get; set; }
        [JsonPropertyName("seven_day_sonnet")] public OAuthWindow? SevenDaySonnet { get; set; }
    }
    private sealed class OAuthWindow
    {
        [JsonPropertyName("utilization")] public double? Utilization { get; set; }
        [JsonPropertyName("resets_at")] public string? ResetsAt { get; set; }
    }
}
