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
