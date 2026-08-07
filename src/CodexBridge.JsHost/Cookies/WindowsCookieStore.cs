using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace CodexBridge.JsHost.Cookies;

/// <summary>
/// Faz 6: Chrome/Edge çerezlerini Windows'ta çözer — 31 `web` stratejisini açar.
/// Şema: <c>Local State</c>'teki <c>os_crypt.encrypted_key</c> DPAPI ile çözülür (AES-256
/// anahtarı), ardından <c>Cookies</c> SQLite'ındaki her <c>encrypted_value</c> AES-256-GCM
/// ile çözülür (v10/v20 önekleri: 3 bayt etiket + 12 bayt nonce + şifreli + 16 bayt tag).
///
/// GİZLİLİK: çözülen çerez değerleri sır-eşdeğeridir; asla loglanmaz/dışarı verilmez.
/// Kimlik bilgisi PC'de kalır — telefon istemcisine gitmez.
/// </summary>
public sealed class WindowsCookieStore
{
    /// <summary>v20 (app-bound) çözülen düz metin, gerçek değerden önce 32 baytlık başlık taşır.</summary>
    public const int AppBoundPrefixLength = 32;

    private readonly byte[] _key;          // v10 os_crypt DPAPI anahtarı
    private readonly byte[]? _appBoundKey; // v20 app-bound anahtarı (varsa)
    private readonly string _cookiesDbPath;

    private WindowsCookieStore(byte[] key, byte[]? appBoundKey, string cookiesDbPath)
    {
        _key = key;
        _appBoundKey = appBoundKey;
        _cookiesDbPath = cookiesDbPath;
    }

