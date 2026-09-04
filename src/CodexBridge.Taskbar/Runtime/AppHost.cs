using CodexBridge.Core.Dashboard;
using CodexBridge.Core.Providers;
using CodexBridge.Core.Refresh;
using CodexBridge.Core.Security;
using CodexBridge.Core.Settings;
using CodexBridge.Core.Sources;

namespace CodexBridge.Taskbar.Runtime;

/// <summary>
/// Uygulamanın kurulum kökü. <b>Tek yenileme noktası</b> burada yaşar: band, tepsi ikonu,
/// bildirim ve (varsa) widget hep bu koordinatörün ürettiği snapshot'ı okur. Yüzeyler
/// sağlayıcıya kendileri gitmez — aksi halde aynı kotayı yüzey sayısı kadar tüketirdik.
/// </summary>
public sealed class AppHost : IDisposable
{
    private readonly HttpClient _http = ProviderFactory.CreateHttpClient();
    private readonly CancellationTokenSource _cts = new();
    private readonly OAuthTokenCache _tokens = new(new FileSecretStore());
    private Task? _loop;

    public AppHost()
    {
        Settings = AppSettings.Load();
        Store = new SnapshotStore();

        _aggregate = new AggregateUsageSource(
            ProviderFactory.CreateEnabled(Settings, _http, _tokens),
            version: "codexbridge-" + AppVersion);

        Coordinator = new RefreshCoordinator(
            _aggregate, Store, () => Settings, new WindowsRefreshSignals());

        Coordinator.Updated += s => Updated?.Invoke(s);
    }

    private AggregateUsageSource _aggregate;

    public const string AppVersion = "0.3";

    /// <summary>Geçerli ayarlar. <see cref="ReloadSettings"/> ile tazelenir.</summary>
    public AppSettings Settings { get; private set; }

    public RefreshCoordinator Coordinator { get; }

    public SnapshotStore Store { get; }

    /// <summary>Her başarılı yenilemede tetiklenir. Yüzeyler buna abone olur.</summary>
    public event Action<DashboardSnapshot>? Updated;

    /// <summary>Ayarlar penceresi kaydettiğinde çağrılır: sağlayıcı listesi değişmiş olabilir,
    /// kaynakları yeniden kurup hemen bir yenileme iste.</summary>
    public void ReloadSettings()
    {
        Settings = AppSettings.Load();
        _aggregate = new AggregateUsageSource(
            ProviderFactory.CreateEnabled(Settings, _http, _tokens),
            version: "codexbridge-" + AppVersion);

        // Yeni toplayıcının son-bilinen-değer sözlüğü boş doğuyor. Devretmezsek ayar kaydetmek
        // tüm değerleri siliyor ve ardından gelen ilk hatalı çekimde pill'ler "—" oluyor.
        _aggregate.SeedLastGood(Coordinator.Current ?? Store.Read());

        Coordinator.ReplaceSource(_aggregate);
        Coordinator.RequestRefresh();
    }

    /// <summary>Yenileme döngüsünü başlatır. Diskte önceki snapshot varsa yüzeyler onunla
    /// hemen dolar; ilk canlı çekim arkadan gelir.</summary>
    public void Start()
    {
        if (Store.Read() is { } cached)
        {
            // Sadece göstermek yetmiyor: toplayıcı da bu değerleri bilmeli. Bilmezse açılıştaki
            // ilk çekim patladığında (ör. sağlayıcı 429) boş hata satırı üretir ve diskteki
            // sağlam snapshot'ın üzerine yazar — kullanıcı sayının yaşlandığını değil, yok
            // olduğunu görür.
            _aggregate.SeedLastGood(cached);
            Updated?.Invoke(cached);
        }

        _loop = Coordinator.RunAsync(_cts.Token);
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _loop?.Wait(TimeSpan.FromSeconds(2)); } catch { /* kapanışta bekleme kritik değil */ }
        _cts.Dispose();
        _http.Dispose();
    }
}
