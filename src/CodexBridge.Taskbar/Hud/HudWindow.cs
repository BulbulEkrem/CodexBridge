using CodexBridge.Core.Dashboard;
using CodexBridge.Core.Providers;
using CodexBridge.Core.Settings;
using CodexBridge.Taskbar.Runtime;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;
using static CodexBridge.Taskbar.Interop.NativeMethods;

namespace CodexBridge.Taskbar.Hud;

/// <summary>
/// Yüzen ambient HUD — <b>D4 yerleşimi</b>: sağlayıcı başına bir pill, pill'de saatlik ve
/// haftalık pencere ayrı satırlarda; her satırın kendi yüzdesi, kendi geri sayımı ve tam
/// genişlikte kendi barı var.
///
/// <para><b>Band'dan farkı ve neden ayrı bir pencere:</b> D4 iki satır artık 76 dip yükseklik
/// istiyor, görev çubuğu ise 48 px. Bu yerleşim çubuğa fiziksel olarak sığmıyor; ancak
/// serbest duran bir pencerede yaşayabilir. Band olduğu gibi duruyor, bu ek bir yüzey.</para>
///
/// <para><b>Ekranı ayırmaz:</b> AppBar (<c>SHAppBarMessage</c>) çalışma alanını rezerve edip
/// diğer pencereleri iter; bu sıradan bir top-level pencere, hiçbir şey rezerve etmez.</para>
///
/// <para><b>Marka rengi logoda, dolgu rengi durumda.</b> Bugünkü band'da pill'in dolgusu
/// sağlayıcının rengini taşıyor ve durum yalnızca ince çubuklardan okunuyor. Burada logo
/// markayı taşıdığı için dolgu serbest kaldı: eşiği geçen pill'in tamamı sararıyor.</para>
/// </summary>
public sealed class HudWindow : Window
{
    private const int PillWidthDips = 198;
    private const int BarWidthDips = 96;
    private const int LogoSizeDips = 92;
    private const int LogoBleedDips = 30;

    private static readonly Color ColorOk = Color.FromArgb(0xFF, 0x6C, 0xCB, 0x5F);
    private static readonly Color ColorWarn = Color.FromArgb(0xFF, 0xFC, 0xE1, 0x00);
    private static readonly Color ColorCrit = Color.FromArgb(0xFF, 0xFF, 0x5B, 0x5B);
    private static readonly Color ColorUnknown = Color.FromArgb(0xFF, 0x9A, 0x9A, 0x9A);

    private readonly AppHost _host;
    private readonly IntPtr _hwnd;
    private readonly DispatcherQueue _queue;
    private readonly StackPanel _pills;
    private readonly DispatcherQueueTimer _tick;

    private bool _dragging;
    private int _dragDx, _dragDy;

    private DashboardSnapshot? _last;

