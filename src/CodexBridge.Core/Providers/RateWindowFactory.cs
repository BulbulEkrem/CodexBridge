using CodexBridge.Core.Dashboard;

namespace CodexBridge.Core.Providers;

/// <summary>Kota penceresi üretimi ve okunması için ortak yardımcılar.</summary>
public static class RateWindowFactory
{
    /// <summary>Yüzdeyi 0–100'e kelepçeleyip tek ondalığa yuvarlayarak pencere kurar.</summary>
    public static RateWindow Create(string kind, string label, double usedPercent, DateTimeOffset? resetAt)
    {
        double used = Math.Round(Math.Clamp(usedPercent, 0, 100), 1);
        return new RateWindow
        {
            Kind = kind,
            Label = label,
            UsedPercent = used,
            RemainingPercent = Math.Round(100 - used, 1),
            ResetAt = resetAt,
        };
    }

    /// <summary>Süreyi kısa Türkçe geri sayıma çevirir: <c>2s 58dk</c>, <c>3g 4sa</c>, <c>12dk</c>.</summary>
    public static string? FormatCountdown(DateTimeOffset? resetAt, DateTimeOffset now)
    {
        if (resetAt is not { } reset) return null;
        var left = reset - now;
        if (left <= TimeSpan.Zero) return "şimdi";
        if (left.TotalDays >= 1) return $"{(int)left.TotalDays}g {left.Hours}sa";
        if (left.TotalHours >= 1) return $"{(int)left.TotalHours}s {left.Minutes}dk";
        return $"{Math.Max(1, (int)left.TotalMinutes)}dk";
    }
}

/// <summary>Sağlayıcı satırı üzerinde yüzeylerin ortak sorduğu sorular.</summary>
public static class ProviderRowExtensions
{
    /// <summary>Verilen türde pencereyi bulur; yoksa <c>null</c>.</summary>
    public static RateWindow? Window(this ProviderRow row, string kind) =>
        row.Windows.FirstOrDefault(w => string.Equals(w.Kind, kind, StringComparison.Ordinal));

    /// <summary>
    /// <b>En kısıtlayıcı</b> pencere: kullanımı en yüksek olan. Band'ın pill rengi ve
    /// tepsi ikonunun alarm seviyesi buradan gelir — Opus kotan bitmişken band'ın
    /// "%18" gösterip susmasını engelleyen kural budur.
    /// </summary>
    public static RateWindow? MostRestrictive(this ProviderRow row) =>
        row.Windows.Count == 0 ? null : row.Windows.MaxBy(w => w.UsedPercent ?? 0);

    /// <summary>Satırın alarm seviyesi: en kısıtlayıcı pencereye göre.</summary>
    public static StatusLevel Level(this ProviderRow row, double warnAt, double critAt)
    {
        if (row.Error is not null) return StatusLevel.Unknown;
        double used = row.MostRestrictive()?.UsedPercent ?? 0;
        return used >= critAt ? StatusLevel.Critical
            : used >= warnAt ? StatusLevel.Warning
            : StatusLevel.Ok;
    }
}
