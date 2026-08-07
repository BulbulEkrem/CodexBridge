using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodexBridge.Host.Push;

/// <summary>
/// Push sağlayıcıları için asgari JWT imzalama. APNs ES256 (P-256 ECDSA) sağlayıcı
/// otantikasyon token'ı; FCM ise service account ile RS256 imzalı OAuth2 assertion ister.
/// Harici bağımlılık yok — System.Security.Cryptography yeter.
/// </summary>
internal static class Jwt
{
    public static string Base64Url(ReadOnlySpan<byte> bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Base64UrlJson(object o)
        => Base64Url(JsonSerializer.SerializeToUtf8Bytes(o));

    /// <summary>APNs provider token: header {alg:ES256, kid}, claims {iss:teamId, iat}. .p8 ECDSA ile imzalanır.</summary>
    public static string SignEs256(object header, object claims, ECDsa key)
    {
        string signingInput = Base64UrlJson(header) + "." + Base64UrlJson(claims);
        // ES256 imzası JOSE'de ham R||S (IEEE-P1363), DER değil.
        byte[] sig = key.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return signingInput + "." + Base64Url(sig);
    }

    /// <summary>Google OAuth2 için RS256 imzalı assertion (service account).</summary>
    public static string SignRs256(object header, object claims, RSA key)
    {
        string signingInput = Base64UrlJson(header) + "." + Base64UrlJson(claims);
        byte[] sig = key.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return signingInput + "." + Base64Url(sig);
    }

    public static long UnixNow() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
