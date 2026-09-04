using System.Runtime.InteropServices;
using CodexBridge.Core.Dashboard;
using CodexBridge.Core.Providers;
using CodexBridge.Core.Settings;
using CodexBridge.Taskbar.Interop;
using CodexBridge.Taskbar.Runtime;
using CodexBridge.Taskbar.Taskbar;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using static CodexBridge.Taskbar.Interop.NativeMethods;

namespace CodexBridge.Taskbar;

/// <summary>
/// Görev çubuğu bandı — <b>B varyantı</b>: sağlayıcı başına <b>tek pill</b>, içinde iki çubuk
/// (üst oturum, alt haftalık). Pill'in arka plan rengi <b>en kısıtlayıcı</b> pencereden gelir,
/// ilk pencereden değil: Codex'in haftalığı %91 iken band'ın oturum %18'i gösterip susmasını
/// engelleyen kural budur.
///
/// <para>Dört ayrı pill (A varyantı) yerine bunun seçilmesinin sebebi genişlik: görev çubuğu
/// ortalıyken soldaki boş alan pencere sayısına göre daralıyor ve A ilk kırpılan olurdu.</para>
///
/// <para>Veri kendi başına çekilmez; <see cref="AppHost"/>'un tek yenileme noktasından gelir.</para>
/// </summary>
public sealed partial class MainWindow : Window
{
    /// <summary>Pill başına ayrılan yaklaşık genişlik (dip). Band genişliği buradan hesaplanır.</summary>
    private const int PillWidthDips = 104;
    private const int BandLeftOffsetDips = 12;
    private const double BarTrackWidth = 38;

    private static readonly Color ColorOk = Color.FromArgb(0xFF, 0x6C, 0xCB, 0x5F);
    private static readonly Color ColorWarn = Color.FromArgb(0xFF, 0xFC, 0xE1, 0x00);
    private static readonly Color ColorCrit = Color.FromArgb(0xFF, 0xFF, 0x5B, 0x5B);
    private static readonly Color ColorUnknown = Color.FromArgb(0xFF, 0x9A, 0x9A, 0x9A);

    private readonly AppHost _host;
    private readonly IntPtr _hwnd;
    private readonly DispatcherQueue _queue;

    // LOAD BEARING: subclass delegate'ini alanda tut, yoksa GC toplar ve WndProc çöker.
    private readonly WndProcDelegate _subclassProc;
    private readonly IntPtr _originalProc;

    private int _bandWidthDips = PillWidthDips * 2 + BandLeftOffsetDips;

    public MainWindow(AppHost host)
    {
        _host = host;
        InitializeComponent();

        // Çubuğa oturacağı için pencere kromu istemiyoruz: başlık çubuğu + min/büyüt/kapat
        // düğmeleri tamamen kaldırılır. ExtendsContentIntoTitleBar TEK BAŞINA yetmiyor —
        // canlı testte tespit edildi; presenter ayarı ve Win32 stil sıyırma da gerekiyor.
        ExtendsContentIntoTitleBar = true;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _queue = DispatcherQueue.GetForCurrentThread();

        // WndProc subclass: çözünürlük/DPI/çalışma alanı değişince yeniden konumlan.
        _subclassProc = SubclassProc;
        _originalProc = SetWindowLongPtrW(_hwnd, GWLP_WNDPROC,
            Marshal.GetFunctionPointerForDelegate(_subclassProc));

        // Band'a tıklamak "kullanıcı yüzeyle etkileşti" sinyali: bir sonraki yenileme kısalır.
        Bands.PointerPressed += OnBandPressed;

        _host.Updated += OnSnapshot;
        if (_host.Coordinator.Current is { } current) Render(current);
    }

    /// <summary>Band'ı görev çubuğuna parent'lar. <see cref="App"/> tarafından çağrılır.</summary>
    public void AttachToTaskbar() => TaskbarHost.MoveToTaskbar(_hwnd, BandLeftOffsetDips, _bandWidthDips);

    private void OnBandPressed(object sender, PointerRoutedEventArgs e)
    {
        _host.Coordinator.NoteInteraction();
        _host.Coordinator.RequestRefresh();
    }

    private void OnSnapshot(DashboardSnapshot snapshot) => _queue.TryEnqueue(() => Render(snapshot));

