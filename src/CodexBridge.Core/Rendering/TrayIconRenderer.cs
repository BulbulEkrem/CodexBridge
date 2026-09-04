namespace CodexBridge.Core.Rendering;

/// <summary>Tepsi ikonundaki bir çubuğun alarm seviyesi.</summary>
public enum MeterLevel { Ok, Warn, Crit, Unknown }

/// <summary>
/// Tepsi ikonunun piksellerini üretir — <b>platformdan bağımsız</b>, saf fonksiyon.
/// Windows katmanı çıkan baytları HICON'a çevirir. Bu ayrım sayesinde çizim mantığı
/// Windows olmadan test edilebiliyor (yaklaşım Win-CodexBar'ın <c>render_bar_icon_rgba</c>'sından
/// uyarlandı, MIT).
///
/// <para>Düzen: şeffaf zemin üzerine koyu bir kart, kartın içinde iki yatay çubuk —
/// <b>üstte oturum, altta haftalık</b>. Band'daki iki çubukla aynı sıra, aynı renk kuralı;
/// kullanıcı iki yüzeyde farklı bir dil öğrenmek zorunda kalmıyor.</para>
/// </summary>
public static class TrayIconRenderer
{
    /// <summary>İkonun kenar uzunluğu (piksel).</summary>
    public const int Size = 32;

    /// <summary>Bir pikselin bayt sayısı (BGRA).</summary>
    public const int BytesPerPixel = 4;

    private const int Margin = 3;      // şeffaf kenar payı
    private const int BarInset = 4;    // çubukların karta göre yatay payı
    private const int BarHeight = 7;
    private const int BarGap = 4;

    private static readonly byte[] CardBgra = [0x2E, 0x2E, 0x38, 0xFF];   // koyu kart
    private static readonly byte[] TrackBgra = [0x50, 0x50, 0x5A, 0xFF];  // boş çubuk yatağı

    /// <summary>
    /// İkonu çizer ve <b>BGRA</b> sırasında ham baytları döndürür
    /// (Windows <c>CreateBitmap</c> 32bpp bu sırayı bekler).
    /// </summary>
    /// <param name="sessionPercent">Üst çubuk (0–100). <c>null</c> ise veri yok.</param>
    /// <param name="weeklyPercent">Alt çubuk (0–100). <c>null</c> ise veri yok.</param>
    /// <param name="sessionLevel">Üst çubuğun rengi.</param>
    /// <param name="weeklyLevel">Alt çubuğun rengi.</param>
    /// <param name="dimmed">Veri bayatladıysa ya da hata varsa kart soluklaşır.</param>
    public static byte[] Render(
        double? sessionPercent,
        double? weeklyPercent,
        MeterLevel sessionLevel,
        MeterLevel weeklyLevel,
        bool dimmed = false)
    {
        var pixels = new byte[Size * Size * BytesPerPixel];

        byte cardAlpha = dimmed ? (byte)0xB4 : (byte)0xFF;
        FillRect(pixels, Margin, Margin, Size - Margin, Size - Margin,
            CardBgra[0], CardBgra[1], CardBgra[2], cardAlpha);

        int barLeft = Margin + BarInset;
        int barRight = Size - Margin - BarInset;

        // İki çubuk kartın dikey ortasına simetrik yerleşir.
        int totalHeight = BarHeight * 2 + BarGap;
        int top = (Size - totalHeight) / 2;

        DrawBar(pixels, barLeft, top, barRight, top + BarHeight, sessionPercent, sessionLevel, dimmed);
        DrawBar(pixels, barLeft, top + BarHeight + BarGap, barRight, top + totalHeight,
            weeklyPercent, weeklyLevel, dimmed);

        return pixels;
    }

    private static void DrawBar(
        byte[] pixels, int left, int top, int right, int bottom,
        double? percent, MeterLevel level, bool dimmed)
    {
        FillRect(pixels, left, top, right, bottom, TrackBgra[0], TrackBgra[1], TrackBgra[2], 0xFF);

        if (percent is not { } pct) return;

        int width = right - left;
        // %0 bile olsa 1 piksel çiz: "veri var ama sıfır" ile "veri yok" ayırt edilebilsin.
        int filled = Math.Clamp((int)Math.Round(width * Math.Clamp(pct, 0, 100) / 100.0), 1, width);

        var (b, g, r) = LevelBgr(level);
        byte alpha = dimmed ? (byte)0xB4 : (byte)0xFF;
        FillRect(pixels, left, top, left + filled, bottom, b, g, r, alpha);
    }

    /// <summary>Seviye renkleri band'daki pill renkleriyle birebir aynı.</summary>
    private static (byte B, byte G, byte R) LevelBgr(MeterLevel level) => level switch
    {
        MeterLevel.Ok => (0x5F, 0xCB, 0x6C),      // #6CCB5F
        MeterLevel.Warn => (0x00, 0xE1, 0xFC),    // #FCE100
        MeterLevel.Crit => (0x5B, 0x5B, 0xFF),    // #FF5B5B
        _ => (0x9A, 0x9A, 0x9A),                  // #9A9A9A
    };

    private static void FillRect(byte[] pixels, int x0, int y0, int x1, int y1, byte b, byte g, byte r, byte a)
    {
        x0 = Math.Max(0, x0); y0 = Math.Max(0, y0);
        x1 = Math.Min(Size, x1); y1 = Math.Min(Size, y1);

        for (int y = y0; y < y1; y++)
        {
            int rowStart = y * Size * BytesPerPixel;
            for (int x = x0; x < x1; x++)
            {
                int i = rowStart + x * BytesPerPixel;
                pixels[i] = b;
                pixels[i + 1] = g;
                pixels[i + 2] = r;
                pixels[i + 3] = a;
            }
        }
    }

    /// <summary>Bir pikselin BGRA bileşenlerini okur. Testler ve hata ayıklama için.</summary>
    public static (byte B, byte G, byte R, byte A) PixelAt(byte[] pixels, int x, int y)
    {
        int i = (y * Size + x) * BytesPerPixel;
        return (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]);
    }

    /// <summary>Kullanım yüzdesini eşiklere göre seviyeye çevirir.</summary>
    public static MeterLevel LevelFor(double? percent, double warnAt, double critAt) =>
        percent is not { } p ? MeterLevel.Unknown
        : p >= critAt ? MeterLevel.Crit
        : p >= warnAt ? MeterLevel.Warn
        : MeterLevel.Ok;
}
