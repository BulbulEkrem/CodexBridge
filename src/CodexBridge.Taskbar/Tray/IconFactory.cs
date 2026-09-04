using System.Runtime.InteropServices;
using CodexBridge.Core.Rendering;
using static CodexBridge.Taskbar.Interop.NativeMethods;

namespace CodexBridge.Taskbar.Tray;

/// <summary>
/// <see cref="TrayIconRenderer"/>'ın ürettiği BGRA baytları Windows <c>HICON</c>'una çevirir.
///
/// <para><b>Neden CreateDIBSection, CreateBitmap değil:</b> <c>CreateBitmap</c> cihaz bağımlı
/// bir bitmap üretiyor ve satır sırası (yukarıdan aşağı / aşağıdan yukarı) garanti değil.
/// İkonumuzda üst çubuk oturum, alt çubuk haftalık — ters çevrilmiş bir ikon iki kotayı
/// sessizce takas ederdi. <c>biHeight</c> negatif verilen bir DIB kesiti sırayı garanti eder.</para>
/// </summary>
internal static class IconFactory
{
    /// <summary>BGRA baytlarından ikon üretir. Dönen tanıtıcı çağıran tarafından
    /// <c>DestroyIcon</c> ile serbest bırakılmalıdır. Başarısızlıkta <c>IntPtr.Zero</c>.</summary>
    public static IntPtr FromBgra(byte[] bgra, int size = TrayIconRenderer.Size)
    {
        if (bgra.Length != size * size * 4) return IntPtr.Zero;

        var header = new BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = size,
            biHeight = -size,      // negatif = yukarıdan aşağı; satır sırası garanti
            biPlanes = 1,
            biBitCount = 32,
            biCompression = BI_RGB,
        };

        IntPtr color = CreateDIBSection(IntPtr.Zero, ref header, DIB_RGB_COLORS, out IntPtr bits, IntPtr.Zero, 0);
        if (color == IntPtr.Zero) return IntPtr.Zero;

        IntPtr mask = IntPtr.Zero;
        try
        {
            Marshal.Copy(bgra, 0, bits, bgra.Length);

            // 32bpp alfa kanalı şeffaflığı belirliyor; maske yine de zorunlu, tümü sıfır olsun.
            mask = CreateBitmap(size, size, 1, 1, IntPtr.Zero);
            if (mask == IntPtr.Zero) return IntPtr.Zero;

            var info = new ICONINFO
            {
                fIcon = true,
                xHotspot = 0,
                yHotspot = 0,
                hbmMask = mask,
                hbmColor = color,
            };
            return CreateIconIndirect(ref info);
        }
        finally
        {
            // CreateIconIndirect bitmap'lerin kopyasını alır; ikisini de bırakabiliriz.
            if (mask != IntPtr.Zero) DeleteObject(mask);
            DeleteObject(color);
        }
    }
}
