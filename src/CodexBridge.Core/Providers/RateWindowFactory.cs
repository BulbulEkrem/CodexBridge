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

    /// <summary>
    /// Geri sayımı saat:dakika biçiminde verir — <c>5:13</c> = 5 saat 13 dakika.
    ///
    /// <para>Band'ın pill'i için. Oradaki metin 9.5pt ve satır zaten yüzdeleri taşıyor;
    /// <see cref="FormatCountdown"/>'ın <c>5s 13dk</c> biçimi iki kat yer kaplıyor. Amaç tek
    /// bakışta kaç dakika kaldığını okumak, o yüzden dakika HER ZAMAN iki hane
    /// (<c>0:07</c>, <c>0:09</c>) — tek haneli yazarsak <c>0:7</c> okunurken duraksatır.</para>
    ///
    /// <para>Saat taşmıyor: 26 saat kalmışsa <c>26:04</c> yazar, güne çevirmez. Bu biçim
    /// oturum penceresi için kullanılıyor (birkaç saat); haftalık pencerede kullanılırsa
    /// sayı büyür ama yanlış olmaz.</para>
    ///
    /// <para>Sıfırlanma zamanı bilinmiyorsa <c>null</c> — çağıran segmenti tamamen atlamalı,
    /// yer tutucu yazmamalı.</para>
    /// </summary>
    public static string? FormatClockCountdown(DateTimeOffset? resetAt, DateTimeOffset now)
    {
        if (resetAt is not { } reset) return null;

        var left = reset - now;
        if (left <= TimeSpan.Zero) return "0:00";

        // Saniyeleri yukarı yuvarla: 4dk 30sn kalmışken "0:04" yazıp kullanıcıyı erken
        // rahatlatmaktansa "0:05" demek daha dürüst.
        int totalMinutes = (int)Math.Ceiling(left.TotalMinutes);
        return $"{totalMinutes / 60}:{totalMinutes % 60:D2}";
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
