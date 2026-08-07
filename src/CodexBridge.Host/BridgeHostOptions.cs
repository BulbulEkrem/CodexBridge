using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace CodexBridge.Host;

/// <summary>
/// Host bağlama + kimlik doğrulama yapılandırması. Üst akış CodexBar/Win-CodexBar `serve`
/// güvenlik modelini yansıtır: loopback dışı bind token + açık düz-HTTP onayı gerektirir;
/// dashboard rotası token yoksa kapalı-başarısızdır (fails closed).
/// </summary>
public sealed class BridgeHostOptions
{
    public string Host { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 8787;
    public int RefreshIntervalSeconds { get; init; } = 60;
    public bool AllowPlainHttp { get; init; }
    /// <summary>SHA-256 digest of the bearer token, or null if unset.</summary>
    public byte[]? TokenDigest { get; private set; }

    // --- Veri kaynağı seçimi (Faz 3 sahte → Faz 5 kendi JS katmanı → dashboard/v1 host'tan oku) ---
    /// <summary>fake | http. http ise başka bir dashboard/v1 host'undan okur (çok-makine birleştirme).</summary>
    public string SourceKind { get; init; } = "fake";
    public string? SourceUrl { get; init; }
    public string? SourceToken { get; init; }

    // --- Faz 7 push ---
    public bool PushEnabled { get; init; } = true;
    /// <summary>Aynı olayın (dedupeKey) yeniden gönderilmeden önce beklemesi gereken süre.</summary>
    public int PushCooldownMinutes { get; init; } = 30;
    public string DevicesPath { get; init; } = "";

    public bool IsLoopback => IsLoopbackHost(Host);
    public bool HasToken => TokenDigest is not null;

    public static BridgeHostOptions FromEnvironment(string[] args)
    {
        string Get(string cliFlag, string env, string def)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], cliFlag, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return Environment.GetEnvironmentVariable(env) ?? def;
        }
        bool Has(string cliFlag, string env)
        {
            if (args.Any(a => string.Equals(a, cliFlag, StringComparison.OrdinalIgnoreCase))) return true;
            var v = Environment.GetEnvironmentVariable(env);
            return v is "1" or "true" or "TRUE";
        }

        var opts = new BridgeHostOptions
        {
            Host = Get("--host", "CODEXBRIDGE_HOST", "127.0.0.1"),
            Port = int.TryParse(Get("--port", "CODEXBRIDGE_PORT", "8787"), out var p) ? p : 8787,
            RefreshIntervalSeconds = int.TryParse(Get("--refresh-interval", "CODEXBRIDGE_REFRESH_INTERVAL", "60"), out var r) ? r : 60,
            AllowPlainHttp = Has("--allow-plain-http", "CODEXBRIDGE_ALLOW_PLAIN_HTTP"),
            SourceKind = Get("--source", "CODEXBRIDGE_SOURCE", "fake").Trim().ToLowerInvariant(),
            SourceUrl = Get("--source-url", "CODEXBRIDGE_SOURCE_URL", "").Trim() is { Length: > 0 } su ? su : null,
            SourceToken = Get("--source-token", "CODEXBRIDGE_SOURCE_TOKEN", "").Trim() is { Length: > 0 } st ? st : null,
            PushEnabled = Get("--push", "CODEXBRIDGE_PUSH_ENABLED", "true").Trim().ToLowerInvariant() is not ("0" or "false"),
            PushCooldownMinutes = int.TryParse(Get("--push-cooldown", "CODEXBRIDGE_PUSH_COOLDOWN_MIN", "30"), out var pc) ? pc : 30,
            DevicesPath = Get("--devices-path", "CODEXBRIDGE_DEVICES_PATH", Push.JsonFileDeviceRegistry.DefaultPath()),
        };

        // Token: CODEXBAR_DASHBOARD_TOKEN (üst akışla uyum) veya --dashboard-token.
        var token = Get("--dashboard-token", "CODEXBAR_DASHBOARD_TOKEN", "").Trim();
        if (token.Length > 0)
            opts.TokenDigest = SHA256.HashData(Encoding.UTF8.GetBytes(token));

        return opts;
    }

    /// <summary>Başlangıç doğrulaması. Hata varsa mesaj döndürür; yoksa null.</summary>
    public string? Validate()
    {
        if (Port is < 1 or > 65535) return "--port 1..65535 aralığında olmalı.";
        if (SourceKind is not ("fake" or "http")) return "--source yalnızca fake | http olabilir.";
        if (SourceKind == "http" && SourceUrl is null) return "http kaynağı için --source-url gerekli.";
        if (!IsLoopback)
        {
            if (!HasToken) return "Loopback dışı bind için token gerekli (CODEXBAR_DASHBOARD_TOKEN).";
            if (!AllowPlainHttp) return "Loopback dışı bind için --allow-plain-http gerekli (düz HTTP'de token açık geçer).";
        }
        return null;
    }

    /// <summary>Sabit zamanlı token karşılaştırması (SHA-256 digest).</summary>
    public bool TokenMatches(string? bearer)
    {
        if (TokenDigest is null || string.IsNullOrEmpty(bearer)) return false;
        var incoming = SHA256.HashData(Encoding.UTF8.GetBytes(bearer));
        return CryptographicOperations.FixedTimeEquals(incoming, TokenDigest);
    }

    public static bool IsLoopbackHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        return IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip);
    }
}
