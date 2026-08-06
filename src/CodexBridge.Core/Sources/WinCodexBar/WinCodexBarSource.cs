using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodexBridge.Core.Dashboard;

namespace CodexBridge.Core.Sources.WinCodexBar;

/// <summary>
/// Faz 2 veri kaynağı: Win-CodexBar'ın (MIT) HTTP <c>serve</c>'ünden (<c>/usage</c>, <c>/cost</c>)
/// veri çekip <c>dashboard/v1</c> şemasına çevirir. Win-CodexBar Windows'ta zaten `/usage`+`/cost`
/// sunuyor ama telefon-dostu versiyonlu snapshot şemasını sunmuyor; bu adaptör o boşluğu doldurur.
///
/// Eşleme mantığı <see cref="MapUsage"/>'da saf/statik tutuldu ki canlı Win-CodexBar olmadan,
/// örnek JSON ile test edilebilsin.
/// </summary>
public sealed class WinCodexBarSource(HttpClient http, string baseUrl, string? bearerToken) : IUsageSource
{
    public async Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl.TrimEnd('/')}/usage");
        if (!string.IsNullOrEmpty(bearerToken))
            req.Headers.Authorization = new("Bearer", bearerToken);

        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);

        var rows = MapUsage(json, DateTimeOffset.UtcNow);
        return new DashboardSnapshot
        {
            SchemaVersion = 1,
            GeneratedAt = DateTimeOffset.UtcNow,
            StaleAfterSeconds = 180,
            Host = new HostInfo { CodexBarVersion = "win-codexbar-serve", RefreshIntervalSeconds = 0 },
            Providers = rows,
        };
    }

    /// <summary>
    /// Win-CodexBar <c>/usage</c> yanıtını (dizi) dashboard/v1 satırlarına çevirir. Saf fonksiyon —
    /// test edilebilir. Bilinmeyen/eksik alanlar atlanır; hata satırları <c>error</c> ile taşınır.
    /// </summary>
    public static IReadOnlyList<ProviderRow> MapUsage(string usageJson, DateTimeOffset now)
    {
        var items = JsonSerializer.Deserialize<List<WinUsageItem>>(usageJson, WinJson) ?? [];
        var rows = new List<ProviderRow>(items.Count);
        int sort = 0;
        foreach (var item in items)
        {
            var windows = new List<RateWindow>();
            if (item.Usage?.Primary is { } primary)
                windows.Add(ToWindow("session", "Session", primary));
            if (item.Usage?.Secondary is { } secondary)
                windows.Add(ToWindow("weekly", "Weekly", secondary));

            rows.Add(new ProviderRow
            {
                Id = item.Provider ?? "unknown",
                Name = Capitalize(item.Provider ?? "unknown"),
                Enabled = true,
                Source = item.Source,
                Status = item.Error is null
                    ? new ProviderStatus { Level = LevelFor(windows), Label = null, UpdatedAt = now }
                    : new ProviderStatus { Level = StatusLevel.Unknown, UpdatedAt = now },
                Windows = windows,
                Cost = item.Cost?.TotalUsd is { } usd ? new CostInfo { Last30DaysUsd = usd } : null,
                Display = new DisplayHints { SortKey = sort++, Priority = "normal" },
                Error = item.Error is null ? null : new ProviderError { Message = item.Error },
                UpdatedAt = now,
            });
        }
        return rows;
    }

    private static RateWindow ToWindow(string kind, string label, WinRateWindow w) => new()
    {
        Kind = kind,
        Label = label,
        UsedPercent = Math.Round(w.UsedPercent, 1),
        RemainingPercent = Math.Round(Math.Clamp(100 - w.UsedPercent, 0, 100), 1),
        ResetAt = w.ResetsAt,
    };

    private static StatusLevel LevelFor(IReadOnlyList<RateWindow> windows)
    {
        double max = 0;
        foreach (var w in windows) max = Math.Max(max, w.UsedPercent ?? 0);
        return max >= 90 ? StatusLevel.Critical : max >= 75 ? StatusLevel.Warning : StatusLevel.Ok;
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static readonly JsonSerializerOptions WinJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    // --- Win-CodexBar /usage DTO'ları (snake_case) ---
    private sealed class WinUsageItem
    {
        public string? Provider { get; set; }
        public string? Source { get; set; }
        public WinUsage? Usage { get; set; }
        public WinCost? Cost { get; set; }
        public string? Error { get; set; }
    }
    private sealed class WinUsage
    {
        public WinRateWindow? Primary { get; set; }
        public WinRateWindow? Secondary { get; set; }
    }
    private sealed class WinCost
    {
        [JsonPropertyName("total_usd")] public double? TotalUsd { get; set; }
    }
    private sealed class WinRateWindow
    {
        [JsonPropertyName("used_percent")] public double UsedPercent { get; set; }
        [JsonPropertyName("resets_at")] public DateTimeOffset? ResetsAt { get; set; }
    }
}
