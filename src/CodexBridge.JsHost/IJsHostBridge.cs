namespace CodexBridge.JsHost;

/// <summary>
/// JS eklentilerin dokuz host API'sinin arkasındaki gerçek platform yeteneği. Test için
/// mock'lanır (mock http), üretimde HttpClient + ayar deposu + çerez katmanı bağlanır.
/// </summary>
public interface IJsHostBridge
{
    HttpResult Request(string url, string method, IReadOnlyDictionary<string, string> headers, string? bodyJson, int timeoutSeconds);
    string? GetSetting(string key, bool secure);
    string? GetCookie(string domain);
    void Log(string message);
    string? CacheGet(string key);
    void CacheSet(string key, string? value, int ttlSeconds);
}

/// <summary>host.http yanıtı. BodyText JSON ise prelude JSON.parse eder.</summary>
public readonly record struct HttpResult(int Status, string HeadersJson, string? BodyText)
{
    public static HttpResult Json(int status, string bodyJson) => new(status, "{}", bodyJson);
}
