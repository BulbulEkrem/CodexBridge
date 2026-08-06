namespace CodexBridge.Core.Refresh;

/// <summary>
/// Üst akış CodexBar'ın <c>AdaptiveRefreshCore</c> karar tablosunun C# çevirisi
/// (<c>docs/refresh-loop.md</c>). Saf, bağımsız bir fonksiyon — hiçbir platform bağımlılığı yok.
/// Amaç: 67 sağlayıcı açıkken sabit 1 dk yenilemenin tetikleyeceği sağlayıcı-taraflı hız
/// limitlerinden kaçınmak. Değerler üst akışla birebir (2–30 dk).
/// </summary>
/// <remarks>
/// Görev çubuğu yüzeyine uyarlama: üst akıştaki "menü açıldı" kavramı bizde "kullanıcı yüzeyle
/// etkileşti" (band'a tıklama / popover açma) olur. Band daima görünür olduğundan, arka plan
/// yenilemesi yine de bütçeye tabidir.
/// </remarks>
public static class AdaptiveRefresh
{
    public static readonly TimeSpan Min = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan Max = TimeSpan.FromMinutes(30);

    /// <summary>Bir sonraki yenilemeye kadar beklenecek süreyi karar tablosuna göre döndürür.</summary>
    public static TimeSpan Decide(in RefreshContext ctx)
    {
        // Sıra önemli: ilk eşleşen kazanır (üst akış tablosu yukarıdan aşağı).
        if (ctx.LowPowerOrThermalPressure) return TimeSpan.FromMinutes(30);
        if (ctx.SinceLastInteraction is { } s)
        {
            if (s <= TimeSpan.FromMinutes(5)) return TimeSpan.FromMinutes(2);
            if (s <= TimeSpan.FromHours(1)) return TimeSpan.FromMinutes(5);
        }
        if (ctx.LocalAgentActivityWithin5Min) return TimeSpan.FromMinutes(5);
        if (ctx.SinceLastInteraction is { } s2 && s2 <= TimeSpan.FromHours(4))
            return TimeSpan.FromMinutes(15);
        // Hiç etkileşilmedi veya 4+ saat.
        return TimeSpan.FromMinutes(30);
    }
}

/// <summary>Yenileme kararının girdileri. Platform katmanı bu bağlamı doldurur.</summary>
public readonly record struct RefreshContext
{
    /// <summary>Düşük güç modu veya termal baskı var mı.</summary>
    public bool LowPowerOrThermalPressure { get; init; }
    /// <summary>Kullanıcının yüzeyle son etkileşiminden bu yana geçen süre; hiç etkileşmediyse null.</summary>
    public TimeSpan? SinceLastInteraction { get; init; }
    /// <summary>Yerel Codex/Claude etkinliği son 5 dakika içinde görüldü mü (log dosyaları).</summary>
    public bool LocalAgentActivityWithin5Min { get; init; }
}
