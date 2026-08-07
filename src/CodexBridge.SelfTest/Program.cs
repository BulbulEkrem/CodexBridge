using CodexBridge.Core.Dashboard;
using CodexBridge.Core.Notifications;
using CodexBridge.Core.Refresh;
using CodexBridge.Core.Sources;
using CodexBridge.Core.Sources.WinCodexBar;

// SAC (Smart App Control) enforce modunda `dotnet test` engellenir; apphost exe'ler çalışır.
// Bu yüzden testleri assertion çalıştıran bir konsol olarak koşuyoruz.

int failed = 0;
void Check(string name, bool ok)
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}");
    if (!ok) failed++;
}

// --- 1) AdaptiveRefresh karar tablosu ---
static TimeSpan R(bool low = false, double? sinceMin = null, bool agent = false) =>
    AdaptiveRefresh.Decide(new RefreshContext
    {
        LowPowerOrThermalPressure = low,
        SinceLastInteraction = sinceMin is { } m ? TimeSpan.FromMinutes(m) : null,
        LocalAgentActivityWithin5Min = agent,
    });

Check("adaptive: düşük güç → 30 dk", R(low: true) == TimeSpan.FromMinutes(30));
Check("adaptive: etkileşim 3 dk → 2 dk", R(sinceMin: 3) == TimeSpan.FromMinutes(2));
Check("adaptive: etkileşim 30 dk → 5 dk", R(sinceMin: 30) == TimeSpan.FromMinutes(5));
Check("adaptive: yerel ajan etkinliği → 5 dk", R(agent: true) == TimeSpan.FromMinutes(5));
Check("adaptive: etkileşim 2 sa → 15 dk", R(sinceMin: 120) == TimeSpan.FromMinutes(15));
Check("adaptive: hiç etkileşim yok → 30 dk", R() == TimeSpan.FromMinutes(30));
Check("adaptive: düşük güç, etkileşim 1dk olsa bile → 30 dk (öncelik)", R(low: true, sinceMin: 1) == TimeSpan.FromMinutes(30));

// --- 2) dashboard/v1 JSON round-trip + sözleşme detayları ---
var snap = new DashboardSnapshot
{
    GeneratedAt = DateTimeOffset.Parse("2026-07-16T12:00:00Z"),
    Host = new HostInfo { CodexBarVersion = "t", RefreshIntervalSeconds = 60 },
    Providers =
    [
        new ProviderRow
        {
            Id = "codex", Name = "Codex", Source = "oauth",
            Status = new ProviderStatus { Level = StatusLevel.Ok },
            Windows = [ new RateWindow { Kind = "session", UsedPercent = 28, RemainingPercent = 72 } ],
        },
    ],
};
string js = snap.ToJson();
Check("json: schemaVersion 1", js.Contains("\"schemaVersion\":1"));
Check("json: camelCase usedPercent", js.Contains("\"usedPercent\":28"));
Check("json: enum küçük harf level:ok", js.Contains("\"level\":\"ok\""));
Check("json: null alanlar atlanır (error yok)", !js.Contains("\"error\""));
var back = DashboardSnapshot.FromJson(js);
Check("json: round-trip provider id", back?.Providers[0].Id == "codex");
Check("json: round-trip enum", back?.Providers[0].Status?.Level == StatusLevel.Ok);

// --- 3) WinCodexBarSource.MapUsage (Win-CodexBar /usage → dashboard/v1) ---
const string sampleUsage = """
[
  {
    "provider": "codex",
    "source": "oauth",
    "usage": {
      "primary":   { "used_percent": 42.4, "resets_at": "2026-07-16T17:15:00Z" },
      "secondary": { "used_percent": 61.0, "resets_at": "2026-07-20T00:00:00Z" }
    },
    "cost": { "total_usd": 18.22, "currency": "USD" }
  },
  { "provider": "claude", "error": "not configured" }
]
""";
var rows = WinCodexBarSource.MapUsage(sampleUsage, DateTimeOffset.UtcNow);
Check("map: iki satır", rows.Count == 2);
Check("map: codex id/name", rows[0].Id == "codex" && rows[0].Name == "Codex");
Check("map: codex iki pencere (session+weekly)", rows[0].Windows.Count == 2);
Check("map: session usedPercent 42.4", rows[0].Windows[0].UsedPercent == 42.4);
Check("map: session remaining 57.6", rows[0].Windows[0].RemainingPercent == 57.6);
Check("map: weekly kind", rows[0].Windows[1].Kind == "weekly");
Check("map: cost last30 = 18.22", rows[0].Cost?.Last30DaysUsd == 18.22);
Check("map: hata satırı error taşır", rows[1].Error?.Message == "not configured");
Check("map: hata satırı level unknown", rows[1].Status?.Level == StatusLevel.Unknown);

