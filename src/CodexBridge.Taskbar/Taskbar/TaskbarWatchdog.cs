using System.Runtime.InteropServices;
using CodexBridge.Taskbar.Interop;
using static CodexBridge.Taskbar.Interop.NativeMethods;

namespace CodexBridge.Taskbar.Taskbar;

/// <summary>
/// Explorer-restart hayatta kalma mekanizması — bizim Deskband11 üzerine eklememiz.
///
/// Deskband11 bu noktada pes ediyor: band penceresi görev çubuğunun ÇOCUĞU olduğu için,
/// Explorer çöküp <c>Shell_TrayWnd</c> yok olduğunda band da yok oluyor (child, parent'la
/// birlikte ölür) ve WinUI XAML ağacı çöküyor. Deskband11'in yaptığı tek şey uygulamayı
/// kapatmak.
///
/// Çözümümüz: band penceresine GÜVENMEYEN, görev çubuğuna PARENT'LANMAMIŞ, üst-seviye gizli
/// bir "gözcü" pencere. Üst-seviye pencereler <c>TaskbarCreated</c> broadcast mesajını alır
/// (çocuk pencereler almaz). Explorer yeniden başlayıp yeni görev çubuğunu yarattığında bu
/// mesaj gelir; gözcü <see cref="TaskbarRecreated"/> olayını tetikler ve uygulama band'ı
/// SIFIRDAN yeniden kurup yeni çubuğa parent'lar.
///
/// Uygulama süreci canlı kaldığı için (yalnızca band penceresi ölür) bu yeniden kurulum mümkün.
/// </summary>
internal sealed class TaskbarWatchdog : IDisposable
{
    private const string ClassName = "CodexBridge.TaskbarWatchdog";

    // LOAD BEARING: delegate'i bir alanda tut. Aksi halde GC toplar ve WndProc'a marshal edilen
    // fonksiyon işaretçisi geçersizleşir (Deskband11'in _hotkeyWndProc notuyla aynı sebep).
    private readonly WndProcDelegate _wndProc;
    private readonly uint _wmTaskbarCreated;
    private IntPtr _hwnd;
    private bool _classRegistered;

    /// <summary>Explorer yeniden başlayıp yeni görev çubuğu oluştuğunda tetiklenir.</summary>
    public event Action? TaskbarRecreated;

    public TaskbarWatchdog()
    {
        _wndProc = WndProc;
        _wmTaskbarCreated = RegisterWindowMessageW("TaskbarCreated");
    }

    /// <summary>Gözcü pencereyi oluşturur ve dinlemeye başlar.</summary>
    public void Start()
    {
        if (_hwnd != IntPtr.Zero) return;

        IntPtr hInstance = GetModuleHandleW(null);
        var wc = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = hInstance,
            lpszClassName = ClassName,
        };
        // Zaten kayıtlıysa RegisterClassEx 0 döner; sorun değil, devam ederiz.
        _classRegistered = RegisterClassExW(ref wc) != 0;

        // Üst-seviye, gizli, araç penceresi (görev çubuğu düğmesi yok). ASLA parent'lanmaz.
        _hwnd = CreateWindowExW(
            WS_EX_TOOLWINDOW, ClassName, "CodexBridge Watchdog", WS_OVERLAPPED,
            0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == _wmTaskbarCreated)
        {
            TaskbarRecreated?.Invoke();
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
        _ = _classRegistered; // sınıf sürecin ömrü boyunca kayıtlı kalabilir; ayrıca kaldırmıyoruz.
    }
}
