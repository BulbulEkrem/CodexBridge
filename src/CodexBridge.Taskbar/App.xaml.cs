using CodexBridge.Core.Sources;
using CodexBridge.Taskbar.Taskbar;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace CodexBridge.Taskbar;

/// <summary>
/// Uygulama girişi. Band penceresini ve Explorer-restart gözcüsünü sahiplenir.
/// Gözcü tetiklendiğinde band penceresini SIFIRDAN yeniden kurar (Deskband11'in yapamadığı).
/// </summary>
public partial class App : Application
{
    private readonly IUsageSource _source = CreateSource();
    private TaskbarWatchdog? _watchdog;

    // CODEXBRIDGE_HOST_URL ayarlıysa gerçek host'tan (dashboard/v1) oku, yoksa sahte veri.
    private static IUsageSource CreateSource()
    {
        var url = Environment.GetEnvironmentVariable("CODEXBRIDGE_HOST_URL");
        if (string.IsNullOrWhiteSpace(url)) return new FakeUsageSource();
        var token = Environment.GetEnvironmentVariable("CODEXBAR_DASHBOARD_TOKEN");
        return new HttpUsageSource(new HttpClient { Timeout = TimeSpan.FromSeconds(10) }, url, token);
    }
    private MainWindow? _band;
    private DispatcherQueue? _uiQueue;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _uiQueue = DispatcherQueue.GetForCurrentThread();

        CreateBand();

        // Explorer-restart hayatta kalma: gözcü UI iş parçacığında oluşturulur ki mesajları
        // ana mesaj döngüsünde alsın; tetiklenince band'ı UI iş parçacığında yeniden kurarız.
        _watchdog = new TaskbarWatchdog();
        _watchdog.TaskbarRecreated += OnTaskbarRecreated;
        _watchdog.Start();
    }

    private void CreateBand()
    {
        _band = new MainWindow(_source);
        _band.Activate();
        _band.AttachToTaskbar();
    }

    private void OnTaskbarRecreated()
    {
        // Gözcü UI iş parçacığında olduğundan doğrudan çalışabiliriz; yine de güvenli tarafta kalalım.
        _uiQueue?.TryEnqueue(() =>
        {
            try { _band?.Close(); } catch { /* eski pencere zaten ölmüş olabilir */ }
            _band = null;
            // Yeni görev çubuğu tam hazır olsun diye küçük bir gecikmeyle yeniden kur.
            var timer = _uiQueue!.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(500);
            timer.IsRepeating = false;
            timer.Tick += (s, e) => CreateBand();
            timer.Start();
        });
    }
}
