namespace CodexBridge.Core;

/// <summary>
/// Yarım yazılmış dosya bırakmayan yazım. Snapshot'ı başka bir süreç (widget sağlayıcısı)
/// aynı anda okuyor olabilir; geçici dosyaya yazıp yer değiştirmek okuyucunun ya eski ya
/// yeni tam içeriği görmesini garanti eder.
/// </summary>
public static class AtomicFile
{
    public static void WriteAllBytes(string path, byte[] content)
    {
        string dir = Path.GetDirectoryName(path) ?? ".";
        AppPaths.EnsureDir(dir);
        string temp = Path.Combine(dir, Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp");

        try
        {
            File.WriteAllBytes(temp, content);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(temp); } catch { /* temizlik en iyi çaba */ }
            throw;
        }
    }

    public static void WriteAllText(string path, string content) =>
        WriteAllBytes(path, System.Text.Encoding.UTF8.GetBytes(content));
}