    /// <summary>Chrome veya Edge kullanıcı profilinden bir çerez deposu oluşturur.</summary>
    public static WindowsCookieStore ForBrowser(BrowserKind browser, string? profile = "Default")
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string root = browser switch
        {
            BrowserKind.Chrome => Path.Combine(local, "Google", "Chrome", "User Data"),
            BrowserKind.Edge => Path.Combine(local, "Microsoft", "Edge", "User Data"),
            _ => throw new ArgumentOutOfRangeException(nameof(browser)),
        };
        string localState = Path.Combine(root, "Local State");
        byte[] key = LoadAesKey(localState);
        byte[]? appBound = AppBoundKeyProvider.TryLoad(localState); // v20; çözülemezse null (COM elevator sınırı)
        string cookies = Path.Combine(root, profile ?? "Default", "Network", "Cookies");
        return new WindowsCookieStore(key, appBound, cookies);
    }

    /// <summary>Bir alan adı için <c>name=value; ...</c> çerez başlığı üretir (eşleşen çerezler).</summary>
    public string GetCookieHeader(string domain)
    {
        // Cookies dosyası tarayıcı tarafından kilitli olabilir → salt-okunur, paylaşımlı kopya iyi olur.
        var pairs = new List<string>();
        var csb = new SqliteConnectionStringBuilder { DataSource = _cookiesDbPath, Mode = SqliteOpenMode.ReadOnly };
        using var conn = new SqliteConnection(csb.ToString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name, encrypted_value FROM cookies WHERE host_key LIKE $d OR host_key LIKE $dot";
        cmd.Parameters.AddWithValue("$d", domain);
        cmd.Parameters.AddWithValue("$dot", "%." + domain);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            string name = reader.GetString(0);
            byte[] enc = (byte[])reader[1];
            // Sürüme göre anahtar seç: v20 (app-bound) ayrı anahtar ister; yoksa çözülemez atlanır.
            byte[]? key = IsV20(enc) ? _appBoundKey : _key;
            if (key is null) continue;
            string? value = TryDecrypt(enc, key);
            if (value is not null) pairs.Add($"{name}={value}");
        }
        return string.Join("; ", pairs);
    }

    /// <summary>Local State'ten AES anahtarını çıkarır (DPAPI ile korunmuş).</summary>
    private static byte[] LoadAesKey(string localStatePath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(localStatePath));
        string b64 = doc.RootElement.GetProperty("os_crypt").GetProperty("encrypted_key").GetString()!;
        byte[] blob = Convert.FromBase64String(b64);
        // "DPAPI" öneki (5 bayt) çıkarılır, kalan DPAPI ile çözülür.
        byte[] dpapiBlob = blob[5..];
        return ProtectedData.Unprotect(dpapiBlob, null, DataProtectionScope.CurrentUser);
    }

    /// <summary>Şifreli değer v20 (app-bound) önekiyle mi başlıyor.</summary>
    public static bool IsV20(byte[] encrypted)
        => encrypted.Length >= 3 && encrypted[0] == (byte)'v' && encrypted[1] == (byte)'2' && encrypted[2] == (byte)'0';

    /// <summary>
    /// v10/v20 AES-256-GCM çerez değerini çözer. Test edilebilir saf fonksiyon.
    /// Biçim: [3 bayt önek "v10"/"v20"][12 bayt nonce][şifreli metin][16 bayt tag].
    /// v20'de <paramref name="key"/> app-bound anahtar olmalı; çözülen düz metnin ilk
    /// <see cref="AppBoundPrefixLength"/> baytı app-bound başlıktır ve atılır.
    /// </summary>
    public static string? TryDecrypt(byte[] encrypted, byte[] key)
    {
        if (encrypted.Length < 3 + 12 + 16) return null;
        string prefix = Encoding.ASCII.GetString(encrypted, 0, 3);
        if (prefix is not ("v10" or "v20")) return null;
        bool appBound = prefix == "v20";

        ReadOnlySpan<byte> span = encrypted;
        ReadOnlySpan<byte> nonce = span.Slice(3, 12);
        ReadOnlySpan<byte> tag = span[^16..];
        ReadOnlySpan<byte> cipher = span[15..^16];

        byte[] plain = new byte[cipher.Length];
        try
        {
            using var gcm = new AesGcm(key, 16);
            gcm.Decrypt(nonce, cipher, tag, plain);
        }
        catch (CryptographicException)
        {
            return null;
        }

        // v20 (app-bound): düz metin [32 bayt başlık][gerçek değer]. Başlığı sıyır.
        if (appBound)
        {
            if (plain.Length < AppBoundPrefixLength) return null;
            return Encoding.UTF8.GetString(plain, AppBoundPrefixLength, plain.Length - AppBoundPrefixLength);
        }
        return Encoding.UTF8.GetString(plain);
    }

    /// <summary>
    /// Test yardımcı: bir düz metni Chrome v10 biçiminde şifreler (gerçek çerezlere dokunmadan
    /// çözme yolunu doğrulamak için).
    /// </summary>
    public static byte[] EncryptV10(string value, byte[] key)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] plain = Encoding.UTF8.GetBytes(value);
        byte[] cipher = new byte[plain.Length];
        byte[] tag = new byte[16];
        using var gcm = new AesGcm(key, 16);
        gcm.Encrypt(nonce, plain, cipher, tag);

        byte[] outp = new byte[3 + 12 + cipher.Length + 16];
        Encoding.ASCII.GetBytes("v10").CopyTo(outp, 0);
        nonce.CopyTo(outp, 3);
        cipher.CopyTo(outp, 15);
        tag.CopyTo(outp, 15 + cipher.Length);
        return outp;
    }

    /// <summary>
    /// Test yardımcı: bir düz metni Chrome v20 (app-bound) biçiminde şifreler — düz metnin önüne
    /// 32 baytlık app-bound başlık eklenir. v20 çözme + başlık sıyırma yolunu doğrulamak için.
    /// </summary>
    public static byte[] EncryptV20(string value, byte[] key)
    {
        byte[] prefixed = new byte[AppBoundPrefixLength + Encoding.UTF8.GetByteCount(value)];
        RandomNumberGenerator.Fill(prefixed.AsSpan(0, AppBoundPrefixLength));
        Encoding.UTF8.GetBytes(value).CopyTo(prefixed, AppBoundPrefixLength);

        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] cipher = new byte[prefixed.Length];
        byte[] tag = new byte[16];
        using var gcm = new AesGcm(key, 16);
        gcm.Encrypt(nonce, prefixed, cipher, tag);

        byte[] outp = new byte[3 + 12 + cipher.Length + 16];
        Encoding.ASCII.GetBytes("v20").CopyTo(outp, 0);
        nonce.CopyTo(outp, 3);
        cipher.CopyTo(outp, 15);
        tag.CopyTo(outp, 15 + cipher.Length);
        return outp;
    }
}

public enum BrowserKind { Chrome, Edge }
