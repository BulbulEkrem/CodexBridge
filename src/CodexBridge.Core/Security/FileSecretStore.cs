using System.Text;

namespace CodexBridge.Core.Security;

/// <summary>
/// Dosya tabanlı sır deposu. Windows'ta içerik <b>kullanıcı kapsamlı DPAPI</b> ile şifrelenir;
/// diğer platformlarda (geliştirme/test) düz metin yazılır ve dosyada açıkça işaretlenir.
///
/// Biçim: 1 bayt başlık (<c>0x01</c> DPAPI · <c>0x00</c> düz metin) + yük.
/// Başlık sayesinde bir platformda yazılan dosya diğerinde yanlışlıkla çözülmeye çalışılmaz.
/// </summary>
public sealed class FileSecretStore(string? directory = null) : ISecretStore
{
    private const byte HeaderPlain = 0x00;
    private const byte HeaderDpapi = 0x01;

    private string Dir => directory ?? AppPaths.SecretsDir;

    private string PathFor(string key)
    {
        // Anahtar dosya adına gireceği için yol ayırıcılarını ve sürprizleri reddet.
        if (key.Length == 0 || key.AsSpan().IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Geçersiz sır anahtarı.", nameof(key));
        return System.IO.Path.Combine(Dir, key + ".bin");
    }

    public string? Read(string key)
    {
        string path = PathFor(key);
        if (!File.Exists(path)) return null;

        byte[] raw;
        try { raw = File.ReadAllBytes(path); }
        catch (IOException) { return null; }

        if (raw.Length < 1) return null;
        byte header = raw[0];
        byte[] payload = raw[1..];

        try
        {
            return header switch
            {
                HeaderDpapi when OperatingSystem.IsWindows() =>
                    Encoding.UTF8.GetString(Dpapi.Unprotect(payload)),
                HeaderPlain => Encoding.UTF8.GetString(payload),
                // Başka bir makinede/kullanıcıda yazılmış ya da bozulmuş: sır kaybı değil,
                // yalnızca yeniden üretilmesi gereken bir önbellek.
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    public void Write(string key, string value)
    {
        AppPaths.EnsureDir(Dir);
        byte[] utf8 = Encoding.UTF8.GetBytes(value);

        byte header;
        byte[] payload;
        if (OperatingSystem.IsWindows())
        {
            header = HeaderDpapi;
            payload = Dpapi.Protect(utf8);
        }
        else
        {
            header = HeaderPlain;
            payload = utf8;
        }

        var buffer = new byte[payload.Length + 1];
        buffer[0] = header;
        payload.CopyTo(buffer, 1);

        AtomicFile.WriteAllBytes(PathFor(key), buffer);
    }

    public void Delete(string key)
    {
        try { File.Delete(PathFor(key)); }
        catch (IOException) { /* zaten yok ya da kilitli; sır silinmemesi kritik değil */ }
    }
}
