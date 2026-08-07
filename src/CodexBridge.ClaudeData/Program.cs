using CodexBridge.Core.Sources;

// Gerçek Claude verisi — API anahtarı YOK. Yereldeki OAuth token'ıyla /api/oauth/usage çağrılır.
// Token okunur ama ASLA yazdırılmaz; yalnızca kullanım yüzdeleri gösterilir.

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
var source = new ClaudeOAuthSource(http);

try
{
    var snap = await source.GetSnapshotAsync();
    var claude = snap.Providers[0];

    Console.WriteLine("=== GERÇEK Claude kullanımı (dashboard/v1, API anahtarı olmadan) ===");
    Console.WriteLine($"Sağlayıcı : {claude.Name}  [{claude.Status?.Level}]  plan: {claude.Identity?.Plan ?? "?"}");
    if (claude.Windows.Count == 0)
        Console.WriteLine("  (pencere yok — hesapta aktif kota penceresi olmayabilir)");
    foreach (var w in claude.Windows)
        Console.WriteLine($"  {w.Label,-14}: %{w.UsedPercent} kullanıldı · kalan %{w.RemainingPercent} · reset {w.ResetAt:u}");
    Console.WriteLine();
    Console.WriteLine("GERÇEK VERİ ALINDI ✓ (kaynak: yerel OAuth token → api.anthropic.com/api/oauth/usage)");
    return 0;
}
catch (Exception ex)
{
    Console.WriteLine("HATA: " + ex.Message);
    if (ex.Message.Contains("401"))
        Console.WriteLine("→ Token süresi dolmuş olabilir. Bir Claude Code oturumu açıp kapatınca yenilenir.");
    return 1;
}
