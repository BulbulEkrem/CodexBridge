using CodexBridge.Core.Dashboard;

namespace CodexBridge.Core.Providers;

/// <summary>
/// <b>Tek</b> sağlayıcının kullanım verisini çeker. <see cref="Sources.IUsageSource"/> tüm
/// snapshot'ı döndürürken bu arayüz tek satır döndürür; toplama işini
/// <see cref="Sources.AggregateUsageSource"/> yapar. Böylece bir sağlayıcının hatası
/// diğerini düşürmez.
/// </summary>
/// <remarks>
/// Hata durumunda <see cref="ProviderSourceException"/> fırlatılır — toplayıcı bunu yakalayıp
/// satırı <c>error</c> alanıyla üretir.
/// </remarks>
public interface IProviderSource
{
    /// <summary><see cref="ProviderIds"/> sabitlerinden biri.</summary>
    string ProviderId { get; }

    /// <summary>Sağlayıcının güncel kota satırını çeker.</summary>
    Task<ProviderRow> FetchAsync(CancellationToken ct = default);
}
