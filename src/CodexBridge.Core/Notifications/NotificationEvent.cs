namespace CodexBridge.Core.Notifications;

/// <summary>
/// Faz 7: host'un telefona iteceği tek bir bildirim olayı. Platformsuz (Core) — APNs/FCM
/// gövdesine bu modelden çevrilir. <see cref="DedupeKey"/> aynı durumun tekrar tekrar
/// gönderilmesini engellemek için kullanılır (cooldown penceresi bununla anahtarlanır).
/// </summary>
public sealed record NotificationEvent
{
    public required string ProviderId { get; init; }
    public required string ProviderName { get; init; }
    public required NotificationKind Kind { get; init; }

    /// <summary>İlgili kota penceresi (session/weekly/tertiary); geçerliyse.</summary>
    public string? WindowKind { get; init; }
    /// <summary>Olay anındaki kullanım yüzdesi (kota olayları için).</summary>
    public double? UsedPercent { get; init; }

    public required string Title { get; init; }
    public required string Body { get; init; }

    /// <summary>Aynı durumu tekilleştiren anahtar (provider + kind + window).</summary>
    public required string DedupeKey { get; init; }
    public DateTimeOffset OccurredAt { get; init; }

    /// <summary>Telefon istemcisinin doğrudan ilgili satıra atlaması için taşınır.</summary>
    public string ProviderIdPayload => ProviderId;
}

/// <summary>Bildirim türü. JSON'da küçük harfli sözleşme değerlerine eşlenir.</summary>
public enum NotificationKind
{
    /// <summary>Kullanım uyarı eşiğini yukarı geçti.</summary>
    QuotaWarning,
    /// <summary>Kullanım kritik eşiği yukarı geçti.</summary>
    QuotaCritical,
    /// <summary>Kota sıfırlandı / kullanım toparlandı.</summary>
    QuotaReset,
    /// <summary>Sağlayıcı hataya düştü (önceden sağlıklıydı).</summary>
    ProviderError,
    /// <summary>Sağlayıcı hatadan çıktı.</summary>
    ProviderRecovered,
}

/// <summary>Eşik yapılandırması. Üst akışın uyarı/kritik bandlarıyla uyumlu varsayılanlar.</summary>
public sealed record NotificationThresholds
{
    public double WarningPercent { get; init; } = 75;
    public double CriticalPercent { get; init; } = 90;
    /// <summary>Bu yüzdenin altına düşünce "toparlandı/sıfırlandı" sayılır (histerezis).</summary>
    public double RecoverBelowPercent { get; init; } = 50;

    public static readonly NotificationThresholds Default = new();
}
