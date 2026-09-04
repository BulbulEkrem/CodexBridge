namespace CodexBridge.Core;

/// <summary>
/// Uygulamanın diskteki tek adres kaynağı. Windows'ta:
/// <list type="bullet">
///   <item><c>%LOCALAPPDATA%\CodexBridge\</c> — snapshot, sırlar, günlükler (makineye özel, yedeklenmez)</item>
///   <item><c>%APPDATA%\CodexBridge\</c> — settings.json (kullanıcı profiliyle gezer)</item>
/// </list>
/// Windows dışında .NET'in eşdeğer dizinlerine düşer; geliştirme ve test için yeterli.
/// Testlerde <see cref="OverrideRoot"/> ile geçici bir dizine yönlendirilebilir.
/// </summary>
public static class AppPaths
{
    private const string AppFolder = "CodexBridge";
    private static string? _override;

    /// <summary>Testler için tüm kökleri tek bir geçici dizine yönlendirir. <c>null</c> ile geri alınır.</summary>
    public static void OverrideRoot(string? root) => _override = root;

    /// <summary>Makineye özel veri kökü (snapshot, sırlar).</summary>
    public static string LocalRoot => _override
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolder);

    /// <summary>Gezici ayar kökü.</summary>
    public static string RoamingRoot => _override
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolder);

    /// <summary>Süreçler arası paylaşılan dashboard/v1 snapshot'ı.</summary>
    public static string SnapshotFile => Path.Combine(LocalRoot, "snapshot.json");

    /// <summary>Kullanıcı ayarları.</summary>
    public static string SettingsFile => Path.Combine(RoamingRoot, "settings.json");

    /// <summary>DPAPI korumalı sır dosyaları.</summary>
    public static string SecretsDir => Path.Combine(LocalRoot, "secrets");

    /// <summary>Kullanıcının Claude Code OAuth kimlik dosyası. <b>Yalnızca okunur.</b></summary>
    public static string ClaudeCredentialsFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", ".credentials.json");

    /// <summary>Codex CLI OAuth kimlik dosyası. <c>CODEX_HOME</c> varsa onu dinler. <b>Yalnızca okunur.</b></summary>
    public static string CodexAuthFile
    {
        get
        {
            string home = Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } h
                ? h
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
            return Path.Combine(home, "auth.json");
        }
    }

    /// <summary>Dizini oluşturur (varsa dokunmaz) ve yolu döndürür.</summary>
    public static string EnsureDir(string dir)
    {
        Directory.CreateDirectory(dir);
        return dir;
    }
}
