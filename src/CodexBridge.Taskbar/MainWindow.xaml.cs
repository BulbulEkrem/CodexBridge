using System.Runtime.InteropServices;
using CodexBridge.Core.Dashboard;
using CodexBridge.Core.Sources;
using CodexBridge.Taskbar.Interop;
using CodexBridge.Taskbar.Taskbar;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using static CodexBridge.Taskbar.Interop.NativeMethods;

namespace CodexBridge.Taskbar;

/// <summary>
/// Görev çubuğu band'ı: sağlayıcı kullanım yüzdelerini gösteren yatay şerit.
/// Faz 1 — sahte veriyle çalışır. DPI/çözünürlük/çalışma alanı değişiminde kendini yeniden
/// konumlandırır (WndProc subclass). Explorer-restart hayatta kalmayı <see cref="App"/> + gözcü yönetir.
/// </summary>
public sealed partial class MainWindow : Window
{
    // Faz 1 geliştirme yenilemesi: sahte verinin oynadığını görmek için kısa. Gerçekte
    // CodexBridge.Core.Refresh.AdaptiveRefresh (2–30 dk) devreye girecek.
    private static readonly TimeSpan DevRefresh = TimeSpan.FromSeconds(5);

    private readonly IUsageSource _source;
    private readonly IntPtr _hwnd;
    private readonly DispatcherQueue _queue;
    private readonly DispatcherQueueTimer _refreshTimer;

    // LOAD BEARING: subclass delegate'ini alanda tut, yoksa GC toplar ve WndProc çöker.
    private readonly WndProcDelegate _subclassProc;
    private readonly IntPtr _originalProc;

    public MainWindow(IUsageSource source)
    {
        _source = source;
        InitializeComponent();

        // Çubuğa oturacağı için pencere kromu istemiyoruz.
        ExtendsContentIntoTitleBar = true;

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _queue = DispatcherQueue.GetForCurrentThread();

        // WndProc subclass: çözünürlük/DPI/çalışma alanı değişince yeniden konumlan.
        _subclassProc = SubclassProc;
        _originalProc = SetWindowLongPtrW(_hwnd, GWLP_WNDPROC,
            Marshal.GetFunctionPointerForDelegate(_subclassProc));

        _refreshTimer = _queue.CreateTimer();
        _refreshTimer.Interval = DevRefresh;
        _refreshTimer.Tick += async (s, e) => await RefreshAsync();
        _refreshTimer.Start();

        _ = RefreshAsync(); // ilk çekim hemen
    }

    /// <summary>Band'ı görev çubuğuna parent'lar. <see cref="App"/> tarafından çağrılır.</summary>
    public void AttachToTaskbar() => TaskbarHost.MoveToTaskbar(_hwnd);

    private IntPtr SubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_DISPLAYCHANGE)
        {
            _queue.TryEnqueue(() => TaskbarHost.MoveToTaskbar(_hwnd));
        }
        else if (msg == WM_SETTINGCHANGE && (uint)wParam == SPI_SETWORKAREA)
        {
            _queue.TryEnqueue(() => TaskbarHost.MoveToTaskbar(_hwnd));
        }
        return CallWindowProcW(_originalProc, hWnd, msg, wParam, lParam);
    }

    private async Task RefreshAsync()
    {
        DashboardSnapshot snapshot;
        try { snapshot = await _source.GetSnapshotAsync(); }
        catch { return; }

        Bands.Children.Clear();
        foreach (var p in snapshot.Providers.OrderBy(p => p.Display?.SortKey ?? 0))
        {
            var window = p.Windows.FirstOrDefault();
            double used = window?.UsedPercent ?? 0;
            Bands.Children.Add(BuildPill(p, used));
        }
    }

    private static Border BuildPill(ProviderRow p, double used)
    {
        Color accent = ParseColor(p.Display?.AccentColor) ?? Color.FromArgb(0xFF, 0x1E, 0x88, 0x9A);

        var text = new TextBlock
        {
            Text = $"{ShortName(p.Name)} {used:0}%",
            Foreground = new SolidColorBrush(Colors.White),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };

        // Doluluğu ima eden ince alt çizgi.
        var bar = new Border
        {
            Height = 3,
            Width = Math.Max(4, used / 100.0 * 46),
            Background = new SolidColorBrush(accent),
            CornerRadius = new CornerRadius(2),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 2),
        };

        var content = new Grid();
        content.Children.Add(text);
        content.Children.Add(bar);

        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x33, accent.R, accent.G, accent.B)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 2, 8, 2),
            Child = content,
        };
    }

    private static string ShortName(string name) => name.Length <= 6 ? name : name[..6];

    private static Color? ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        hex = hex.TrimStart('#');
        if (hex.Length != 6) return null;
        try
        {
            byte r = Convert.ToByte(hex.Substring(0, 2), 16);
            byte g = Convert.ToByte(hex.Substring(2, 2), 16);
            byte b = Convert.ToByte(hex.Substring(4, 2), 16);
            return Color.FromArgb(0xFF, r, g, b);
        }
        catch { return null; }
    }
}
