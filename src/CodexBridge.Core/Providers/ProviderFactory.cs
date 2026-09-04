using CodexBridge.Core.Security;
using CodexBridge.Core.Settings;

namespace CodexBridge.Core.Providers;

/// <summary>
/// Sağlayıcı kaynaklarını kuran <b>tek</b> fabrika. Yüzeyler ve probe'lar buradan geçer;
/// hiçbir yerde ikinci bir kurulum yolu olmamalı — aksi halde bir yüzey hız sınırı
/// sarmalayıcısı olmadan istek atmaya başlar.
/// </summary>
public static class ProviderFactory
{
    /// <summary>Ayarlarda etkin olan sağlayıcıları, 429 geri çekilmesiyle sarılmış olarak kurar.</summary>
    public static IReadOnlyList<IProviderSource> CreateEnabled(
        AppSettings settings,
        HttpClient http,
        OAuthTokenCache? tokens = null,
        TimeProvider? clock = null)
    {
        var cache = tokens ?? new OAuthTokenCache(new FileSecretStore());
        var list = new List<IProviderSource>(ProviderIds.All.Length);

        foreach (string id in ProviderIds.All)
        {
            if (!settings.IsEnabled(id)) continue;
            IProviderSource source = id switch
            {
                ProviderIds.Claude => new ClaudeUsageSource(http, cache, clock: clock),
                ProviderIds.Codex => new CodexUsageSource(http, clock: clock),
                _ => throw new InvalidOperationException($"Tanımsız sağlayıcı: {id}"),
            };
            list.Add(new RateLimitedSource(source, clock));
        }

        return list;
    }

    /// <summary>Sağlayıcı istekleri için ortak HTTP istemcisi. Zaman aşımı, uç noktanın
    /// yanıt vermemesi durumunda yenileme döngüsünü kilitlememek için kısa tutulur.</summary>
    public static HttpClient CreateHttpClient() => new()
    {
        Timeout = TimeSpan.FromSeconds(20),
    };
}
