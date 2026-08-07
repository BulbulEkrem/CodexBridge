using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodexBridge.JsHost.Cookies;

/// <summary>
/// Faz 7 (v20): Chrome/Edge 127+ "app-bound" çerez anahtarını çözmeye çalışır.
///
/// <para><b>Yapı:</b> <c>Local State → os_crypt.app_bound_encrypted_key</c> (base64). Çözülünce
/// <c>"APPB"</c> (4 bayt) önekiyle başlar. Kalan blob, tarayıcının <b>elevation service</b>'i
/// tarafından iki kat DPAPI ile sarılıdır: önce <b>SYSTEM</b> bağlamında, sonra <b>kullanıcı</b>
/// bağlamında. İç katman çözülünce [1 bayt bayrak][32 bayt AES anahtarı] elde edilir.</para>
///
/// <para><b>Bilinçli sınır:</b> SYSTEM DPAPI katmanı yalnızca SYSTEM olarak çalışan
/// <c>IElevator</c> COM sunucusuyla açılabilir; bunu taklit etmek yükseltme ister ve otonom
/// oturumda GÜVENLİK/GİZLİLİK gereği çalıştırılmaz. Bu sağlayıcı kullanıcı-katmanını en iyi
/// çabayla soyar; SYSTEM katmanı kalırsa <c>null</c> döner (v20 çerezleri sessizce atlanır,
/// v10 çerezleri etkilenmez). COM elevator entegrasyonu gelecek tur.</para>
/// </summary>
public static class AppBoundKeyProvider
{
    private const string AppBoundPrefix = "APPB";
    private const int AesKeyLength = 32;

    /// <summary>app-bound anahtarını döndürür; çözülemezse (SYSTEM katmanı / anahtar yok) null.</summary>
    public static byte[]? TryLoad(string localStatePath)
    {
        try
        {
            if (!File.Exists(localStatePath)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(localStatePath));
            if (!doc.RootElement.TryGetProperty("os_crypt", out var osCrypt)) return null;
            if (!osCrypt.TryGetProperty("app_bound_encrypted_key", out var keyEl)) return null;
            if (keyEl.GetString() is not { Length: > 0 } b64) return null;

            byte[] blob = Convert.FromBase64String(b64);
            if (blob.Length <= AppBoundPrefix.Length) return null;
            if (Encoding.ASCII.GetString(blob, 0, AppBoundPrefix.Length) != AppBoundPrefix) return null;

            byte[] inner = blob[AppBoundPrefix.Length..];
            return TryUnwrap(inner);
        }
        catch
        {
            // Herhangi bir hata → v20 kullanılamaz; v10 yolu etkilenmez. Sessiz.
            return null;
        }
    }

    /// <summary>
    /// Kullanıcı DPAPI katman(lar)ını en iyi çabayla soyar. SYSTEM katmanı kalırsa (gerçek
    /// Chrome durumu) Unprotect başarısız olur → null. Sonuç [bayrak][32 bayt] ise anahtarı çıkarır.
    /// </summary>
    private static byte[]? TryUnwrap(byte[] data)
    {
        byte[] current = data;
        // En çok iki DPAPI sarımı bekleriz; her adımda kullanıcı-bağlamı çözmeyi dene.
        for (int layer = 0; layer < 2; layer++)
        {
            if (ExtractKey(current) is { } direct) return direct;
            try
            {
                current = ProtectedData.Unprotect(current, null, DataProtectionScope.CurrentUser);
            }
            catch (CryptographicException)
            {
                return null; // SYSTEM katmanı: kullanıcı olarak çözülemez → COM elevator gerekir.
            }
        }
        return ExtractKey(current);
    }

    /// <summary>Blob doğrudan 32 baytlık anahtar ya da [1 bayt bayrak][32 bayt] ise anahtarı döndürür.</summary>
    private static byte[]? ExtractKey(byte[] blob)
    {
        if (blob.Length == AesKeyLength) return blob;
        if (blob.Length == AesKeyLength + 1) return blob[1..]; // baş bayrak baytını at
        return null;
    }
}
