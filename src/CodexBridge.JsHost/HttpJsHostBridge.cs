using System.Text;

namespace CodexBridge.JsHost;

/// <summary>
/// Üretim host köprüsü: JS eklentilerin HTTP isteklerini gerçek <see cref="HttpClient"/> ile yapar,
/// ayar/sır ve çerezleri sağlanan kaynaklardan okur. V8 iş parçacığında senkron çalışır
/// (her yenileme kısa; isteğe zaman aşımı uygulanır).
/// </summary>
public sealed class HttpJsHostBridge(
    HttpClient http,
    IReadOnlyDictionary<string, string> settings,
    IReadOnlyDictionary<string, string> secrets,
    (string Name, string Value)? authHeader = null,
    Func<string, string?>? cookieResolver = null) : IJsHostBridge
{
    private readonly Dictionary<string, string> _cache = new();

    public HttpResult Request(string url, string method, IReadOnlyDictionary<string, string> headers, string? bodyJson, int timeoutSeconds)
    {
        using var req = new HttpRequestMessage(new HttpMethod(method), url);
        // Eklentinin talep ettiği başlıklar (declared auth başlığını değiştiremez — üst akış kuralı).
        foreach (var (k, v) in headers)
            if (!req.Headers.TryAddWithoutValidation(k, v) && req.Content is not null)
                req.Content.Headers.TryAddWithoutValidation(k, v);
        if (authHeader is { } a)
            req.Headers.TryAddWithoutValidation(a.Name, a.Value);
        if (bodyJson is not null)
            req.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 30)));
        using var resp = http.Send(req, cts.Token);
        string body = new StreamReader(resp.Content.ReadAsStream(cts.Token)).ReadToEnd();

        var headerObj = new StringBuilder("{");
        bool first = true;
        foreach (var h in resp.Headers)
        {
            if (!first) headerObj.Append(',');
            first = false;
            headerObj.Append(System.Text.Json.JsonSerializer.Serialize(h.Key))
                     .Append(':')
                     .Append(System.Text.Json.JsonSerializer.Serialize(string.Join(",", h.Value)));
        }
        headerObj.Append('}');

        return new HttpResult((int)resp.StatusCode, headerObj.ToString(), body);
    }

    public string? GetSetting(string key, bool secure)
        => secure ? secrets.GetValueOrDefault(key) : settings.GetValueOrDefault(key);

    public string? GetCookie(string domain) => cookieResolver?.Invoke(domain);

    public void Log(string message) { /* isteğe bağlı: örnek-kapsamlı log */ }

    public string? CacheGet(string key) => _cache.GetValueOrDefault(key);

    public void CacheSet(string key, string? value, int ttlSeconds)
    {
        if (value is not null) _cache[key] = value; // TTL basitleştirildi (runtime kısa ömürlü)
    }
}
