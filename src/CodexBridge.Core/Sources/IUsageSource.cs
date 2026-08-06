using CodexBridge.Core.Dashboard;

namespace CodexBridge.Core.Sources;

/// <summary>
/// Yüzeyin (görev çubuğu, host) veri kaynağı soyutlaması. Faz sırasına göre farklı
/// implementasyonlar takılır:
///   Faz 1 → <see cref="FakeUsageSource"/> (sahte veri)
///   Faz 2 → Win-CodexBar `serve` (/usage,/cost) sarmalayıcı adaptörü
///   Faz 5 → kendi sağlayıcı katmanımız (ClearScript JS host + C# HTTP stratejileri)
/// Yüzey ve host bu arayüzü görür; kaynağın ne olduğunu bilmez.
/// </summary>
public interface IUsageSource
{
    /// <summary>Güncel dashboard/v1 snapshot'ını döndürür.</summary>
    Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken ct = default);
}
