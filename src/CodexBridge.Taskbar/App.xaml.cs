using CodexBridge.Taskbar.Hud;
using CodexBridge.Taskbar.Notifications;
using CodexBridge.Taskbar.Runtime;
using CodexBridge.Taskbar.Tray;
using CodexBridge.Taskbar.Taskbar;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace CodexBridge.Taskbar;

/// <summary>
/// Uygulama girişi. Tek yenileme noktasını (<see cref="AppHost"/>), band penceresini,
/// tepsi ikonunu ve Explorer-restart gözcüsünü sahiplenir.
///
/// <para>Gözcü tetiklendiğinde band penceresi SIFIRDAN yeniden kurulur — Deskband11'in
/// çözemediği kısım budur. Tepsi ikonu bu sırada hiç etkilenmez; band'ın tekniği bir gün
/// bozulursa tek yüzey olarak o kalır.</para>
/// </summary>
public partial class App : Application
{
    private AppHost? _host;
    private TaskbarWatchdog? _watchdog;
    private MainWindow? _band;
    private HudWindow? _hud;
    private TrayIcon? _tray;
    private NotificationService? _notifications;
    private SettingsWindow? _settings;
    private DispatcherQueue? _uiQueue;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _uiQueue = DispatcherQueue.GetForCurrentThread();

        // Bildirimlerin doğru ad ve ikonla görünmesi için kimlik ilk iş kaydedilmeli.
        AppIdentity.Apply();

        _host = new AppHost();

        if (_host.Settings.BandEnabled) CreateBand();
        if (_host.Settings.TrayIconEnabled) CreateTray();
        if (_host.Settings.HudEnabled) CreateHud();

        _notifications = new NotificationService(_host);
        _notifications.OpenSettingsRequested += ShowSettings;
        _notifications.Start();

        // Kabuk kayıtları bilerek BURADA: Register() aynı AUMID anahtarını yeniden yazıyor,
        // önce yazsaydık ikonumuz düşerdi (bkz. AppIdentity.WriteShellRegistration).
        AppIdentity.WriteShellRegistration();

        // Explorer-restart hayatta kalma: gözcü UI iş parçacığında oluşturulur ki mesajları
        // ana mesaj döngüsünde alsın; tetiklenince band'ı UI iş parçacığında yeniden kurarız.
        _watchdog = new TaskbarWatchdog();
        _watchdog.TaskbarRecreated += OnTaskbarRecreated;
        _watchdog.Start();

        _host.Start();
    }

    private void CreateBand()
    {
        _band = new MainWindow(_host!);
        _band.Activate();
        _band.AttachToTaskbar();
    }

    /// <summary>Yüzen HUD. Band'dan bağımsız yaşıyor: Explorer yeniden başladığında band
    /// sıfırdan kurulurken HUD hiç etkilenmiyor, çünkü görev çubuğuna parent'lanmış değil.</summary>
    private void CreateHud()
    {
        _hud = new HudWindow(_host!);
        _hud.Activate();
    }

    private void CreateTray()
    {
        _tray = new TrayIcon(_host!);
        _tray.OpenSettingsRequested += ShowSettings;
        _tray.QuitRequested += Shutdown;
        _tray.Show();
    }

    private void ShowSettings()
    {
        _uiQueue?.TryEnqueue(() =>
        {
            if (_settings is not null)
            {
                _settings.Activate();
                return;
            }
            _settings = new SettingsWindow(_host!);
            _settings.Closed += (_, _) => _settings = null;
            _settings.Activate();
        });
    }

    private void Shutdown()
    {
        _uiQueue?.TryEnqueue(() =>
        {
            _notifications?.Dispose();
            _hud?.Detach();
            _tray?.Dispose();
            _watchdog?.Dispose();
            _host?.Dispose();
            Exit();
        });
    }

    private void OnTaskbarRecreated()
    {
        _uiQueue?.TryEnqueue(() =>
        {
            // ÖNEMLİ: eski band penceresine Close() ÇAĞIRMA. Explorer öldüğünde Shell_TrayWnd
            // ile birlikte band'ın HWND'si zaten yok edildi; ölmüş bir WinUI penceresini
            // kapatmak yakalanamayan native segfault'a yol açar (canlı testte tespit edildi).
            // Referansı bırak, çöp toplayıcıya bırak, doğrudan yenisini kur.
            _band = null;

            // Tepsi ikonu da Explorer ile birlikte gitti; onu da yeniden kaydet.
            _tray?.Reregister();

            // Yeni görev çubuğu tam hazır olsun diye küçük bir gecikmeyle yeniden kur.
            var timer = _uiQueue!.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(500);
            timer.IsRepeating = false;
            timer.Tick += (s, e) =>
            {
                if (_host!.Settings.BandEnabled) CreateBand();
            };
            timer.Start();
        });
    }
}
