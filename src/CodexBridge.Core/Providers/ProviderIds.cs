namespace CodexBridge.Core.Providers;

/// <summary>
/// Desteklenen sağlayıcıların sabit kimlikleri ve sunum meta verisi.
/// Kimlik dizeleri <c>dashboard/v1</c> şemasında <c>providers[].id</c> olarak geçer ve
/// <c>settings.json</c> ile <c>snapshot.json</c>'da anahtar olarak kullanılır — <b>değiştirilemez</b>.
/// </summary>
public static class ProviderIds
{
    public const string Claude = "claude";
    public const string Codex = "codex";

    /// <summary>Bu sürümde desteklenen sağlayıcılar, band'daki görüntülenme sırasıyla.</summary>
    public static readonly string[] All = [Claude, Codex];

    public static string DisplayName(string id) => id switch
    {
        Claude => "Claude",
        Codex => "Codex",
        _ => id,
    };

    /// <summary>Sağlayıcı kimlik rengi. Yüzeylerde ikon/kare rozetinde kullanılır;
    /// pill arka planı eşik renginden gelir, buradan değil.</summary>
    public static string AccentColor(string id) => id switch
    {
        Claude => "#D97757",
        Codex => "#10A37F",
        _ => "#6E6E6E",
    };

    /// <summary>Band ve pano sıralaması. Küçük olan önce gelir.</summary>
    public static int SortKey(string id) => Array.IndexOf(All, id) is var i && i >= 0 ? i * 10 : 10_000;
}
