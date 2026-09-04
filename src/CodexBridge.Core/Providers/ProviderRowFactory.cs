using CodexBridge.Core.Dashboard;

namespace CodexBridge.Core.Providers;

/// <summary>Sağlayıcı satırlarını tek yerden kurar; kimlik/renk/sıra meta verisi
/// <see cref="ProviderIds"/>'den gelir, sağlayıcı modülleri tekrar etmez.</summary>
public static class ProviderRowFactory
{
    /// <summary>Varsayılan uyarı eşiği (%).</summary>
    public const double DefaultWarnPercent = 75;

    /// <summary>Varsayılan kritik eşik (%).</summary>
    public const double DefaultCritPercent = 90;

    public static ProviderRow Create(
        string providerId,
        string source,
        IReadOnlyList<RateWindow> windows,
        DateTimeOffset now,
        string? plan = null,
        string? accountEmail = null,
        double warnAt = DefaultWarnPercent,
        double critAt = DefaultCritPercent)
    {
        var row = new ProviderRow
        {
            Id = providerId,
            Name = ProviderIds.DisplayName(providerId),
            Enabled = true,
            Source = source,
            Identity = plan is null && accountEmail is null
                ? null
                : new ProviderIdentity { Plan = plan, AccountEmail = accountEmail },
            Windows = windows,
            Display = new DisplayHints
            {
                AccentColor = ProviderIds.AccentColor(providerId),
                SortKey = ProviderIds.SortKey(providerId),
                Priority = "normal",
            },
            UpdatedAt = now,
        };

        return row with
        {
            Status = new ProviderStatus { Level = row.Level(warnAt, critAt), UpdatedAt = now },
        };
    }

    /// <summary>Çekim başarısız olduğunda gösterilecek hata satırı. Kota penceresi taşımaz.</summary>
    public static ProviderRow CreateError(string providerId, ProviderErrorKind kind, string message, DateTimeOffset now) =>
        new()
        {
            Id = providerId,
            Name = ProviderIds.DisplayName(providerId),
            Enabled = true,
            Source = null,
            Status = new ProviderStatus { Level = StatusLevel.Unknown, UpdatedAt = now },
            Windows = [],
            Display = new DisplayHints
            {
                AccentColor = ProviderIds.AccentColor(providerId),
                SortKey = ProviderIds.SortKey(providerId),
                Priority = "normal",
            },
            Error = new ProviderError { Code = kind.ToString(), Message = message },
            UpdatedAt = now,
        };
}
