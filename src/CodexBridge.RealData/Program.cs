using CodexBridge.Core.Dashboard;
using CodexBridge.JsHost;

// Gerçek veri denemesi: openai.js'i kullanıcının GERÇEK OpenAI anahtarıyla V8'de çalıştırır.
// Anahtar doğrudan User-scope ortam değişkeninden okunur — sohbete/komuta hiç girmez.
// Anahtar ASLA yazdırılmaz.

string? key = Environment.GetEnvironmentVariable("OPENAI_API_KEY", EnvironmentVariableTarget.User)
           ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");

if (string.IsNullOrWhiteSpace(key))
{
    Console.WriteLine("OPENAI_API_KEY bulunamadı (User ortam değişkeni).");
    Console.WriteLine("PowerShell'de ayarla:");
    Console.WriteLine("""  [Environment]::SetEnvironmentVariable("OPENAI_API_KEY", "sk-...", "User")""");
    return 2;
}
Console.WriteLine($"Anahtar okundu (uzunluk {key.Length}, önek {key[..Math.Min(7, key.Length)]}…) — değer yazdırılmıyor.");

string pluginsDir = Path.Combine(AppContext.BaseDirectory, "plugins");
string prelude = File.ReadAllText(Path.Combine(pluginsDir, "provider-plugin-prelude.js"));
string openai = File.ReadAllText(Path.Combine(pluginsDir, "openai.js"));

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
var settings = new Dictionary<string, string>
{
    ["OPENAI_HISTORY_DAYS"] = "30",
    ["OPENAI_PROJECT_ID"] = Environment.GetEnvironmentVariable("OPENAI_PROJECT_ID", EnvironmentVariableTarget.User) ?? "",
};
var secrets = new Dictionary<string, string> { ["OPENAI_API_KEY"] = key };
var bridge = new HttpJsHostBridge(http, settings, secrets, authHeader: ("Authorization", $"Bearer {key}"));

try
{
    using var rt = new JsProviderRuntime(prelude, openai, bridge);
    string json = rt.FetchUsageJson();
    ProviderRow row = JsSnapshotMapper.Map(json, "openai", "OpenAI");

    Console.WriteLine();
    Console.WriteLine("=== GERÇEK OpenAI verisi (dashboard/v1 satırı) ===");
    Console.WriteLine($"Sağlayıcı : {row.Name}  [{row.Status?.Level}]");
    foreach (var w in row.Windows)
        Console.WriteLine($"  Pencere : {w.Label} — %{w.UsedPercent} kullanıldı, kalan %{w.RemainingPercent}, reset {w.ResetAt:u}");
    if (row.Cost is { } c)
        Console.WriteLine($"  Maliyet : bugün ${c.TodayUsd}, son 30 gün ${c.Last30DaysUsd}");
    if (row.Credits is { } cr)
        Console.WriteLine($"  Kredi   : {cr.Remaining} {cr.Unit}");
    if (row.Identity is { } id)
        Console.WriteLine($"  Kimlik  : {id.AccountEmail} (maskeli), plan {id.Plan}");
    if (row.Windows.Count == 0 && row.Cost is null && row.Credits is null)
    {
        Console.WriteLine("  (Eşlenmiş özet boş — ham JSON uzunluğu " + json.Length + " bayt)");
        Console.WriteLine("  Ham JSON ilk 400 karakter: " + json[..Math.Min(400, json.Length)]);
    }
    Console.WriteLine();
    Console.WriteLine("GERÇEK VERİ ALINDI ✓");
    return 0;
}
catch (Exception ex)
{
    Console.WriteLine();
    Console.WriteLine("HATA: " + ex.Message);
    if (ex.Message.Contains("401") || ex.Message.Contains("403"))
    {
        Console.WriteLine("→ OpenAI kullanım/maliyet uç noktaları (/v1/organization/*) ADMIN anahtarı");
        Console.WriteLine("  gerektirir (sk-admin-...). Normal proje anahtarı 401/403 verir.");
        Console.WriteLine("  Admin anahtarı: platform.openai.com → Settings → Organization → Admin keys.");
    }
    return 1;
}
