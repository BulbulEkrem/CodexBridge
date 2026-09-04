using CodexBridge.Core.Dashboard;
using CodexBridge.Core.Providers;
using CodexBridge.Core.Settings;
using CodexBridge.Taskbar.Runtime;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace CodexBridge.Taskbar;

/// <summary>
/// Minimal ayar penceresi. Yüzey değil ama olmazsa uygulama <b>yapılandırılamaz</b>:
/// sağlayıcı açma/kapama, eşikler, bildirim ve kimlik durumu buradan görünür.
///
/// <para>XAML dosyası yok — içerik kodla kuruluyor. Tek pencerelik bir form için
/// XAML + code-behind ikilisi gereksiz yük.</para>
/// </summary>
public sealed class SettingsWindow : Window
{
    private readonly AppHost _host;
    private readonly Dictionary<string, CheckBox> _providerBoxes = [];
    private readonly Dictionary<string, TextBlock> _providerStatus = [];

    private readonly NumberBox _warn = NewNumber(1, 99);
    private readonly NumberBox _crit = NewNumber(1, 100);
    private readonly NumberBox _minRefresh = NewNumber(60, 3600);
    private readonly NumberBox _maxRefresh = NewNumber(60, 21600);
    private readonly CheckBox _notifications = new() { Content = "Eşik bildirimleri gönder" };
    private readonly CheckBox _band = new() { Content = "Görev çubuğu bandını göster" };
    private readonly CheckBox _tray = new() { Content = "Tepsi ikonunu göster" };
    private readonly CheckBox _autostart = new() { Content = "Windows açılışında başlat" };
    private readonly TextBlock _message = new() { Opacity = 0.75, TextWrapping = TextWrapping.Wrap };

    public SettingsWindow(AppHost host)
    {
        _host = host;
        Title = "CodexBridge Ayarları";

        AppWindow.Resize(new SizeInt32(520, 700));
        if (AppWindow.Presenter is OverlappedPresenter p) p.IsMaximizable = false;

        Content = BuildUi();
        LoadFromSettings();

        _host.Updated += OnSnapshot;
        Closed += (_, _) => _host.Updated -= OnSnapshot;

        // Ayarlar açmak da bir etkileşim sinyali.
        _host.Coordinator.NoteInteraction();
    }

