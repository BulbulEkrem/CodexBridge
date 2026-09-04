using CodexBridge.Core.Dashboard;
using CodexBridge.Core.Settings;
using CodexBridge.Core.Sources;

namespace CodexBridge.Core.Refresh;

/// <summary>Platform katmanının sağladığı yenileme sinyalleri. Varsayılan uygulama hiçbir
/// şey bilmez; Windows tarafı pil durumu ve yerel ajan etkinliğini doldurur.</summary>
public interface IRefreshSignals
{
    /// <summary>Pil tasarrufu veya termal kısıtlama var mı.</summary>
    bool LowPowerOrThermalPressure { get; }

    /// <summary>Son 5 dakikada yerel bir kodlama ajanı çalıştı mı (kota hızlı hareket ediyor).</summary>
    bool LocalAgentActivityWithin5Min { get; }
}

/// <summary>Sinyal sağlayamayan ortamlar için nötr uygulama.</summary>
public sealed class NoRefreshSignals : IRefreshSignals
{
    public static readonly NoRefreshSignals Instance = new();
    public bool LowPowerOrThermalPressure => false;
    public bool LocalAgentActivityWithin5Min => false;
}

/// <summary>
/// <b>Tek yenileme noktası.</b> Sağlayıcılara giden tek taraf budur; band, tepsi, bildirim,
/// widget ve telefon hep bunun ürettiği snapshot'ı okur. Amaç aynı kotayı süreç sayısı kadar
/// tüketmemek.
///
/// <para>Aralığı <see cref="AdaptiveRefresh"/> belirler (2–30 dk), ayarlardaki alt/üst sınıra
/// kelepçelenir. Kullanıcı yüzeyle etkileşince <see cref="NoteInteraction"/> çağrılır ve
/// bir sonraki tur kısalır.</para>
/// </summary>
public sealed class RefreshCoordinator(
    IUsageSource source,
    SnapshotStore store,
    Func<AppSettings> settings,
    IRefreshSignals? signals = null,
    TimeProvider? clock = null)
{
    private readonly IRefreshSignals _signals = signals ?? NoRefreshSignals.Instance;
    private volatile IUsageSource _source = source;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly SemaphoreSlim _fetchGate = new(1, 1);

    private DateTimeOffset? _lastInteraction;

    /// <summary>Son başarılı snapshot. Henüz çekim yapılmadıysa <c>null</c>.</summary>
    public DashboardSnapshot? Current { get; private set; }

    /// <summary>Her başarılı yenilemede tetiklenir. Yüzeyler buna abone olur.</summary>
    public event Action<DashboardSnapshot>? Updated;

    /// <summary>Veri kaynağını değiştirir. Ayarlar penceresinde sağlayıcı listesi değişince
    /// çağrılır; döngü durdurulmadan yeni kaynağa geçilir.</summary>
    public void ReplaceSource(IUsageSource replacement) => _source = replacement;

    /// <summary>Kullanıcı yüzeyle etkileşti (band'a tıkladı, önizlemeye baktı, ayarları açtı).
    /// Bir sonraki yenileme aralığını kısaltır.</summary>
    public void NoteInteraction() => _lastInteraction = _clock.GetUtcNow();

    /// <summary>Bekleyen turu iptal edip hemen yenileme yapılmasını ister.</summary>
    public void RequestRefresh()
    {
        // Zaten sinyalliyse ikinci kez artırma (kapasite 1).
        try { _wake.Release(); } catch (SemaphoreFullException) { /* zaten uyanık */ }
    }

    /// <summary>Bir sonraki yenilemeye kadar beklenecek süre.</summary>
    public TimeSpan NextDelay()
    {
        var s = settings().Normalized();
        var decided = AdaptiveRefresh.Decide(new RefreshContext
        {
            LowPowerOrThermalPressure = _signals.LowPowerOrThermalPressure,
            SinceLastInteraction = _lastInteraction is { } t ? _clock.GetUtcNow() - t : null,
            LocalAgentActivityWithin5Min = _signals.LocalAgentActivityWithin5Min,
        });

        var min = TimeSpan.FromSeconds(s.MinRefreshSeconds);
        var max = TimeSpan.FromSeconds(s.MaxRefreshSeconds);
        return decided < min ? min : decided > max ? max : decided;
    }

    /// <summary>Şimdi bir çekim yapar, snapshot'ı diske yazar ve aboneleri uyarır.
    /// Eşzamanlı çağrılar tek çekimde birleşir.</summary>
    public async Task<DashboardSnapshot> RefreshNowAsync(CancellationToken ct = default)
    {
        await _fetchGate.WaitAsync(ct);
        try
        {
            var snapshot = await _source.GetSnapshotAsync(ct);
            snapshot = snapshot with
            {
                Host = snapshot.Host with { RefreshIntervalSeconds = (int)NextDelay().TotalSeconds },
            };

            Current = snapshot;
            try { store.Write(snapshot); }
            catch (IOException) { /* disk yazımı yüzeyleri düşürmemeli; bellekteki snapshot geçerli */ }

            Updated?.Invoke(snapshot);
            return snapshot;
        }
        finally
        {
            _fetchGate.Release();
        }
    }

    /// <summary>Yenileme döngüsü. İptal edilene kadar çalışır.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RefreshNowAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                // Toplayıcı sağlayıcı hatalarını zaten yutuyor; buraya düşen ancak
                // beklenmedik bir şeydir ve döngüyü durdurmamalı.
            }

            try
            {
                // Uyanma sinyali gelirse beklemeyi kısa kes.
                await _wake.WaitAsync(NextDelay(), ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
