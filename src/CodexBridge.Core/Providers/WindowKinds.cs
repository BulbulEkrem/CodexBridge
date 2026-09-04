namespace CodexBridge.Core.Providers;

/// <summary>
/// <c>dashboard/v1</c> içindeki <c>windows[].kind</c> değerleri. Yüzeyler pencereleri
/// diziye göre değil <b>bu kimliğe göre</b> seçer — sağlayıcı kaç pencere döndürürse
/// döndürsün band hep oturum ve haftalığı gösterir.
/// </summary>
public static class WindowKinds
{
    /// <summary>Kısa yuvarlanan pencere (Claude ve Codex'te 5 saat).</summary>
    public const string Session = "session";

    /// <summary>7 günlük pencere.</summary>
    public const string Weekly = "weekly";

    /// <summary>30 günlük pencere (Codex'te bazı planlarda).</summary>
    public const string Monthly = "monthly";

    /// <summary>Claude'un Opus'a özel haftalık penceresi.</summary>
    public const string Opus = "opus";

    /// <summary>Band'ın gösterdiği iki pencere, üstten alta sırayla.</summary>
    public static readonly string[] BandOrder = [Session, Weekly];
}
