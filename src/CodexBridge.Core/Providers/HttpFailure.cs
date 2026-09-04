using System.Net;

namespace CodexBridge.Core.Providers;

/// <summary>HTTP yanıtlarını sağlayıcıdan bağımsız olarak hata türüne çevirir.</summary>
internal static class HttpFailure
{
    /// <summary><c>Retry-After</c> başlığını süreye çevirir (saniye ya da HTTP tarihi).</summary>
    public static TimeSpan? RetryAfter(HttpResponseMessage response, DateTimeOffset now)
    {
        var header = response.Headers.RetryAfter;
        if (header is null) return null;
        if (header.Delta is { } delta) return delta > TimeSpan.Zero ? delta : null;
        if (header.Date is { } date)
        {
            var wait = date - now;
            return wait > TimeSpan.Zero ? wait : null;
        }
        return null;
    }

    /// <summary>Başarısız yanıtı sınıflandırır. <b>Yanıt gövdesi mesaja konmaz</b> —
    /// içinde token/hesap bilgisi olabilir.</summary>
    public static ProviderErrorKind Classify(HttpStatusCode status) => status switch
    {
        HttpStatusCode.TooManyRequests => ProviderErrorKind.RateLimited,
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ProviderErrorKind.AuthExpired,
        >= HttpStatusCode.InternalServerError => ProviderErrorKind.Network,
        _ => ProviderErrorKind.Unknown,
    };
}
