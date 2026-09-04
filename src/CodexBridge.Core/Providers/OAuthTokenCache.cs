using System.Text.Json;
using System.Text.Json.Serialization;
using CodexBridge.Core.Security;

namespace CodexBridge.Core.Providers;

/// <summary>Önbellekteki bir erişim token'ı.</summary>
public sealed record CachedToken(
    [property: JsonPropertyName("accessToken")] string AccessToken,
    [property: JsonPropertyName("expiresAt")] DateTimeOffset? ExpiresAt,
    [property: JsonPropertyName("refreshToken")] string? RefreshToken)
{
    /// <summary>Token <paramref name="skew"/> içinde dolacaksa (veya dolduysa) true.
    /// Bitiş zamanı bilinmiyorsa taze kabul edilir — sunucu 401 dönerse zaten yenilenir.</summary>
    public bool IsExpiring(DateTimeOffset now, TimeSpan skew) =>
        ExpiresAt is { } exp && exp <= now + skew;
}

/// <summary>
/// Yenilediğimiz OAuth token'larını <b>kendi</b> DPAPI korumalı deposunda tutar.
///
/// <para><b>Kural:</b> kullanıcının <c>~/.claude/.credentials.json</c> ve
/// <c>~/.codex/auth.json</c> dosyalarına <b>asla yazılmaz.</b> Bu dosyalar ilgili CLI'ın
/// malıdır; aynı anda o da yenileme yapıyor olabilir ve birbirimizin token'ını ezeriz.
/// Biz yalnızca okuruz, yenilediğimizi buraya koyarız.</para>
/// </summary>
public sealed class OAuthTokenCache(ISecretStore store)
{
    private const string StoreKey = "oauth-tokens";

    private readonly Lock _gate = new();
    private Dictionary<string, CachedToken>? _cache;

    public CachedToken? Get(string providerId)
    {
        lock (_gate)
        {
            Load();
            return _cache!.GetValueOrDefault(providerId);
        }
    }

    public void Put(string providerId, CachedToken token)
    {
        lock (_gate)
        {
            Load();
            _cache![providerId] = token;
            Persist();
        }
    }

    public void Clear(string providerId)
    {
        lock (_gate)
        {
            Load();
            if (_cache!.Remove(providerId)) Persist();
        }
    }

    private void Load()
    {
        if (_cache is not null) return;
        string? raw = store.Read(StoreKey);
        if (raw is null)
        {
            _cache = [];
            return;
        }
        try
        {
            _cache = JsonSerializer.Deserialize<Dictionary<string, CachedToken>>(raw, Json) ?? [];
        }
        catch (JsonException)
        {
            // Bozuk önbellek sır kaybı değil; sıfırdan yenilenir.
            _cache = [];
        }
    }

    private void Persist() => store.Write(StoreKey, JsonSerializer.Serialize(_cache, Json));

    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
