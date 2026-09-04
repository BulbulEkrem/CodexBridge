namespace CodexBridge.Core.Providers;

/// <summary>Bir sağlayıcı çekiminin neden başarısız olduğu. Yüzeyler bu türe göre farklı
/// mesaj gösterir: kimlik yoksa "giriş yap", hız sınırındaysa "birazdan tekrar denenecek".</summary>
public enum ProviderErrorKind
{
    /// <summary>Kimlik dosyası yok — kullanıcı ilgili CLI ile giriş yapmamış.</summary>
    NoCredentials,
    /// <summary>Token var ama süresi dolmuş ve yenilenemedi.</summary>
    AuthExpired,
    /// <summary>Sağlayıcı 429 döndü. <see cref="ProviderSourceException.RetryAfter"/> dolu olabilir.</summary>
    RateLimited,
    /// <summary>Ağ/aktarım hatası.</summary>
    Network,
    /// <summary>Yanıt beklenen şekilde değil.</summary>
    Parse,
    /// <summary>Sınıflandırılamayan hata.</summary>
    Unknown,
}

/// <summary>
/// Sağlayıcı çekim hatası. <b>Mesaj kullanıcıya gösterilir</b> — asla token, çerez veya
/// ham yanıt gövdesi içermemeli.
/// </summary>
public sealed class ProviderSourceException(
    ProviderErrorKind kind,
    string message,
    TimeSpan? retryAfter = null,
    Exception? inner = null) : Exception(message, inner)
{
    public ProviderErrorKind Kind { get; } = kind;

    /// <summary>429'da sunucunun bildirdiği bekleme süresi; yoksa null.</summary>
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
