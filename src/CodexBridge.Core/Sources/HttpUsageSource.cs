using CodexBridge.Core.Dashboard;

namespace CodexBridge.Core.Sources;

/// <summary>
/// Bir <c>dashboard/v1</c> host'undan (kendi HTTP host'umuz, ya da macOS/Linux'ta
/// <c>codexbar serve</c>) snapshot okuyan istemci. Görev çubuğu yüzeyi bunu "host'tan oku"
/// modunda kullanabilir; aynı mantık telefon istemcisinin ve çok-makineli birleşik görünümün
/// çekirdeğidir (tek istemci, üç OS host).
/// </summary>
public sealed class HttpUsageSource(HttpClient http, string baseUrl, string? bearerToken) : IUsageSource
{
    public async Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl.TrimEnd('/')}/dashboard/v1/snapshot");
        if (!string.IsNullOrEmpty(bearerToken))
            req.Headers.Authorization = new("Bearer", bearerToken);

        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        return DashboardSnapshot.FromJson(json)
            ?? throw new InvalidOperationException("dashboard/v1 snapshot ayrıştırılamadı.");
    }
}
