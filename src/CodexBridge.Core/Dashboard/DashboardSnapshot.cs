using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexBridge.Core.Dashboard;

/// <summary>
/// dashboard/v1 snapshot sözleşmesi — üst akış CodexBar'ın <c>docs/dashboard-api.md</c>
/// şemasının birebir C# karşılığı. Windows host'umuz bunu üretecek, telefon istemcisi bunu tüketecek.
/// Kimlik daima maskeli (e-posta yerel kısmı gizli). Alan adları camelCase JSON ile eşleşir.
/// </summary>
public sealed record DashboardSnapshot
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; init; } = 1;
    [JsonPropertyName("generatedAt")] public DateTimeOffset GeneratedAt { get; init; }
    /// <summary>İstemci tarafı bayatlık ipucu. Şema minimumu 180 sn.</summary>
    [JsonPropertyName("staleAfterSeconds")] public int StaleAfterSeconds { get; init; } = 180;
    [JsonPropertyName("host")] public required HostInfo Host { get; init; }
    [JsonPropertyName("providers")] public IReadOnlyList<ProviderRow> Providers { get; init; } = [];

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        // enum değerleri sözleşmeye uygun küçük harf: "ok" | "warning" | "critical" | "unknown".
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
    public static DashboardSnapshot? FromJson(string json) =>
        JsonSerializer.Deserialize<DashboardSnapshot>(json, JsonOptions);
}

public sealed record HostInfo
{
    [JsonPropertyName("codexBarVersion")] public string? CodexBarVersion { get; init; }
    /// <summary>HTTP yanıt önbelleği aralığı; tek-atış komutta 0.</summary>
    [JsonPropertyName("refreshIntervalSeconds")] public int RefreshIntervalSeconds { get; init; }
}

public sealed record ProviderRow
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("enabled")] public bool Enabled { get; init; } = true;
    /// <summary>Verinin kaynağı: oauth, apiToken, web, cli, localProbe...</summary>
    [JsonPropertyName("source")] public string? Source { get; init; }
    [JsonPropertyName("status")] public ProviderStatus? Status { get; init; }
    [JsonPropertyName("identity")] public ProviderIdentity? Identity { get; init; }
    [JsonPropertyName("windows")] public IReadOnlyList<RateWindow> Windows { get; init; } = [];
    [JsonPropertyName("credits")] public CreditInfo? Credits { get; init; }
    [JsonPropertyName("cost")] public CostInfo? Cost { get; init; }
    [JsonPropertyName("display")] public DisplayHints? Display { get; init; }
    [JsonPropertyName("error")] public ProviderError? Error { get; init; }
    [JsonPropertyName("updatedAt")] public DateTimeOffset? UpdatedAt { get; init; }
}

/// <summary>Sağlayıcı servis durumu. level: ok | warning | critical | unknown.</summary>
public sealed record ProviderStatus
{
    [JsonPropertyName("level")] public StatusLevel Level { get; init; } = StatusLevel.Unknown;
    [JsonPropertyName("label")] public string? Label { get; init; }
    [JsonPropertyName("updatedAt")] public DateTimeOffset? UpdatedAt { get; init; }
}

public enum StatusLevel { Ok, Warning, Critical, Unknown }

/// <summary>Maskeli kimlik: e-posta yerel kısmı gizli, alan adı ve plan korunur.</summary>
public sealed record ProviderIdentity
{
    [JsonPropertyName("accountEmail")] public string? AccountEmail { get; init; }
    [JsonPropertyName("plan")] public string? Plan { get; init; }
}

/// <summary>Oturum / haftalık / üçüncül veya sağlayıcıya özel kota penceresi.</summary>
public sealed record RateWindow
{
    /// <summary>session | weekly | tertiary | provider-specific</summary>
    [JsonPropertyName("kind")] public required string Kind { get; init; }
    [JsonPropertyName("label")] public string? Label { get; init; }
    [JsonPropertyName("usedPercent")] public double? UsedPercent { get; init; }
    [JsonPropertyName("remainingPercent")] public double? RemainingPercent { get; init; }
    [JsonPropertyName("resetAt")] public DateTimeOffset? ResetAt { get; init; }
}

public sealed record CreditInfo
{
    [JsonPropertyName("remaining")] public double? Remaining { get; init; }
    [JsonPropertyName("unit")] public string? Unit { get; init; }
}

public sealed record CostInfo
{
    [JsonPropertyName("todayUSD")] public double? TodayUsd { get; init; }
    [JsonPropertyName("last30DaysUSD")] public double? Last30DaysUsd { get; init; }
}

/// <summary>UI ipuçları: sıralama ve renklendirme. Görev çubuğu yüzeyi bunları kullanır.</summary>
public sealed record DisplayHints
{
    [JsonPropertyName("accentColor")] public string? AccentColor { get; init; }
    [JsonPropertyName("sortKey")] public int SortKey { get; init; }
    /// <summary>low | normal | high</summary>
    [JsonPropertyName("priority")] public string? Priority { get; init; }
}

public sealed record ProviderError
{
    [JsonPropertyName("code")] public string? Code { get; init; }
    [JsonPropertyName("message")] public string? Message { get; init; }
}