    private IntPtr SubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_DISPLAYCHANGE)
        {
            _queue.TryEnqueue(AttachToTaskbar);
        }
        else if (msg == WM_SETTINGCHANGE && (uint)wParam == SPI_SETWORKAREA)
        {
            _queue.TryEnqueue(AttachToTaskbar);
        }
        return CallWindowProcW(_originalProc, hWnd, msg, wParam, lParam);
    }

    // ---- Çizim ----

    private void Render(DashboardSnapshot snapshot)
    {
        var settings = _host.Settings;
        var rows = snapshot.Providers
            .Where(p => settings.IsEnabled(p.Id))
            .OrderBy(p => p.Display?.SortKey ?? int.MaxValue)
            .ToList();

        Bands.Children.Clear();
        foreach (var row in rows) Bands.Children.Add(BuildPill(row, settings));

        // Veri bayatladıysa tüm band soluklaşır — "gösterdiğim sayı eski" demenin en sessiz yolu.
        bool stale = DateTimeOffset.UtcNow - snapshot.GeneratedAt
            > TimeSpan.FromSeconds(Math.Max(60, snapshot.StaleAfterSeconds));
        Bands.Opacity = stale ? 0.55 : 1.0;

        // Sağlayıcı sayısı değiştiyse band genişliğini güncelle ve yeniden konumlan.
        int wanted = Math.Max(1, rows.Count) * PillWidthDips + BandLeftOffsetDips;
        if (wanted != _bandWidthDips)
        {
            _bandWidthDips = wanted;
            AttachToTaskbar();
        }
    }

    private static Border BuildPill(ProviderRow row, AppSettings settings)
    {
        var session = row.Window(WindowKinds.Session);
        var weekly = row.Window(WindowKinds.Weekly);
        var worst = row.MostRestrictive();

        bool hasData = row.Windows.Count > 0;
        Color accent = hasData
            ? SeverityColor(worst?.UsedPercent ?? 0, settings)
            : ColorUnknown;

        var name = new TextBlock
        {
            Text = row.Name,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
            LineHeight = 15,
        };

        var numbers = new TextBlock
        {
            Text = hasData ? FormatNumbers(session, weekly) : "—",
            FontSize = 9.5,
            Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
            LineHeight = 12,
        };

        var labels = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(name);
        labels.Children.Add(numbers);

        var bars = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 3,
            VerticalAlignment = VerticalAlignment.Center,
            Width = BarTrackWidth,
        };
        bars.Children.Add(BuildBar(session?.UsedPercent, settings, hasData));
        bars.Children.Add(BuildBar(weekly?.UsedPercent, settings, hasData));

        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        content.Children.Add(labels);
        content.Children.Add(bars);

        var pill = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x33, accent.R, accent.G, accent.B)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(9, 4, 9, 4),
            Child = content,
            VerticalAlignment = VerticalAlignment.Center,
        };

        // Hata varsa son bilinen değer gösterilmeye devam eder ama araç ipucunda sebebi yazar.
        ToolTipService.SetToolTip(pill, BuildTooltip(row, session, weekly));
        return pill;
    }

    private static Border BuildBar(double? percent, AppSettings settings, bool hasData)
    {
        double pct = Math.Clamp(percent ?? 0, 0, 100);
        Color color = hasData && percent is not null ? SeverityColor(pct, settings) : ColorUnknown;

        var fill = new Border
        {
            Height = 4,
            Width = Math.Max(2, BarTrackWidth * pct / 100.0),
            Background = new SolidColorBrush(color),
            CornerRadius = new CornerRadius(2),
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        return new Border
        {
            Height = 4,
            Width = BarTrackWidth,
            Background = new SolidColorBrush(Color.FromArgb(0x2B, 0xFF, 0xFF, 0xFF)),
            CornerRadius = new CornerRadius(2),
            Child = fill,
        };
    }

    /// <summary>Dar alanda yüzde işareti gürültü; iki sayı orta noktayla ayrılıyor: <c>47 · 78</c>.</summary>
    private static string FormatNumbers(RateWindow? session, RateWindow? weekly)
    {
        string s = session?.UsedPercent is { } a ? ((int)Math.Round(a)).ToString() : "–";
        string w = weekly?.UsedPercent is { } b ? ((int)Math.Round(b)).ToString() : "–";
        return $"{s} · {w}";
    }

    private static string BuildTooltip(ProviderRow row, RateWindow? session, RateWindow? weekly)
    {
        var now = DateTimeOffset.UtcNow;
        var lines = new List<string> { row.Name + (row.Identity?.Plan is { } p ? $" · {p}" : "") };

        foreach (var (label, w) in new[] { ("Oturum", session), ("Haftalık", weekly) })
        {
            if (w is null) continue;
            string reset = RateWindowFactory.FormatCountdown(w.ResetAt, now) is { } c ? $" · {c}" : "";
            lines.Add($"{label} %{w.UsedPercent}{reset}");
        }

        if (row.Error is { } err) lines.Add("⚠ " + err.Message);
        return string.Join('\n', lines);
    }

    private static Color SeverityColor(double usedPercent, AppSettings settings) =>
        usedPercent >= settings.CritPercent ? ColorCrit
        : usedPercent >= settings.WarnPercent ? ColorWarn
        : ColorOk;
}
