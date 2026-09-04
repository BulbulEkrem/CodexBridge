using System.Text.Json;
using System.Text.Json.Serialization;
using CodexBridge.Core.Providers;

namespace CodexBridge.Core.Settings;

/// <summary>
/// Kullanıcı ayarları (<c>%APPDATA%\CodexBridge\settings.json</c>).
/// Ayarlar pencereden yazılır, tüm yüzeyler buradan okur.
/// <b>Sır içermez</b> — token ve kimlik bilgisi <see cref="Security.ISecretStore"/>'da durur.
/// </summary>
public sealed record AppSettings
{
    /// <summary>Kota çekilen sağlayıcılar. Boşsa hiçbir istek atılmaz.</summary>
    [JsonPropertyName("enabledProviders")]
    public IReadOnlyList<string> EnabledProviders { get; init; } = ProviderIds.All;

    /// <summary>Sarı eşik (%). Bu değerin üstünde pill ve tepsi ikonu uyarı rengine geçer.</summary>
    [JsonPropertyName("warnPercent")]
    public double WarnPercent { get; init; } = ProviderRowFactory.DefaultWarnPercent;

    /// <summary>Kırmızı eşik (%).</summary>
    [JsonPropertyName("critPercent")]
    public double CritPercent { get; init; } = ProviderRowFactory.DefaultCritPercent;

    [JsonPropertyName("notificationsEnabled")]
    public bool NotificationsEnabled { get; init; } = true;

    /// <summary>Görev çubuğu bandı gösterilsin mi.</summary>
    [JsonPropertyName("bandEnabled")]
    public bool BandEnabled { get; init; } = true;

    /// <summary>Tepsi ikonu gösterilsin mi. Band çökerse tek yüzey bu kalır — kapatılması önerilmez.</summary>
    [JsonPropertyName("trayIconEnabled")]
    public bool TrayIconEnabled { get; init; } = true;

    [JsonPropertyName("startAtLogin")]
    public bool StartAtLogin { get; init; }

    /// <summary>Adaptif yenilemenin alt sınırı (sn). Uç noktanın hız sınırını korumak için
    /// 60 saniyenin altına inilmez.</summary>
    [JsonPropertyName("minRefreshSeconds")]
    public int MinRefreshSeconds { get; init; } = 120;

    /// <summary>Adaptif yenilemenin üst sınırı (sn).</summary>
    [JsonPropertyName("maxRefreshSeconds")]
    public int MaxRefreshSeconds { get; init; } = 1800;

    /// <summary>Aralık dışı ya da anlamsız değerleri güvenli aralığa çeker.</summary>
    public AppSettings Normalized()
    {
        double warn = Math.Clamp(WarnPercent, 1, 99);
        double crit = Math.Clamp(CritPercent, 1, 100);
        if (crit <= warn) crit = Math.Min(100, warn + 1);

        int min = Math.Clamp(MinRefreshSeconds, 60, 3600);
        int max = Math.Clamp(MaxRefreshSeconds, 60, 21600);
        if (max < min) max = min;

        var known = EnabledProviders
            .Where(p => ProviderIds.All.Contains(p, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return this with
        {
            WarnPercent = warn,
            CritPercent = crit,
            MinRefreshSeconds = min,
            MaxRefreshSeconds = max,
            EnabledProviders = known,
        };
    }

    public bool IsEnabled(string providerId) =>
        EnabledProviders.Contains(providerId, StringComparer.Ordinal);

    // ---- Kalıcılık ----

    public static AppSettings Load(string? path = null)
    {
        string file = path ?? AppPaths.SettingsFile;
        try
        {
            if (!File.Exists(file)) return new AppSettings().Normalized();
            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file), Json);
            return (loaded ?? new AppSettings()).Normalized();
        }
        catch (JsonException)
        {
            // Bozuk ayar dosyası uygulamayı açılmaz hale getirmemeli: varsayılana dön.
            return new AppSettings().Normalized();
        }
        catch (IOException)
        {
            return new AppSettings().Normalized();
        }
    }

    public void Save(string? path = null) =>
        AtomicFile.WriteAllText(path ?? AppPaths.SettingsFile,
            JsonSerializer.Serialize(Normalized(), Json));

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
