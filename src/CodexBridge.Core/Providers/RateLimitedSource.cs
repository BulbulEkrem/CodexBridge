using CodexBridge.Core.Dashboard;

namespace CodexBridge.Core.Providers;

/// <summary>
/// Bir sağlayıcı kaynağını 429 geri çekilmesiyle sarar.
///
/// Sağlayıcı 429 döndüğünde <c>Retry-After</c> süresi boyunca <b>hiç istek atılmaz</b>:
/// pencere kapalıyken çağrı doğrudan <see cref="ProviderErrorKind.RateLimited"/> ile düşer.
/// Toplayıcı bunu görüp son bilinen değeri "veri yaşı" ile göstermeye devam eder.
///
/// <para><c>Retry-After</c> gelmediyse üstel geri çekilme uygulanır
/// (<see cref="FallbackBackoff"/>'tan başlayıp <see cref="MaxBackoff"/>'a kadar ikiye katlanır),
/// başarılı bir çekimde sıfırlanır.</para>
/// </summary>
public sealed class RateLimitedSource(IProviderSource inner, TimeProvider? clock = null) : IProviderSource
{
    public static readonly TimeSpan FallbackBackoff = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(30);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Lock _gate = new();
    private DateTimeOffset _blockedUntil = DateTimeOffset.MinValue;
    private TimeSpan _nextFallback = FallbackBackoff;

    public string ProviderId => inner.ProviderId;

    /// <summary>Şu an geri çekilme penceresi açıksa kalan süre; değilse <c>null</c>.</summary>
    public TimeSpan? BlockedFor
    {
        get
        {
            lock (_gate)
            {
                var remaining = _blockedUntil - _clock.GetUtcNow();
                return remaining > TimeSpan.Zero ? remaining : null;
            }
        }
    }

    public async Task<ProviderRow> FetchAsync(CancellationToken ct = default)
    {
        if (BlockedFor is { } remaining)
        {
            throw new ProviderSourceException(
                ProviderErrorKind.RateLimited,
                $"Hız sınırı: {FormatRemaining(remaining)} sonra tekrar denenecek.",
                remaining);
        }

        try
        {
            var row = await inner.FetchAsync(ct);
            lock (_gate) { _nextFallback = FallbackBackoff; }
            return row;
        }
        catch (ProviderSourceException ex) when (ex.Kind == ProviderErrorKind.RateLimited)
        {
            TimeSpan wait;
            lock (_gate)
            {
                wait = ex.RetryAfter ?? _nextFallback;
                if (wait > MaxBackoff) wait = MaxBackoff;
                _blockedUntil = _clock.GetUtcNow() + wait;
                if (ex.RetryAfter is null)
                {
                    var doubled = _nextFallback * 2;
                    _nextFallback = doubled > MaxBackoff ? MaxBackoff : doubled;
                }
            }
            throw new ProviderSourceException(
                ProviderErrorKind.RateLimited,
                $"Hız sınırı: {FormatRemaining(wait)} sonra tekrar denenecek.",
                wait, ex);
        }
    }

    private static string FormatRemaining(TimeSpan t) =>
        t.TotalMinutes >= 1 ? $"{Math.Ceiling(t.TotalMinutes):0} dk" : $"{Math.Ceiling(t.TotalSeconds):0} sn";
}
