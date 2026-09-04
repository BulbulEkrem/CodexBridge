using System.Runtime.InteropServices;

namespace CodexBridge.Taskbar.Interop;

/// <summary>
/// Görev çubuğu parent'lama ve Explorer-restart hayatta kalma için gereken minimal Win32 yüzeyi.
/// El yazımı P/Invoke (CsWin32 kod üretimine bağımlı değil) — öngörülebilir derleme için.
/// Teknik Deskband11'den (MIT) uyarlandı; hayatta kalma mantığı bizim eklememiz.
/// </summary>
internal static partial class NativeMethods
{
    internal const int GWL_STYLE = -16;
    internal const int WS_POPUP = unchecked((int)0x80000000);
    internal const int WS_CHILD = 0x40000000;
    // Görev çubuğu band'ında istenmeyen pencere kromu (başlık/kenarlık/sistem menüsü/düğmeler).
    internal const int WS_CAPTION = 0x00C00000;
    internal const int WS_THICKFRAME = 0x00040000;
    internal const int WS_SYSMENU = 0x00080000;
    internal const int WS_MINIMIZEBOX = 0x00020000;
    internal const int WS_MAXIMIZEBOX = 0x00010000;
    internal const int WS_CHROME = WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX;

    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint SWP_FRAMECHANGED = 0x0020;
    internal const uint SWP_SHOWWINDOW = 0x0040;
    internal const uint SWP_NOZORDER = 0x0004;

    internal const int WM_DISPLAYCHANGE = 0x007E;
    internal const int WM_SETTINGCHANGE = 0x001A;
    internal const uint SPI_SETWORKAREA = 0x002F;

    // Gizli koordinatör penceresi için pencere stilleri.
    internal const int WS_EX_TOOLWINDOW = 0x00000080; // görev çubuğu düğmesi yok
    internal const uint WS_OVERLAPPED = 0x00000000;

    // ---- Yüzen HUD ----
    internal const int GWL_EXSTYLE = -20;
    internal const int WS_EX_APPWINDOW = 0x00040000; // Alt-Tab'da görünme sebebi; sıyrılıyor

    /// <summary>Fare hangi pencere parçasının üstünde sorusu. HUD'ın her yerinden
    /// sürüklenebilmesi için istemci alanına gelen bu mesaja "başlık çubuğu" cevabı veriyoruz.</summary>
    internal const int WM_NCHITTEST = 0x0084;
    internal const int HTCLIENT = 1;
    internal const int HTCAPTION = 2;

    /// <summary>Sürükleme bitti; konumu bu noktada kaydediyoruz (her hareket adımında değil).</summary>
    internal const int WM_EXITSIZEMOVE = 0x0232;

    // Sanal ekran (tüm monitörleri kapsayan dikdörtgen) ölçüleri. Kaydedilmiş HUD konumunun
    // hâlâ görünür bir yerde olup olmadığını sınamak için; WinUI'ın DisplayArea'sı yerine
    // bunlar kullanılıyor (bkz. HudWindow.Place notu).
    internal const int SM_XVIRTUALSCREEN = 76;
    internal const int SM_YVIRTUALSCREEN = 77;
    internal const int SM_CXVIRTUALSCREEN = 78;
    internal const int SM_CYVIRTUALSCREEN = 79;

    [LibraryImport("user32.dll")]
    internal static partial int GetSystemMetrics(int nIndex);

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial IntPtr FindWindowW(string? lpClassName, string? lpWindowName);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial IntPtr FindWindowExW(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial int GetWindowLongW(IntPtr hWnd, int nIndex);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial int SetWindowLongW(IntPtr hWnd, int nIndex, int dwNewLong);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint RegisterWindowMessageW(string lpString);

    [LibraryImport("user32.dll")]
    internal static partial uint GetDpiForWindow(IntPtr hwnd);
}