// --- 4) NotificationEngine (Faz 7) — eşik geçişleri ---
static DashboardSnapshot Snap(params ProviderRow[] providers) => new()
{
    GeneratedAt = DateTimeOffset.Parse("2026-08-07T12:00:00Z"),
    Host = new HostInfo { RefreshIntervalSeconds = 60 },
    Providers = providers,
};
static ProviderRow Prov(string id, double? used, string? error = null) => new()
{
    Id = id, Name = char.ToUpper(id[0]) + id[1..],
    Status = new ProviderStatus { Level = StatusLevel.Ok },
    Windows = used is { } u ? [ new RateWindow { Kind = "session", UsedPercent = u, RemainingPercent = 100 - u } ] : [],
    Error = error is null ? null : new ProviderError { Message = error },
};
static IReadOnlyList<NotificationEvent> Diff(DashboardSnapshot? prev, DashboardSnapshot cur)
    => NotificationEngine.Diff(prev, cur, NotificationThresholds.Default);

Check("notif: ilk snapshot (prev=null) → olay yok", Diff(null, Snap(Prov("codex", 95))).Count == 0);

var warnEv = Diff(Snap(Prov("codex", 60)), Snap(Prov("codex", 80)));
Check("notif: 60→80 uyarı geçişi → 1 olay", warnEv.Count == 1);
Check("notif: uyarı türü QuotaWarning", warnEv.Count == 1 && warnEv[0].Kind == NotificationKind.QuotaWarning);

var critEv = Diff(Snap(Prov("codex", 80)), Snap(Prov("codex", 95)));
Check("notif: 80→95 kritik geçişi → QuotaCritical", critEv.Count == 1 && critEv[0].Kind == NotificationKind.QuotaCritical);

Check("notif: 95→96 yüksekte kalış → olay yok (kenar tetikleme)",
    Diff(Snap(Prov("codex", 95)), Snap(Prov("codex", 96))).Count == 0);

var resetEv = Diff(Snap(Prov("codex", 95)), Snap(Prov("codex", 10)));
Check("notif: 95→10 sıfırlama → QuotaReset", resetEv.Count == 1 && resetEv[0].Kind == NotificationKind.QuotaReset);

var errEv = Diff(Snap(Prov("codex", 30)), Snap(Prov("codex", 30, error: "boom")));
Check("notif: sağlıklı→hata → ProviderError", errEv.Count == 1 && errEv[0].Kind == NotificationKind.ProviderError);

var recEv = Diff(Snap(Prov("codex", 30, error: "boom")), Snap(Prov("codex", 30)));
Check("notif: hata→sağlıklı → ProviderRecovered", recEv.Count == 1 && recEv[0].Kind == NotificationKind.ProviderRecovered);

Check("notif: dedupeKey uyarı≠kritik (ikisi de gönderilebilsin)",
    warnEv[0].DedupeKey != critEv[0].DedupeKey);

Check("notif: kritik geçiş 60→95 tek olay (uyarı gölgelenir)",
    Diff(Snap(Prov("codex", 60)), Snap(Prov("codex", 95))).Count == 1);

Console.WriteLine();
Console.WriteLine(failed == 0 ? "TÜM TESTLER GEÇTI ✓" : $"{failed} TEST BAŞARISIZ ✗");
return failed == 0 ? 0 : 1;
