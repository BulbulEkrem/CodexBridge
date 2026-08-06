using System.Security.Cryptography;
using System.Text.Json;
using CodexBridge.Core.Dashboard;
using CodexBridge.JsHost;
using CodexBridge.JsHost.Cookies;

// Faz 5 fizibilite probu: üst akışın JS sağlayıcı eklentileri V8/ClearScript'te çalışıyor mu?
// SAC dotnet run/test'i engellediğinden bu apphost exe olarak koşulur.

int failed = 0;
void Check(string name, bool ok, string? detail = null)
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(detail != null && !ok ? "  → " + detail : "")}");
    if (!ok) failed++;
}

string pluginsDir = Path.Combine(AppContext.BaseDirectory, "plugins");
string prelude = File.ReadAllText(Path.Combine(pluginsDir, "provider-plugin-prelude.js"));

// === TEST A: gerçek üst-akış eklentisi (xai.js) V8'de mock http ile ===
try
{
    string xai = File.ReadAllText(Path.Combine(pluginsDir, "xai.js"));
    var bridge = new MockBridge(
        settings: new() { ["XAI_TEAM_ID"] = "team123" },
        responder: (url, method, body) =>
        {
            if (url.EndsWith("/prepaid/balance")) return HttpResult.Json(200, """{"total":{"val":"-500"}}""");
            if (url.EndsWith("/usage")) return HttpResult.Json(200, """{"timeSeries":[]}""");
            return HttpResult.Json(404, "{}");
        });

    using var rt = new JsProviderRuntime(prelude, xai, bridge);
    string json = rt.FetchUsageJson();
    using var doc = JsonDocument.Parse(json);
    var root = doc.RootElement;
    double used = root.GetProperty("cost").GetProperty("used").GetDouble();
    string login = root.GetProperty("identity").GetProperty("loginMethod").GetString()!;
    Check("A: xai.js V8'de çalıştı (cost.used = 5)", used == 5.0, $"used={used}");
    Check("A: xai.js identity.loginMethod = Management API", login == "Management API");
    Check("A: mock http en az bir çağrı aldı", bridge.CallCount >= 1, $"calls={bridge.CallCount}");
}
catch (Exception ex)
{
    Check("A: xai.js V8'de çalıştı", false, ex.Message);
}

// === TEST B: primary/secondary/credits/identity'li eklenti + dashboard/v1 eşleme ===
try
{
    const string plugin = """
        defineProvider({
          id: "demo", name: "Demo",
          endpoints: ["https://api.demo.test"],
          settings: [],
          async fetchUsage(ctx) {
            const r = await ctx.http.getJSON("https://api.demo.test/usage");
            return {
              primary: { usedPercent: ctx.pct(r.json.used, r.json.limit), resetsAt: ctx.date.unixSeconds(r.json.reset) },
              secondary: { usedPercent: 61 },
              cost: { balance: 112.5, currency: "credits" },
              identity: { email: "alice@acme.com" },
            };
          },
        });
        """;
    var bridge = new MockBridge(
        settings: new(),
        responder: (url, m, b) => HttpResult.Json(200, """{"used":28,"limit":100,"reset":1789000000}"""));

    using var rt = new JsProviderRuntime(prelude, plugin, bridge);
    ProviderRow row = JsSnapshotMapper.Map(rt.FetchUsageJson(), "demo", "Demo");

    Check("B: iki pencere (session+weekly)", row.Windows.Count == 2);
    Check("B: session usedPercent = 28 (ctx.pct)", row.Windows[0].UsedPercent == 28);
    Check("B: session remaining = 72", row.Windows[0].RemainingPercent == 72);
    Check("B: session resetAt dolu (ctx.date.unixSeconds)", row.Windows[0].ResetAt is not null);
    Check("B: weekly usedPercent = 61", row.Windows[1].UsedPercent == 61);
    Check("B: credits = 112.5 credits", row.Credits?.Remaining == 112.5 && row.Credits?.Unit == "credits");
    Check("B: e-posta MASKELENDİ (redacted@acme.com)", row.Identity?.AccountEmail == "redacted@acme.com");
    Check("B: level ok (max %61 < 75)", row.Status?.Level == StatusLevel.Ok);
}
catch (Exception ex)
{
    Check("B: eşleme + prelude özellikleri", false, ex.Message);
}

// === TEST C: Faz 6 çerez şifre çözme (SENTETİK — gerçek çerezlere DOKUNMADAN) ===
try
{
    byte[] key = RandomNumberGenerator.GetBytes(32);
    byte[] enc = WindowsCookieStore.EncryptV10("session=abc123", key);
    Check("C: v10 önek biçimi doğru", System.Text.Encoding.ASCII.GetString(enc, 0, 3) == "v10");
    Check("C: AES-256-GCM round-trip (Chrome v10)", WindowsCookieStore.TryDecrypt(enc, key) == "session=abc123");
    byte[] wrongKey = RandomNumberGenerator.GetBytes(32);
    Check("C: yanlış anahtar → null (GCM auth)", WindowsCookieStore.TryDecrypt(enc, wrongKey) is null);
}
catch (Exception ex)
{
    Check("C: çerez şifre çözme", false, ex.Message);
}

Console.WriteLine();
Console.WriteLine(failed == 0
    ? "FAZ 5+6 FİZİBİLİTE: JS eklentileri V8'de ÇALIŞIYOR + çerez çözme doğrulandı ✓"
    : $"{failed} PROB BAŞARISIZ ✗");
return failed == 0 ? 0 : 1;

// --- mock host köprüsü ---
sealed class MockBridge(Dictionary<string, string> settings, Func<string, string, string?, HttpResult> responder) : IJsHostBridge
{
    public int CallCount { get; private set; }
    public HttpResult Request(string url, string method, IReadOnlyDictionary<string, string> headers, string? bodyJson, int timeoutSeconds)
    {
        CallCount++;
        return responder(url, method, bodyJson);
    }
    public string? GetSetting(string key, bool secure) => settings.GetValueOrDefault(key);
    public string? GetCookie(string domain) => null;
    public void Log(string message) { }
    public string? CacheGet(string key) => null;
    public void CacheSet(string key, string? value, int ttlSeconds) { }
}
