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
        int clusterLeft = -1;
        if (rebar != IntPtr.Zero && GetWindowRect(rebar, out RECT rb))
        {
            y = rb.Top - tb.Top;
            h = rb.Height;
            clusterLeft = rb.Left - tb.Left;
        }

        float scale = GetDpiForWindow(bandHwnd) / 96f;
        if (scale <= 0) scale = 1f;
        int w = (int)(widthDips * scale);
        int margin = (int)(leftOffsetDips * scale);

        // Windows 11'de görev çubuğunun TÜM görsel içeriği (hava durumu, Start, arama, uygulama
        // düğmeleri) çubuğun tam genişliğini kaplayan tek bir XAML adasında
        // (Windows.UI.Composition.DesktopWindowContentBridge) çiziliyor — yani "boş sol alan"
        // ayrı bir pencere değil, sorgulanamıyor. Sabit sol ofset kullanmak band'ı doğrudan hava
        // durumu widget'ının üstüne oturtuyordu (canlı testte görüldü; opak siyah zemin bunu
        // gizliyordu, zemin saydamlaşınca ortaya çıktı).
        //
        // Ölçülebilir tek sınır ortadaki kümenin sol kenarı: ReBarWindow32 ve gizli "Start"
        // penceresi ikisi de oradan başlıyor. Band'ı o kenara YASLIYORUZ — soldaki widget ne kadar
        // genişse genişlesin üstüne binmiyoruz, küme büyüyüp küçüldükçe band onu takip ediyor.
        // Küme sola dayalıysa (ortalama kapalı) solda boşluk kalmaz → eski davranışa düşülür.
        int x = clusterLeft > 0 ? clusterLeft - w - margin : margin;
        if (x < margin) x = margin;

        SetWindowPos(bandHwnd, IntPtr.Zero, x, y, w, h,
            SWP_FRAMECHANGED | SWP_NOACTIVATE | SWP_NOZORDER | SWP_SHOWWINDOW);
        return true;
    }
}
