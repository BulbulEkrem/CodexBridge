using System.Text.Json;
using Microsoft.ClearScript;
using Microsoft.ClearScript.V8;

namespace CodexBridge.JsHost;

/// <summary>
/// Üst akışın JS sağlayıcı eklentilerini V8'de (ClearScript) çalıştırır. Swift/JavaScriptCore
/// runtime'ının (ProviderPluginRuntime.swift) birebir C# karşılığı:
///   1. <c>defineProvider</c> global'i tanımlanır (eklenti tanımını yakalar).
///   2. Prelude eval edilir → <c>applyPrelude(ctx, host)</c> fonksiyonu.
///   3. Eklenti kaynağı eval edilir (defineProvider'ı çağırır).
///   4. <c>fetchUsage(ctx)</c> çağrılır; sonuç JSON.stringify ile düz JSON'a çevrilir.
///
/// <c>host</c> nesnesi JS tarafında C# delegate'lerine bağlanır (ClearScript member-adı
/// eşlemesine güvenmeden — sağlam yol). Bu, araştırmanın #4 açık sorusunu yanıtlar:
/// eklentiler JavaScriptCore'a değil standart ES + Intl'e dayanır → V8'de çalışır.
/// </summary>
public sealed class JsProviderRuntime : IDisposable
{
    private readonly V8ScriptEngine _engine;
    private readonly IJsHostBridge _bridge;

    public JsProviderRuntime(string preludeSource, string pluginSource, IJsHostBridge bridge)
    {
        _bridge = bridge;
        _engine = new V8ScriptEngine(V8ScriptEngineFlags.EnableDateTimeConversion);

        // JS yardımcıları: host yanıtı kurma + opts normalizasyonu.
        _engine.Execute("""
            globalThis.__mkResp = function (status, headersJson, bodyText, wantJSON) {
                var headers = headersJson ? JSON.parse(headersJson) : {};
                return wantJSON
                    ? { status: status, headers: headers, json: bodyText ? JSON.parse(bodyText) : null }
                    : { status: status, headers: headers, bodyText: bodyText || "" };
            };
            globalThis.__optsToJson = function (o) {
                if (!o) return "{}";
                var out = {};
                if (o.headers) out.headers = o.headers;
                if (o.bodyJSON !== undefined) out.bodyJSON = o.bodyJSON;
                if (o.timeoutSeconds !== undefined) out.timeoutSeconds = o.timeoutSeconds;
                return JSON.stringify(out);
            };
            """);

        // Host yeteneklerini C# delegate'leri olarak enjekte et.
        _engine.Script.__hostHttp = (Action<string, object, string, bool, object, object>)HostHttp;
        _engine.Script.__hostSettingGet = (Func<string, bool, object?>)((k, s) => _bridge.GetSetting(k, s));
        _engine.Script.__hostCookie = (Action<string, object, object>)HostCookie;
        _engine.Script.__hostLog = (Action<string>)(m => _bridge.Log(m));
        _engine.Script.__hostCacheGet = (Func<string, object?>)(k => _bridge.CacheGet(k));
        _engine.Script.__hostCacheSet = (Action<string, object?, double>)((k, v, t) => _bridge.CacheSet(k, v?.ToString(), (int)t));

        // host nesnesini JS tarafında kur (prelude'un beklediği imzalarla).
        _engine.Execute("""
            globalThis.__host = {
                http: function (url, opts, method, wantJSON, resolve, reject) { __hostHttp(url, opts, method, wantJSON, resolve, reject); },
                settingGet: function (key, secure) { return __hostSettingGet(key, secure); },
                cookieHeader: function (domain, resolve, reject) { __hostCookie(domain, resolve, reject); },
                log: function (message) { __hostLog(message); },
                cacheGet: function (key) { return __hostCacheGet(key); },
                cacheSet: function (key, value, ttl) { __hostCacheSet(key, value, ttl); },
            };
            """);

        // defineProvider: eklenti tanımını yakala.
        _engine.Script.__captureDefinition = (Action<object>)(def => _engine.Script.__def = def);
        _engine.Execute("globalThis.defineProvider = function (def) { __captureDefinition(def); };");

        // Prelude → applyPrelude fonksiyonu.
        _engine.Script.__applyPrelude = _engine.Evaluate(preludeSource);

        // Eklenti kaynağı → defineProvider çağrılır.
        _engine.Execute(pluginSource);

        if (_engine.Script.__def is Undefined)
            throw new JsProviderException("eklenti defineProvider(...) çağırmadı");
    }

    /// <summary>fetchUsage(ctx)'i çalıştırır ve sonucu düz JSON string olarak döndürür.</summary>
    public string FetchUsageJson()
    {
        string? json = null;
        string? error = null;
        _engine.Script.__resolveFetch = (Action<object?>)(v => json = v as string);
        _engine.Script.__rejectFetch = (Action<object?>)(e => error = e?.ToString());

        // Async IIFE: ctx kur, fetchUsage'ı bekle, sonucu düz JSON'a çevir.
        // Mock/senkron host.http ile tüm promise'ler microtask drenajında çözülür.
        _engine.Execute("""
            (async () => {
                try {
                    const ctx = __applyPrelude({}, __host);
                    const result = await __def.fetchUsage(ctx);
                    __resolveFetch(JSON.stringify(result || null));
                } catch (e) {
                    __rejectFetch(String((e && e.stack) || e));
                }
            })();
            """);

        if (error != null) throw new JsProviderException(error);
        if (json == null) throw new JsProviderException("fetchUsage bir sonuç döndürmedi (async çözülmedi?)");
        return json;
    }

    private void HostHttp(string url, object opts, string method, bool wantJson, object resolve, object reject)
    {
        try
        {
            var (headers, bodyJson, timeout) = ReadOpts(opts);
            HttpResult res = _bridge.Request(url, method, headers, bodyJson, timeout);
            dynamic mk = ((dynamic)_engine.Script).__mkResp;
            var response = mk(res.Status, res.HeadersJson, res.BodyText, wantJson);
            ((dynamic)resolve)(response);
        }
        catch (Exception ex)
        {
            ((dynamic)reject)(ex.Message);
        }
    }

    private void HostCookie(string domain, object resolve, object reject)
    {
        try { ((dynamic)resolve)(_bridge.GetCookie(domain)); }
        catch (Exception ex) { ((dynamic)reject)(ex.Message); }
    }

    private (IReadOnlyDictionary<string, string>, string?, int) ReadOpts(object opts)
    {
        string json = ((dynamic)_engine.Script).__optsToJson(opts);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("headers", out var h) && h.ValueKind == JsonValueKind.Object)
            foreach (var p in h.EnumerateObject())
                headers[p.Name] = p.Value.ToString();

        string? bodyJson = root.TryGetProperty("bodyJSON", out var b) ? b.GetString() : null;
        int timeout = root.TryGetProperty("timeoutSeconds", out var t) && t.TryGetInt32(out var ts) ? ts : 15;
        return (headers, bodyJson, timeout);
    }

    public void Dispose() => _engine.Dispose();
}

public sealed class JsProviderException(string message) : Exception(message);
