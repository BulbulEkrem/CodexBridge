using System.Runtime.InteropServices;
using CodexBridge.Core.Dashboard;
using CodexBridge.Core.Providers;
using CodexBridge.Core.Rendering;
using CodexBridge.Taskbar.Runtime;
using static CodexBridge.Taskbar.Interop.NativeMethods;

namespace CodexBridge.Taskbar.Tray;

/// <summary>
/// Bildirim alanı (tepsi) ikonu — band'ın <b>yedek yüzeyi</b>.
///
/// <para>Neden band varken buna da ihtiyaç var: görev çubuğuna parent'lama desteklenmeyen bir
/// teknik. Microsoft bir gün engellerse, çubuk ortalıyken yer kalmazsa ya da otomatik gizleme
/// açıksa band görünmez olur. Tepsi ikonu resmî API ve hep orada. Mimari risk tablomuzdaki
/// (<c>00-MIMARI</c> §10) azaltma tam olarak budur.</para>
///
/// <para>İkon 32×32 çizilir: üstte oturum, altta haftalık — band'daki iki çubukla aynı sıra
/// ve aynı renk kuralı. Araç ipucu 128 karaktere sığdırılır.</para>
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const string WindowClassName = "CodexBridge.TrayIcon";

    // Sabit GUID: ikonun yeri Explorer yeniden başlasa da kullanıcı taşısa da korunur.
    private static readonly Guid IconGuid = new("7b3a1f42-9c65-4b18-9d0e-2f5a6c81d3ee");

    private const uint MenuRefresh = 1;
    private const uint MenuSettings = 2;
    private const uint MenuQuit = 3;

    // LOAD BEARING: delegate alanda tutulmalı; GC toplarsa marshal edilmiş işaretçi çöker.
    private readonly WndProcDelegate _wndProc;
    private readonly AppHost _host;
    private readonly uint _taskbarCreatedMessage;

    private IntPtr _hwnd;
    private IntPtr _currentIcon;
    private bool _registered;
    private DashboardSnapshot? _snapshot;

    /// <summary>Kullanıcı bağlam menüsünden ayarları istedi.</summary>
    public event Action? OpenSettingsRequested;

    /// <summary>Kullanıcı çıkışı istedi.</summary>
    public event Action? QuitRequested;

    private static bool _menuThemeApplied;

    public TrayIcon(AppHost host)
    {
        _host = host;
        _wndProc = WndProc;
        _taskbarCreatedMessage = RegisterWindowMessageW("TaskbarCreated");
    }

    public void Show()
    {
        EnsureWindow();
        if (_hwnd == IntPtr.Zero) return;

        _snapshot = _host.Coordinator.Current ?? _host.Store.Read();
        AddOrModify(NIM_ADD);

        // Sürüm 4 geri çağrı sözleşmesi: olay kodu lParam'ın alt yarısında gelir.
        var version = NewData();
        version.uVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIconW(NIM_SETVERSION, ref version);

        _host.Updated += OnSnapshot;
    }

    /// <summary>Explorer yeniden başladıktan sonra ikonu tekrar kaydeder. Tepsi ikonları
    /// Explorer ile birlikte kaybolur; <c>TaskbarCreated</c> yayınında geri eklenmeleri gerekir.</summary>
    /// <summary>
    /// Explorer yeniden başladıktan sonra ikonu kabuğa geri koyar.
    ///
    /// <para>Tek başına <c>NIM_ADD</c> YETMİYOR — canlı testte üretildi: ikon görünüyor ama
    /// tıklamalar hiçbir yere gitmiyor. İki sebebi var:</para>
    ///
    /// <list type="number">
    ///   <item>İkonu <c>NIF_GUID</c> ile kaydediyoruz. Kabukta o GUID'e ait ESKİ kayıt hâlâ
    ///   duruyor; üstüne <c>NIM_ADD</c> denemek başarısız oluyor ve görünen ikon kabuğun
    ///   bayat girdisi oluyor — bizim penceremize bağlı değil. Önce <c>NIM_DELETE</c> ile
    ///   o girdiyi temizlemek gerekiyor.</item>
    ///
    ///   <item><c>NIM_SETVERSION</c> yeniden gönderilmezse sürüm 4 geri çağrı sözleşmesi
    ///   kurulmuyor; <c>NIF_SHOWTIP</c> ve olay kodunun lParam'ın alt yarısında gelmesi
    ///   buna bağlı.</item>
    /// </list>
    ///
    /// <para><c>NIM_DELETE</c>'in başarısız olması normal (silinecek kayıt olmayabilir);
    /// dönüş değerine bakılmıyor.</para>
    /// </summary>
    public void Reregister()
    {
        if (_hwnd == IntPtr.Zero) return;

        var stale = NewData();
        stale.uFlags = NIF_GUID;
        Shell_NotifyIconW(NIM_DELETE, ref stale);

        _registered = false;
        AddOrModify(NIM_ADD);

        var version = NewData();
        version.uVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIconW(NIM_SETVERSION, ref version);
    }

    private void OnSnapshot(DashboardSnapshot snapshot)
    {
        _snapshot = snapshot;
        AddOrModify(NIM_MODIFY);
    }

    // ---- Win32 ----

    private void EnsureWindow()
    {
        if (_hwnd != IntPtr.Zero) return;

        IntPtr instance = GetModuleHandleW(null);
        var wc = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = instance,
            lpszClassName = WindowClassName,
        };
        RegisterClassExW(ref wc);

        // Mesaj penceresi: görünmez, görev çubuğu düğmesi yok.
        _hwnd = CreateWindowExW(
            WS_EX_TOOLWINDOW, WindowClassName, "CodexBridge Tray", WS_OVERLAPPED,
            0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
    }

    private NOTIFYICONDATAW NewData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _hwnd,
        uID = 1,
        guidItem = IconGuid,
        szTip = string.Empty,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private void AddOrModify(uint message)
    {
        if (_hwnd == IntPtr.Zero) return;

        IntPtr newIcon = BuildIcon();
        string tip = TrayTooltip.Build(_snapshot, _host.Settings, DateTimeOffset.UtcNow);

        var data = NewData();
        data.uFlags = NIF_ICON | NIF_TIP | NIF_MESSAGE | NIF_GUID | NIF_SHOWTIP;
        data.uCallbackMessage = WM_TRAYCALLBACK;
        data.hIcon = newIcon;
        // Sınır aşılırsa yazım bozulur; TrayTooltip zaten sığdırıyor, burada da kelepçele.
        data.szTip = tip.Length > TrayTooltip.MaxLength ? tip[..TrayTooltip.MaxLength] : tip;

        bool ok = Shell_NotifyIconW(message, ref data);
        if (!ok && message == NIM_MODIFY)
        {
            // Explorer yeniden başlamış olabilir: eklemeyi dene.
            ok = Shell_NotifyIconW(NIM_ADD, ref data);
        }
        _registered = _registered || ok;

        // Eski ikonu ancak yenisi yerine geçtikten sonra bırak.
        if (_currentIcon != IntPtr.Zero && _currentIcon != newIcon) DestroyIcon(_currentIcon);
        _currentIcon = newIcon;
    }

    private IntPtr BuildIcon()
    {
        var settings = _host.Settings;

        // İkon tek sağlayıcıyı gösterebilir: en kısıtlayıcı olanı seç.
        ProviderRow? worstRow = _snapshot?.Providers
            .Where(p => settings.IsEnabled(p.Id))
            .OrderByDescending(p => p.MostRestrictive()?.UsedPercent ?? -1)
            .FirstOrDefault();

        double? session = worstRow?.Window(WindowKinds.Session)?.UsedPercent;
        double? weekly = worstRow?.Window(WindowKinds.Weekly)?.UsedPercent;
        bool dimmed = worstRow?.Error is not null || worstRow is null;

        byte[] pixels = TrayIconRenderer.Render(
            session, weekly,
            TrayIconRenderer.LevelFor(session, settings.WarnPercent, settings.CritPercent),
            TrayIconRenderer.LevelFor(weekly, settings.WarnPercent, settings.CritPercent),
            dimmed);

        return IconFactory.FromBgra(pixels);
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == _taskbarCreatedMessage)
        {
            Reregister();
            return IntPtr.Zero;
        }

        if (msg == WM_TRAYCALLBACK)
        {
            uint evt = (uint)(lParam.ToInt64() & 0xFFFF);
            if (evt is WM_CONTEXTMENU or WM_RBUTTONUP)
            {
                ShowContextMenu();
            }
            else if (evt is NIN_SELECT or NIN_KEYSELECT or WM_LBUTTONUP)
            {
                // Sol tık = "kullanıcı baktı": yenilemeyi hızlandır.
                _host.Coordinator.NoteInteraction();
                _host.Coordinator.RequestRefresh();
            }
            return IntPtr.Zero;
        }

        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    /// <summary>Menülerin sistem temasını izlemesini sağlar. Bkz. NativeMethods.Tray'deki
    /// ordinal notu — belgelenmemiş API olduğu için başarısızlığı sessizce yutuluyor,
    /// tek kaybı menünün açık temada çizilmesi.</summary>
    private static void EnsureMenuTheme()
    {
        if (_menuThemeApplied) return;
        _menuThemeApplied = true;

        try
        {
            SetPreferredAppMode(PreferredAppModeAllowDark);
            FlushMenuThemes();
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            // Ordinal kaymış ya da uxtheme yok: menü eski görünümüyle çalışmaya devam eder.
        }
    }

    private void ShowContextMenu()
    {
        EnsureMenuTheme();

        IntPtr menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;

        try
        {
            AppendMenuW(menu, MF_STRING, MenuRefresh, "Şimdi yenile");
            AppendMenuW(menu, MF_STRING, MenuSettings, "Ayarlar…");
            AppendMenuW(menu, MF_SEPARATOR, UIntPtr.Zero, null);
            AppendMenuW(menu, MF_STRING, MenuQuit, "Çıkış");

            // Menünün dışına tıklanınca kapanması için pencere ön plana alınmalı.
            SetForegroundWindow(_hwnd);
            GetCursorPos(out POINT pt);

            int choice = TrackPopupMenuEx(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD, pt.X, pt.Y, _hwnd, IntPtr.Zero);
            switch ((uint)choice)
            {
                case MenuRefresh:
                    _host.Coordinator.NoteInteraction();
                    _host.Coordinator.RequestRefresh();
                    break;
                case MenuSettings:
                    OpenSettingsRequested?.Invoke();
                    break;
                case MenuQuit:
                    QuitRequested?.Invoke();
                    break;
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        _host.Updated -= OnSnapshot;

        if (_registered)
        {
            var data = NewData();
            data.uFlags = NIF_GUID;
            Shell_NotifyIconW(NIM_DELETE, ref data);
            _registered = false;
        }

        if (_currentIcon != IntPtr.Zero)
        {
            DestroyIcon(_currentIcon);
            _currentIcon = IntPtr.Zero;
        }

        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }
}