    private UIElement BuildUi()
    {
        var root = new StackPanel { Spacing = 18, Padding = new Thickness(24) };

        root.Children.Add(Header("Sağlayıcılar"));
        var providers = new StackPanel { Spacing = 10 };
        foreach (string id in ProviderIds.All)
        {
            var box = new CheckBox { Content = ProviderIds.DisplayName(id) };
            var status = new TextBlock { FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
            _providerBoxes[id] = box;
            _providerStatus[id] = status;

            var group = new StackPanel { Spacing = 2 };
            group.Children.Add(box);
            group.Children.Add(status);
            providers.Children.Add(group);
        }
        root.Children.Add(providers);
        root.Children.Add(Note(
            "Kota verisi yereldeki CLI oturumundan okunur; API anahtarı gerekmez. " +
            "Bu istekler ölçüm okumadır — aboneliğinin kotasından düşmez."));

        root.Children.Add(Header("Eşikler"));
        root.Children.Add(LabeledRow("Uyarı eşiği (%)", _warn));
        root.Children.Add(LabeledRow("Kritik eşik (%)", _crit));
        root.Children.Add(Note("Band'ın pill rengi ve tepsi ikonu bu eşiklere göre değişir."));

        root.Children.Add(Header("Yenileme"));
        root.Children.Add(LabeledRow("En sık (saniye)", _minRefresh));
        root.Children.Add(LabeledRow("En seyrek (saniye)", _maxRefresh));
        root.Children.Add(Note(
            "Aralık kullanıma göre kendiliğinden ayarlanır: sen bilgisayar başındayken sık, " +
            "boştayken seyrek. 60 saniyenin altına inilmez."));

        root.Children.Add(Header("Yüzeyler"));
        root.Children.Add(_band);
        root.Children.Add(_tray);
        root.Children.Add(Note("Tepsi ikonu band'ın yedeğidir; kapatman önerilmez."));
        root.Children.Add(_notifications);
        root.Children.Add(_autostart);

        var save = new Button { Content = "Kaydet", Style = null };
        save.Click += (_, _) => Save();

        var refresh = new Button { Content = "Şimdi yenile" };
        refresh.Click += (_, _) =>
        {
            _host.Coordinator.NoteInteraction();
            _host.Coordinator.RequestRefresh();
            _message.Text = "Yenileme istendi.";
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        buttons.Children.Add(save);
        buttons.Children.Add(refresh);
        root.Children.Add(buttons);
        root.Children.Add(_message);

        return new ScrollViewer { Content = root };
    }

    private void LoadFromSettings()
    {
        var s = _host.Settings;
        foreach (var (id, box) in _providerBoxes) box.IsChecked = s.IsEnabled(id);

        _warn.Value = s.WarnPercent;
        _crit.Value = s.CritPercent;
        _minRefresh.Value = s.MinRefreshSeconds;
        _maxRefresh.Value = s.MaxRefreshSeconds;
        _notifications.IsChecked = s.NotificationsEnabled;
        _band.IsChecked = s.BandEnabled;
        _tray.IsChecked = s.TrayIconEnabled;
        _autostart.IsChecked = Autostart.IsEnabled();

        UpdateProviderStatus(_host.Coordinator.Current ?? _host.Store.Read());
    }

    private void OnSnapshot(DashboardSnapshot snapshot) =>
        DispatcherQueue.TryEnqueue(() => UpdateProviderStatus(snapshot));

    /// <summary>Her sağlayıcının kimlik/hata durumunu gösterir. Kullanıcının "neden veri yok?"
    /// sorusunun cevabı burada olmalı — sessiz boş satır değil.</summary>
    private void UpdateProviderStatus(DashboardSnapshot? snapshot)
    {
        foreach (var (id, label) in _providerStatus)
        {
            var row = snapshot?.Providers.FirstOrDefault(p => p.Id == id);
            if (row is null)
            {
                label.Text = "Henüz veri çekilmedi.";
                continue;
            }

            if (row.Error is { } err)
            {
                label.Text = row.Windows.Count > 0
                    ? $"⚠ {err.Message} (son bilinen değer gösteriliyor)"
                    : $"⚠ {err.Message}";
                continue;
            }

            var worst = row.MostRestrictive();
            string plan = row.Identity?.Plan is { } pl ? $" · {pl}" : "";
            string reset = RateWindowFactory.FormatCountdown(worst?.ResetAt, DateTimeOffset.UtcNow) is { } c
                ? $" · {c}" : "";
            label.Text = worst is null
                ? $"Bağlı{plan} · aktif pencere yok"
                : $"Bağlı{plan} · en kısıtlayıcı %{(int)Math.Round(worst.UsedPercent ?? 0)}{reset}";
        }
    }

    private void Save()
    {
        var enabled = _providerBoxes.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToArray();

        var updated = _host.Settings with
        {
            EnabledProviders = enabled,
            WarnPercent = _warn.Value,
            CritPercent = _crit.Value,
            MinRefreshSeconds = (int)_minRefresh.Value,
            MaxRefreshSeconds = (int)_maxRefresh.Value,
            NotificationsEnabled = _notifications.IsChecked == true,
            BandEnabled = _band.IsChecked == true,
            TrayIconEnabled = _tray.IsChecked == true,
            StartAtLogin = _autostart.IsChecked == true,
        };

        updated.Save();

        bool autostartOk = Autostart.Set(updated.StartAtLogin);
        _host.ReloadSettings();
        LoadFromSettings();

        _message.Text = autostartOk
            ? "Kaydedildi."
            : "Kaydedildi, ancak otomatik başlatma kaydı yazılamadı.";
    }

    // ---- Küçük UI yardımcıları ----

    private static NumberBox NewNumber(double min, double max) => new()
    {
        Minimum = min,
        Maximum = max,
        SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        Width = 140,
        SmallChange = 1,
        LargeChange = 10,
    };

    private static TextBlock Header(string text) => new()
    {
        Text = text,
        FontSize = 16,
        FontWeight = FontWeights.SemiBold,
    };

    private static TextBlock Note(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Opacity = 0.65,
        TextWrapping = TextWrapping.Wrap,
    };

    private static StackPanel LabeledRow(string label, FrameworkElement control)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        row.Children.Add(new TextBlock { Text = label, Width = 180, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(control);
        return row;
    }
}
