using CodexBridge.Core.Providers;
using CodexBridge.Core.Settings;
using CodexBridge.Core.Sources;

// Canlı sağlayıcı probu — Claude ve Codex kotalarını GERÇEK kimlikle çeker.
// API anahtarı istemez: yereldeki CLI OAuth token'larını okur.
// Token okunur ama ASLA yazdırılmaz; yalnızca yüzdeler ve reset saatleri gösterilir.

using var http = ProviderFactory.CreateHttpClient();
var settings = AppSettings.Load();
var sources = ProviderFactory.CreateEnabled(settings, http);

if (sources.Count == 0)
{
    Console.WriteLine("Hiçbir sağlayıcı etkin değil (settings.json → enabledProviders).");
    return 1;
}

var aggregate = new AggregateUsageSource(sources, version: "codexbridge-probe");
var snapshot = await aggregate.GetSnapshotAsync();

Console.WriteLine("=== CANLI kota (dashboard/v1, API anahtarı olmadan) ===");
Console.WriteLine($"Üretildi: {snapshot.GeneratedAt:u}");
Console.WriteLine();

int failures = 0;
foreach (var p in snapshot.Providers)
{
    Console.WriteLine($"{p.Name}  [{p.Status?.Level}]  plan: {p.Identity?.Plan ?? "?"}  kaynak: {p.Source ?? "-"}");

    if (p.Error is { } err)
    {
        Console.WriteLine($"  HATA ({err.Code}): {err.Message}");
        failures++;
    }

    if (p.Windows.Count == 0 && p.Error is null)
        Console.WriteLine("  (aktif kota penceresi yok)");

    foreach (var w in p.Windows)
    {
        string reset = RateWindowFactory.FormatCountdown(w.ResetAt, snapshot.GeneratedAt) ?? "?";
        Console.WriteLine($"  {w.Label,-16} %{w.UsedPercent,-5} kalan %{w.RemainingPercent,-5} reset {reset}");
    }

    if (p.MostRestrictive() is { } worst)
        Console.WriteLine($"  → en kısıtlayıcı: {worst.Label} %{worst.UsedPercent}");

    Console.WriteLine();
}

Console.WriteLine(failures == 0
    ? "GERÇEK VERİ ALINDI ✓"
    : $"{failures} sağlayıcı hata verdi (yukarıdaki mesajlara bak).");
return failures == 0 ? 0 : 1;
