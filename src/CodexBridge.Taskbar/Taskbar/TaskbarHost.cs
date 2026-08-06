using CodexBridge.Taskbar.Interop;
using static CodexBridge.Taskbar.Interop.NativeMethods;

namespace CodexBridge.Taskbar.Taskbar;

/// <summary>
/// Bir pencereyi Windows görev çubuğuna (<c>Shell_TrayWnd</c>) çocuk olarak yerleştirir.
/// Teknik Deskband11'in <c>MoveToTaskbar</c>'ından uyarlandı (MIT): WS_POPUP → WS_CHILD,
/// SetParent, sonra ReBar'a göre konumlandırma. WinForms spike'ında bu makinede canlı doğrulandı.
/// </summary>
internal static class TaskbarHost
{
    /// <summary>Verilen pencereyi görev çubuğuna parent'lar ve çubuğun soluna yerleştirir.</summary>
    /// <returns>Başarılıysa true (görev çubuğu bulunduysa).</returns>
    internal static bool MoveToTaskbar(IntPtr bandHwnd, int leftOffsetDips = 12, int widthDips = 280)
    {
        IntPtr taskbar = FindWindowW("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero) return false;

        IntPtr rebar = FindWindowExW(taskbar, IntPtr.Zero, "ReBarWindow32", null);

        // WS_POPUP çıkar, WS_CHILD ekle → z-order kavgası ve autohide sorunları çözülür.
        // Ayrıca başlık/kenarlık/sistem menüsü/düğme stillerini sıyır (kromsuz band).
        int style = GetWindowLongW(bandHwnd, GWL_STYLE);
        style = (style & ~(WS_POPUP | WS_CHROME)) | WS_CHILD;
        SetWindowLongW(bandHwnd, GWL_STYLE, style);
        SetParent(bandHwnd, taskbar);

        GetWindowRect(taskbar, out RECT tb);
        int y = 0, h = tb.Height;
        if (rebar != IntPtr.Zero && GetWindowRect(rebar, out RECT rb))
        {
            y = rb.Top - tb.Top;
            h = rb.Height;
        }

        float scale = GetDpiForWindow(bandHwnd) / 96f;
        if (scale <= 0) scale = 1f;
        int x = (int)(leftOffsetDips * scale);
        int w = (int)(widthDips * scale);

        SetWindowPos(bandHwnd, IntPtr.Zero, x, y, w, h,
            SWP_FRAMECHANGED | SWP_NOACTIVATE | SWP_NOZORDER | SWP_SHOWWINDOW);
        return true;
    }
}
