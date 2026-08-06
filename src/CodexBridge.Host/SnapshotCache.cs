using CodexBridge.Core.Dashboard;
using CodexBridge.Core.Sources;

namespace CodexBridge.Host;

/// <summary>
/// Snapshot'ı <c>refresh-interval</c> saniye önbelleğe alır ve eşzamanlı önbellek-ıskalarını
/// tek bir fetch'te birleştirir (coalescing). Telefon istemcileri asla doğrudan sağlayıcıya
/// gitmesin diye host tek yenileme noktasıdır.
/// </summary>
public sealed class SnapshotCache(IUsageSource source, TimeSpan ttl)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DashboardSnapshot? _cached;
    private DateTimeOffset _fetchedAt = DateTimeOffset.MinValue;

    public async Task<DashboardSnapshot> GetAsync(CancellationToken ct = default)
    {
        if (_cached is { } fresh && DateTimeOffset.UtcNow - _fetchedAt < ttl)
            return fresh;

        await _gate.WaitAsync(ct);
        try
        {
            // Bekleyiş sırasında başka biri doldurmuş olabilir.
            if (_cached is { } again && DateTimeOffset.UtcNow - _fetchedAt < ttl)
                return again;

            var snapshot = await source.GetSnapshotAsync(ct);
            _cached = snapshot;
            _fetchedAt = DateTimeOffset.UtcNow;
            return snapshot;
        }
        finally
        {
            _gate.Release();
        }
    }
}