    public HudWindow(AppHost host)
    {
        _host = host;
        Title = "CodexBridge";

        // Kök Grid'in Transparent olması yetmiyor; backdrop atanmamış WinUI penceresi opak
        // siyah boyuyor (band'da canlı testte görüldü).
        SystemBackdrop = new WinUIEx.TransparentTintBackdrop();

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _queue = DispatcherQueue.GetForCurrentThread();

        StripChrome();

        _pills = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var root = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xC2, 0x1A, 0x18, 0x1C)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x21, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(6),
            Child = _pills,
        };
        Content = root;

        // Sürükleme XAML pointer olaylarıyla yürütülüyor. WM_NCHITTEST -> HTCAPTION yolu
        // WinUI'da işlemiyor: fare girdisi XAML adasında tüketiliyor ve üst-seviye pencerenin
        // isabet testine hiç ulaşmıyor (canlı testte doğrulandı — pencere kımıldamadı).
        root.PointerPressed += OnPointerPressed;
        root.PointerMoved += OnPointerMoved;
        root.PointerReleased += OnPointerReleased;
        root.PointerCaptureLost += (_, _) => _dragging = false;

        _host.Updated += OnSnapshot;
        if (_host.Coordinator.Current is { } current) Render(current);
        else if (_host.Store.Read() is { } cached) Render(cached);

        Resize();
        Place();

        // Geri sayım yenileme döngüsüne binmiyor: aralık 30 dakikaya açılabildiği için saat
        // o kadar bayat kalırdı. Aynı tik bayatlık soluklaşmasını da tazeliyor.
        _tick = _queue.CreateTimer();
        _tick.Interval = TimeSpan.FromSeconds(20);
        _tick.IsRepeating = true;
        _tick.Tick += OnTick;
        _tick.Start();
    }

    /// <summary>
    /// Pencere kromunu ve görev çubuğu/Alt-Tab varlığını sıyırır.
    ///
    /// <para><c>WS_EX_TOOLWINDOW</c> HUD'ı Alt-Tab listesinden ve görev çubuğu düğmelerinden
    /// çıkarıyor — sürekli açık duran bir gösterge oraya girmemeli. <c>WS_EX_APPWINDOW</c>
    /// varsa TOOLWINDOW'u ezdiği için ayrıca sıyrılıyor.</para>
    /// </summary>
    private void StripChrome()
    {
        int style = GetWindowLongW(_hwnd, GWL_STYLE);
        SetWindowLongW(_hwnd, GWL_STYLE, style & ~WS_CHROME);

        int ex = GetWindowLongW(_hwnd, GWL_EXSTYLE);
        SetWindowLongW(_hwnd, GWL_EXSTYLE, (ex | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW);
    }

    private double Scale => GetDpiForWindow(_hwnd) is var dpi && dpi > 0 ? dpi / 96.0 : 1.0;

    /// <summary>Pencereyi içeriğe göre boyutlandırır. İçerik sabit genişlikte olduğu için
    /// ölçü hesapla çıkarılıyor; XAML'in kendi ölçümünü beklemek ilk karede titreme yaratıyor.</summary>
    private void Resize()
    {
        int rows = Math.Max(1, _pills.Children.Count);
        int width = (int)((rows * PillWidthDips + (rows - 1) * 6 + 14) * Scale);
        int height = (int)(88 * Scale);
        AppWindow.Resize(new SizeInt32(width, height));
    }

    /// <summary>
    /// Kaydedilmiş konuma, yoksa çalışma alanının sağ altına yerleştirir.
    ///
    /// <para>Kaydedilmiş konum sanal ekranın dışında kalıyorsa (monitör sökülmüş, çözünürlük
    /// düşmüş) yok sayılıyor — aksi halde HUD görünmez bir yerde açılır ve kullanıcı onu geri
    /// getiremez.</para>
    ///
    /// <para><b>Görünürlük sınaması neden Win32 ile:</b> ilk sürüm <c>DisplayArea.FindAll()</c>
    /// kullanıyordu ve kaydedilmiş konumla açılan ilk çalıştırmada süreç
    /// <c>Microsoft.UI.Xaml.dll</c> içinde fail-fast ile çöktü (canlı testte 0xc000027b).
    /// Pencere daha gösterilmemişken o API'ye dokunmak güvenli değil. Sanal ekran ölçüleri
    /// aynı soruyu WinUI'a hiç girmeden cevaplıyor.</para>
    ///
    /// <para>Tüm gövde korumalı: yerleştirme başarısız olursa HUD varsayılan köşede açılır,
    /// pencere hiç açılmamasındansa yanlış köşede açılması yeğdir.</para>
    /// </summary>
    private void Place()
    {
        try
        {
            var size = AppWindow.Size;
            var s = _host.Settings;

            if (s.HudLeft is { } left && s.HudTop is { } top && IsOnVirtualScreen(left, top, size))
            {
                AppWindow.Move(new PointInt32(left, top));
                return;
            }

            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            int margin = (int)(16 * Scale);
            AppWindow.Move(new PointInt32(
                area.X + area.Width - size.Width - margin,
                area.Y + area.Height - size.Height - margin));
        }
        catch (Exception)
        {
            // Konumlandırılamadı; pencere WinUI'ın verdiği yerde kalır.
        }
    }

    /// <summary>Pencerenin bir kısmı sanal ekranla kesişiyor mu. Tamamen dışarıdaysa
    /// kaydedilmiş konum kullanılamaz.</summary>
    private static bool IsOnVirtualScreen(int x, int y, SizeInt32 size)
    {
        int vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int vw = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        if (vw <= 0 || vh <= 0) return false;

        return x < vx + vw && x + size.Width > vx
            && y < vy + vh && y + size.Height > vy;
    }

    private void OnSnapshot(DashboardSnapshot snapshot) => _queue.TryEnqueue(() => Render(snapshot));

    /// <summary>Ölü pencerenin ağacına dokunmak yakalanamayan çökme demek; tik önce
    /// tanıtıcıyı doğrulayıp kendini durduruyor.</summary>
    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        if (!IsWindow(_hwnd))
        {
            sender.Stop();
            return;
        }
        if (_last is { } snapshot) Render(snapshot);
    }

    // ---- Sürükleme ----

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not UIElement root) return;

        GetCursorPos(out var cursor);
        var pos = AppWindow.Position;
        _dragDx = cursor.X - pos.X;
        _dragDy = cursor.Y - pos.Y;
        _dragging = root.CapturePointer(e.Pointer);

        // Kullanıcı yüzeyle etkileşti: bir sonraki yenileme kısalsın.
        _host.Coordinator.NoteInteraction();
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;

        // Ekran koordinatı imleçten okunuyor: XAML'in verdiği konum pencereye göreli ve
        // pencere hareket ettikçe kayar, imleç ise mutlak.
        GetCursorPos(out var cursor);
        AppWindow.Move(new PointInt32(cursor.X - _dragDx, cursor.Y - _dragDy));
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        if (sender is UIElement root) root.ReleasePointerCapture(e.Pointer);
        SavePosition();
    }

    private void SavePosition()
    {
        try
        {
            var p = AppWindow.Position;
            _host.UpdateHudPosition(p.X, p.Y);
        }
        catch (Exception)
        {
            // Konum kaydedilemezse HUD çalışmaya devam eder, sadece sonraki açılışta
            // varsayılan köşeye döner.
        }
    }

    // ---- Çizim ----

    private void Render(DashboardSnapshot snapshot)
    {
        _last = snapshot;

        var settings = _host.Settings;
        var rows = snapshot.Providers
            .Where(p => settings.IsEnabled(p.Id))
            .OrderBy(p => p.Display?.SortKey ?? int.MaxValue)
            .ToList();

        int before = _pills.Children.Count;
        _pills.Children.Clear();
        foreach (var row in rows) _pills.Children.Add(BuildPill(row, settings, Scale));

        bool stale = DateTimeOffset.UtcNow - snapshot.GeneratedAt
            > TimeSpan.FromSeconds(Math.Max(60, snapshot.StaleAfterSeconds));
        if (Content is Border root) root.Opacity = stale ? 0.6 : 1.0;

        if (_pills.Children.Count != before) { Resize(); Place(); }
    }

    private static Border BuildPill(ProviderRow row, AppSettings settings, double scale)
    {
        var session = row.Window(WindowKinds.Session);
        var weekly = row.Window(WindowKinds.Weekly);
        bool hasData = row.Windows.Count > 0;

        // Dolgu en kısıtlayıcı pencerenin durumundan geliyor: pill'in tamamı uyarıyor.
        Color severity = hasData
            ? SeverityColor(row.MostRestrictive()?.UsedPercent ?? 0, settings)
            : ColorUnknown;

        var body = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(LogoSizeDips - LogoBleedDips + 4, 9, 13, 9),
        };

        if (hasData)
        {
            body.Children.Add(BuildWindowRow(session, settings));
            body.Children.Add(BuildWindowRow(weekly, settings));
        }
        else
        {
            body.Children.Add(new TextBlock
            {
                Text = row.Error?.Code is { } code ? code : "—",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
            });
        }

        var content = new Grid { Width = PillWidthDips };

        if (BrandMarks.SvgFor(row.Id, BrandColor(row)) is { } svg)
        {
            var logo = new Image
            {
                Width = LogoSizeDips,
                Height = LogoSizeDips,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(-LogoBleedDips, 0, 0, 0),
                Opacity = 0.85,
                Stretch = Stretch.Uniform,
            };
            content.Children.Add(logo);

            // Yükleme asenkron: görüntü hazır olunca yerine oturuyor. Beklemiyoruz ki
            // pencere ilk karede zaten çizilsin.
            _ = LoadLogoAsync(logo, svg, (int)(LogoSizeDips * scale));
        }

        content.Children.Add(body);

        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x2B, severity.R, severity.G, severity.B)),
            CornerRadius = new CornerRadius(10),
            Child = content,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    /// <summary>Tek pencere satırı: üstte yüzde ve geri sayım iki uca, altta tam genişlik bar.
    /// Bar 96 dip — 52 dip'te %78 ile %83 gözle ayırt edilemiyor, burada ediliyor.</summary>
    private static StackPanel BuildWindowRow(RateWindow? window, AppSettings settings)
    {
        double? percent = window?.UsedPercent;
        Color color = percent is { } p ? SeverityColor(p, settings) : ColorUnknown;

        var head = new Grid { Width = BarWidthDips };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var pct = new TextBlock
        {
            Text = percent is { } v ? $"%{(int)Math.Round(v)}" : "%–",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
            LineHeight = 15,
        };
        Grid.SetColumn(pct, 0);
        head.Children.Add(pct);

        var left = new TextBlock
        {
            Text = RateWindowFactory.FormatWindowCountdown(window?.ResetAt, DateTimeOffset.UtcNow) ?? "",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromArgb(0xA6, 0xFF, 0xFF, 0xFF)),
            HorizontalAlignment = HorizontalAlignment.Right,
            LineHeight = 15,
        };
        Grid.SetColumn(left, 1);
        head.Children.Add(left);

        double pctClamped = Math.Clamp(percent ?? 0, 0, 100);
        var track = new Border
        {
            Width = BarWidthDips,
            Height = 5,
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            Child = new Border
            {
                Height = 5,
                Width = percent is null ? 0 : Math.Max(3, BarWidthDips * pctClamped / 100.0),
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(color),
                HorizontalAlignment = HorizontalAlignment.Left,
            },
        };

        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 3 };
        stack.Children.Add(head);
        stack.Children.Add(track);
        return stack;
    }

    private static async Task LoadLogoAsync(Image target, string svg, int pixelSize)
    {
        if (await BrandMarks.LoadAsync(svg, pixelSize) is { } source) target.Source = source;
    }

    /// <summary>Logo rengi sağlayıcının kendi marka rengi. Claude'unki snapshot'taki
    /// <c>accentColor</c> ile aynı; OpenAI'ın işareti tek renk beyaz.</summary>
    private static Color BrandColor(ProviderRow row) =>
        row.Id == ProviderIds.Claude
            ? Color.FromArgb(0xFF, 0xD9, 0x77, 0x57)
            : Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);

    private static Color SeverityColor(double usedPercent, AppSettings settings) =>
        usedPercent >= settings.CritPercent ? ColorCrit
        : usedPercent >= settings.WarnPercent ? ColorWarn
        : ColorOk;

    public void Detach() => _host.Updated -= OnSnapshot;
}
