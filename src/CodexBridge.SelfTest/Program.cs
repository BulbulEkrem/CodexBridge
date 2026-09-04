using CodexBridge.Core;
using CodexBridge.Core.Providers;
using CodexBridge.Core.Refresh;
using CodexBridge.Core.Rendering;
using CodexBridge.Core.Security;
using CodexBridge.Core.Settings;
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

// ══════════════════════════════════════════════════════════════════════
//  VERİ KATMANI
// ══════════════════════════════════════════════════════════════════════

// Testler diske yazıyor: gerçek kullanıcı profiline dokunmamak için geçici köke yönlendir.
string testRoot = Path.Combine(Path.GetTempPath(), "codexbridge-selftest-" + Guid.NewGuid().ToString("N")[..8]);
AppPaths.OverrideRoot(testRoot);

var t0 = DateTimeOffset.Parse("2026-09-04T12:00:00Z");
var testClock = new TestClock { Now = t0 };

// Beklenen sağlayıcı hatasını doğrulayan yardımcılar.
async Task ExpectRateLimited(IProviderSource src)
{
    try
    {
        await src.FetchAsync();
        Check("hız sınırı: istisna bekleniyordu", false);
    }
    catch (ProviderSourceException ex) when (ex.Kind != ProviderErrorKind.RateLimited)
    {
        Check($"hız sınırı: RateLimited bekleniyordu, {ex.Kind} geldi", false);
    }
    catch (ProviderSourceException) { /* beklenen */ }
}

async Task ExpectProviderError(IProviderSource src, ProviderErrorKind kind, string name)
{
    try
    {
        await src.FetchAsync();
        Check(name + " (istisna atılmadı)", false);
    }
    catch (ProviderSourceException ex)
    {
        Check(name, ex.Kind == kind);
    }
}

// --- Kota penceresi yardımcıları ---
Check("pencere: yüzde 100'e kelepçeleniyor",
    RateWindowFactory.Create(WindowKinds.Session, "x", 143, null).UsedPercent == 100);
Check("pencere: negatif yüzde 0'a çekiliyor",
    RateWindowFactory.Create(WindowKinds.Session, "x", -5, null).UsedPercent == 0);
Check("pencere: kalan = 100 - kullanılan (tek ondalık)",
    RateWindowFactory.Create(WindowKinds.Session, "x", 42.34, null).RemainingPercent == 57.7);

Check("geri sayım: 3 saat → '3s 0dk'", RateWindowFactory.FormatCountdown(t0.AddHours(3), t0) == "3s 0dk");
Check("geri sayım: 2 gün 3 saat → '2g 3sa'", RateWindowFactory.FormatCountdown(t0.AddDays(2).AddHours(3), t0) == "2g 3sa");
Check("geri sayım: 45 dakika → '45dk'", RateWindowFactory.FormatCountdown(t0.AddMinutes(45), t0) == "45dk");
Check("geri sayım: geçmiş → 'şimdi'", RateWindowFactory.FormatCountdown(t0.AddMinutes(-1), t0) == "şimdi");
Check("geri sayım: reset yoksa null", RateWindowFactory.FormatCountdown(null, t0) is null);

// --- En kısıtlayıcı pencere: band'ın pill rengini belirleyen kural ---
var twoWindows = new List<CodexBridge.Core.Dashboard.RateWindow>
{
    RateWindowFactory.Create(WindowKinds.Session, "Oturum", 47, t0.AddHours(3)),
    RateWindowFactory.Create(WindowKinds.Weekly, "Haftalık", 78, t0.AddDays(3)),
};
var claudeRow = ProviderRowFactory.Create(ProviderIds.Claude, "oauth", twoWindows, t0, plan: "Max 20x");

Check("en kısıtlayıcı = haftalık (%78 > %47)", claudeRow.MostRestrictive()!.Kind == WindowKinds.Weekly);
Check("pencere türe göre bulunuyor", claudeRow.Window(WindowKinds.Session)!.UsedPercent == 47);
Check("olmayan pencere null", claudeRow.Window(WindowKinds.Monthly) is null);
Check("seviye: %78 → Warning", claudeRow.Status!.Level == StatusLevel.Warning);
Check("seviye: %91 → Critical", ProviderRowFactory.Create(ProviderIds.Codex, "oauth",
    [RateWindowFactory.Create(WindowKinds.Weekly, "Haftalık", 91, null)], t0).Status!.Level == StatusLevel.Critical);
Check("seviye: %18 → Ok", ProviderRowFactory.Create(ProviderIds.Codex, "oauth",
    [RateWindowFactory.Create(WindowKinds.Session, "Oturum", 18, null)], t0).Status!.Level == StatusLevel.Ok);
Check("meta: accent + sıra sağlayıcıdan geliyor",
    claudeRow.Display!.AccentColor == "#D97757" && claudeRow.Display.SortKey == 0);

// --- Toplayıcı: hata izolasyonu ---
var okSource = new FakeProviderSource(ProviderIds.Claude, () => claudeRow);
var badSource = new FakeProviderSource(ProviderIds.Codex,
    () => throw new ProviderSourceException(ProviderErrorKind.NoCredentials, "Codex kimliği yok."));
var agg = new AggregateUsageSource([okSource, badSource], testClock);
var snap1 = await agg.GetSnapshotAsync();

Check("toplayıcı: iki satır da geliyor", snap1.Providers.Count == 2);
Check("toplayıcı: sağlam sağlayıcı etkilenmiyor",
    snap1.Providers.First(p => p.Id == ProviderIds.Claude).Error is null);
Check("toplayıcı: hatalı satır error taşıyor",
    snap1.Providers.First(p => p.Id == ProviderIds.Codex).Error?.Code == "NoCredentials");
Check("toplayıcı: satırlar sortKey'e göre sıralı",
    snap1.Providers[0].Id == ProviderIds.Claude && snap1.Providers[1].Id == ProviderIds.Codex);

// --- Toplayıcı: son bilinen değer korunuyor (429 davranışı) ---
var flaky = new FakeProviderSource(ProviderIds.Claude, () => claudeRow);
var agg2 = new AggregateUsageSource([flaky], testClock);
await agg2.GetSnapshotAsync();

flaky.Next = () => throw new ProviderSourceException(
    ProviderErrorKind.RateLimited, "Hız sınırı.", TimeSpan.FromMinutes(5));
testClock.Now = t0.AddMinutes(10);
var degraded = (await agg2.GetSnapshotAsync()).Providers[0];

Check("429: son bilinen pencereler korunuyor", degraded.Windows.Count == 2);
Check("429: yüzde değişmiyor", degraded.Window(WindowKinds.Weekly)!.UsedPercent == 78);
Check("429: hata etiketi ekleniyor", degraded.Error?.Code == "RateLimited");
Check("429: updatedAt eskide kalıyor (veri yaşı dürüst)", degraded.UpdatedAt == t0);
testClock.Now = t0;

// --- Hız sınırı sarmalayıcısı ---
var rlClock = new TestClock { Now = t0 };
var rlSource = new FakeProviderSource(ProviderIds.Codex,
    () => throw new ProviderSourceException(ProviderErrorKind.RateLimited, "429", TimeSpan.FromMinutes(5)));
var limited = new RateLimitedSource(rlSource, rlClock);

await ExpectRateLimited(limited);
Check("hız sınırı: pencere kapandı", limited.BlockedFor is not null);
Check("hız sınırı: Retry-After'a uyuyor (5 dk)",
    limited.BlockedFor is { } b && Math.Abs(b.TotalMinutes - 5) < 0.01);

int callsBefore = rlSource.Calls;
await ExpectRateLimited(limited);
Check("hız sınırı: pencere kapalıyken sağlayıcıya İSTEK ATILMIYOR", rlSource.Calls == callsBefore);

rlClock.Now = t0.AddMinutes(6);
Check("hız sınırı: süre dolunca pencere açılıyor", limited.BlockedFor is null);

// Retry-After yoksa üstel geri çekilme
var expClock = new TestClock { Now = t0 };
var expSource = new FakeProviderSource(ProviderIds.Codex,
    () => throw new ProviderSourceException(ProviderErrorKind.RateLimited, "429"));
var expLimited = new RateLimitedSource(expSource, expClock);

await ExpectRateLimited(expLimited);
Check("üstel: ilk geri çekilme 2 dk",
    expLimited.BlockedFor is { } e1 && Math.Abs(e1.TotalMinutes - 2) < 0.01);
expClock.Now = t0.AddMinutes(3);
await ExpectRateLimited(expLimited);
Check("üstel: ikinci geri çekilme 4 dk",
    expLimited.BlockedFor is { } e2 && Math.Abs(e2.TotalMinutes - 4) < 0.01);

// Başarılı çekim geri çekilmeyi sıfırlar
expClock.Now = t0.AddHours(1);
expSource.Next = () => claudeRow;
await expLimited.FetchAsync();
expSource.Next = () => throw new ProviderSourceException(ProviderErrorKind.RateLimited, "429");
await ExpectRateLimited(expLimited);
Check("üstel: başarılı çekimden sonra 2 dk'ya sıfırlanıyor",
    expLimited.BlockedFor is { } e3 && Math.Abs(e3.TotalMinutes - 2) < 0.01);

// --- Snapshot deposu: atomik yazım + gidiş-dönüş ---
var store = new SnapshotStore(Path.Combine(testRoot, "snapshot.json"));
Check("snapshot: dosya yokken null", store.Read() is null);
store.Write(snap1);
var readBack = store.Read();
Check("snapshot: gidiş-dönüş sağlayıcı sayısı", readBack?.Providers.Count == 2);
Check("snapshot: pencere yüzdesi korunuyor",
    readBack!.Providers.First(p => p.Id == ProviderIds.Claude).Window(WindowKinds.Weekly)!.UsedPercent == 78);
Check("snapshot: yazımdan sonra .tmp artığı kalmıyor",
    Directory.GetFiles(testRoot, "*.tmp").Length == 0);
store.Write(snap1);
Check("snapshot: üzerine yazım çalışıyor", store.Read()?.Providers.Count == 2);

// --- Ayarlar ---
string settingsPath = Path.Combine(testRoot, "settings.json");
Check("ayar: dosya yoksa varsayılan", AppSettings.Load(settingsPath).EnabledProviders.Count == 2);

var messy = new AppSettings
{
    WarnPercent = 95, CritPercent = 50,       // kritik uyarının altında — düzeltilmeli
    MinRefreshSeconds = 5, MaxRefreshSeconds = 2,
    EnabledProviders = ["claude", "claude", "gemini"],
}.Normalized();

Check("ayar: kritik eşik uyarının üstüne çekiliyor", messy.CritPercent > messy.WarnPercent);
Check("ayar: min yenileme 60 sn'nin altına inmiyor", messy.MinRefreshSeconds == 60);
Check("ayar: max < min ise min'e yükseliyor", messy.MaxRefreshSeconds >= messy.MinRefreshSeconds);
Check("ayar: bilinmeyen sağlayıcı düşüyor", !messy.EnabledProviders.Contains("gemini"));
Check("ayar: tekrar eden sağlayıcı tekilleşiyor", messy.EnabledProviders.Count == 1);

messy.Save(settingsPath);
Check("ayar: diskten geri okunuyor", AppSettings.Load(settingsPath).MinRefreshSeconds == 60);
Check("ayar: IsEnabled çalışıyor",
    new AppSettings { EnabledProviders = [ProviderIds.Codex] }.IsEnabled(ProviderIds.Codex));

// --- Sır deposu ve token önbelleği ---
var secrets = new FileSecretStore(Path.Combine(testRoot, "secrets"));
Check("sır: olmayan anahtar null", secrets.Read("yok") is null);
secrets.Write("deneme", "gizli-değer");
Check("sır: gidiş-dönüş", secrets.Read("deneme") == "gizli-değer");
secrets.Delete("deneme");
Check("sır: silme çalışıyor", secrets.Read("deneme") is null);

var tokenCache = new OAuthTokenCache(secrets);
Check("token: boş önbellek null", tokenCache.Get(ProviderIds.Claude) is null);
tokenCache.Put(ProviderIds.Claude, new CachedToken("abc", t0.AddHours(8), "refresh-1"));
Check("token: geri okunuyor", tokenCache.Get(ProviderIds.Claude)?.AccessToken == "abc");
Check("token: yeni örnek diskten okuyor",
    new OAuthTokenCache(secrets).Get(ProviderIds.Claude)?.RefreshToken == "refresh-1");
Check("token: 8 saat sonrası için taze",
    tokenCache.Get(ProviderIds.Claude)!.IsExpiring(t0, TimeSpan.FromMinutes(5)) == false);
Check("token: bitişe 2 dk kala yenilenmeli",
    tokenCache.Get(ProviderIds.Claude)!.IsExpiring(t0.AddHours(8).AddMinutes(-2), TimeSpan.FromMinutes(5)));
tokenCache.Clear(ProviderIds.Claude);
Check("token: temizleniyor", tokenCache.Get(ProviderIds.Claude) is null);

// --- Claude eşlemesi (sahte HTTP) ---
string claudeBody = """
{"five_hour":{"utilization":0.47,"resets_at":"2026-09-04T15:00:00Z"},
 "seven_day":{"utilization":78,"resets_at":"2026-09-07T09:00:00Z"},
 "seven_day_opus":{"utilization":0.12,"resets_at":"2026-09-07T09:00:00Z"}}
""";
string credPath = Path.Combine(testRoot, "claude-creds.json");
// Ham dizede süslü parantez kaçışıyla uğraşmamak için zaman damgası yer tutucuyla konuyor.
File.WriteAllText(credPath, """
{"claudeAiOauth":{"accessToken":"at-1","refreshToken":"rt-1",
 "expiresAt":EXPIRES_AT,"subscriptionType":"max"}}
""".Replace("EXPIRES_AT", t0.AddHours(4).ToUnixTimeMilliseconds().ToString()));

using var claudeHttp = new HttpClient(new StubHandler(claudeBody));
var claudeSource = new ClaudeUsageSource(claudeHttp, new OAuthTokenCache(secrets), credPath, testClock);
var claudeFetched = await claudeSource.FetchAsync();

Check("claude: üç pencere de çekiliyor", claudeFetched.Windows.Count == 3);
Check("claude: kesir kullanım yüzdeye çevriliyor (0.47 → %47)",
    claudeFetched.Window(WindowKinds.Session)!.UsedPercent == 47);
Check("claude: zaten yüzde olan değer bozulmuyor (78 → %78)",
    claudeFetched.Window(WindowKinds.Weekly)!.UsedPercent == 78);
Check("claude: opus penceresi ayrı türde", claudeFetched.Window(WindowKinds.Opus)!.UsedPercent == 12);
Check("claude: reset zamanı ayrıştırılıyor",
    claudeFetched.Window(WindowKinds.Session)!.ResetAt == DateTimeOffset.Parse("2026-09-04T15:00:00Z"));
Check("claude: plan kimliğe yazılıyor", claudeFetched.Identity?.Plan == "max");
Check("claude: en kısıtlayıcı haftalık", claudeFetched.MostRestrictive()!.Kind == WindowKinds.Weekly);

using var noAuthHttp = new HttpClient(new StubHandler(claudeBody));
await ExpectProviderError(
    new ClaudeUsageSource(noAuthHttp, new OAuthTokenCache(secrets), Path.Combine(testRoot, "yok.json"), testClock),
    ProviderErrorKind.NoCredentials, "claude: kimlik dosyası yoksa NoCredentials");

// --- Codex eşlemesi (sahte HTTP) ---
string codexAuth = Path.Combine(testRoot, "codex-auth.json");
File.WriteAllText(codexAuth, """
{"tokens":{"access_token":"ct-1","account_id":"acct_9","plan_type":"plus"}}
""");
string codexBody = """
{"rate_limits":{
  "primary_window":{"used_percent":18,"limit_window_seconds":18000,"reset_at":SESSION_RESET},
  "secondary_window":{"used_percent":91,"limit_window_seconds":604800,"reset_at":WEEKLY_RESET}}}
"""
    .Replace("SESSION_RESET", t0.AddHours(4).ToUnixTimeSeconds().ToString())
    .Replace("WEEKLY_RESET", t0.AddDays(3).ToUnixTimeSeconds().ToString());

using var codexHttp = new HttpClient(new StubHandler(codexBody));
var codexFetched = await new CodexUsageSource(codexHttp, codexAuth, clock: testClock).FetchAsync();

Check("codex: iki pencere", codexFetched.Windows.Count == 2);
Check("codex: 18000 sn → oturum penceresi",
    codexFetched.Window(WindowKinds.Session)!.UsedPercent == 18);
Check("codex: 604800 sn → haftalık pencere",
    codexFetched.Window(WindowKinds.Weekly)!.UsedPercent == 91);
Check("codex: reset epoch'tan çözülüyor",
    codexFetched.Window(WindowKinds.Session)!.ResetAt == t0.AddHours(4));
Check("codex: plan okunuyor", codexFetched.Identity?.Plan == "plus");
Check("codex: %91 → Critical", codexFetched.Status!.Level == StatusLevel.Critical);

// Süre bilgisi yoksa sıraya düşüyor
using var codexArrHttp = new HttpClient(new StubHandler(
    """{"rate_limits":{"windows":[{"used_percent":30},{"used_percent":60}]}}"""));
var codexArr = await new CodexUsageSource(codexArrHttp, codexAuth, clock: testClock).FetchAsync();
Check("codex: dizi biçimi destekleniyor", codexArr.Windows.Count == 2);
Check("codex: süre yoksa ilki oturum", codexArr.Window(WindowKinds.Session)!.UsedPercent == 30);
Check("codex: süre yoksa ikincisi haftalık", codexArr.Window(WindowKinds.Weekly)!.UsedPercent == 60);

using var codex401 = new HttpClient(new StubHandler("{}", System.Net.HttpStatusCode.Unauthorized));
await ExpectProviderError(new CodexUsageSource(codex401, codexAuth, clock: testClock),
    ProviderErrorKind.AuthExpired, "codex: 401 → AuthExpired (codex login yönlendirmesi)");

using var codex429 = new HttpClient(new StubHandler("{}", System.Net.HttpStatusCode.TooManyRequests));
await ExpectProviderError(new CodexUsageSource(codex429, codexAuth, clock: testClock),
    ProviderErrorKind.RateLimited, "codex: 429 → RateLimited");

File.WriteAllText(Path.Combine(testRoot, "apikey-auth.json"), """{"OPENAI_API_KEY":"sk-x"}""");
using var codexApiKeyHttp = new HttpClient(new StubHandler(codexBody));
await ExpectProviderError(
    new CodexUsageSource(codexApiKeyHttp, Path.Combine(testRoot, "apikey-auth.json"), clock: testClock),
    ProviderErrorKind.NoCredentials, "codex: API anahtarı modunda abonelik kotası yok");

// --- Yenileme koordinatörü ---
var coordClock = new TestClock { Now = t0 };
var coordStore = new SnapshotStore(Path.Combine(testRoot, "coord-snapshot.json"));
var coordSettings = new AppSettings { MinRefreshSeconds = 300, MaxRefreshSeconds = 600 }.Normalized();
var coord = new RefreshCoordinator(
    new AggregateUsageSource([new FakeProviderSource(ProviderIds.Claude, () => claudeRow)], coordClock),
    coordStore, () => coordSettings, clock: coordClock);

Check("koordinatör: etkileşimsiz uzun aralık üst sınıra kelepçeleniyor",
    coord.NextDelay() == TimeSpan.FromSeconds(600));
coord.NoteInteraction();
Check("koordinatör: etkileşim sonrası alt sınıra kelepçeleniyor (2 dk < 5 dk)",
    coord.NextDelay() == TimeSpan.FromSeconds(300));

int updatedCount = 0;
coord.Updated += _ => updatedCount++;
var coordSnap = await coord.RefreshNowAsync();
Check("koordinatör: snapshot üretiliyor", coordSnap.Providers.Count == 1);
Check("koordinatör: abone uyarılıyor", updatedCount == 1);
Check("koordinatör: snapshot diske yazılıyor", coordStore.Read()?.Providers.Count == 1);
Check("koordinatör: yenileme aralığı snapshot'a yazılıyor",
    coordSnap.Host.RefreshIntervalSeconds == 300);
Check("koordinatör: Current güncelleniyor", coord.Current is not null);

// --- Tepsi ikonu çizimi ---
// Yerleşim: kenar payı 3, çubuk payı 4 → çubuklar x=7..25. İki çubuk dikey ortada:
// oturum y=7..14, haftalık y=18..25. Üst çubuğun oturum olması band'la tutarlılık için şart.
byte[] icon = TrayIconRenderer.Render(100, 0, MeterLevel.Ok, MeterLevel.Ok);
Check("ikon: 32x32 BGRA bayt uzunluğu", icon.Length == 32 * 32 * 4);
Check("ikon: köşe şeffaf (kenar payı)", TrayIconRenderer.PixelAt(icon, 0, 0).A == 0);
Check("ikon: kart opak", TrayIconRenderer.PixelAt(icon, 16, 4).A == 0xFF);

var topFill = TrayIconRenderer.PixelAt(icon, 24, 10);
Check("ikon: ÜST çubuk oturum (%100 → sağ uca kadar dolu)",
    (topFill.B, topFill.G, topFill.R) == (0x5F, 0xCB, 0x6C));
var bottomTrack = TrayIconRenderer.PixelAt(icon, 24, 21);
Check("ikon: ALT çubuk haftalık (%0 → yatak rengi)",
    (bottomTrack.B, bottomTrack.G, bottomTrack.R) == (0x50, 0x50, 0x5A));
Check("ikon: %0 bile 1 piksel çiziliyor (veri yok'tan ayırt edilsin)",
    TrayIconRenderer.PixelAt(icon, 7, 21).B == 0x5F);

byte[] critIcon = TrayIconRenderer.Render(95, 95, MeterLevel.Crit, MeterLevel.Crit);
var critPx = TrayIconRenderer.PixelAt(critIcon, 20, 10);
Check("ikon: kritik renk kırmızı", (critPx.B, critPx.G, critPx.R) == (0x5B, 0x5B, 0xFF));

byte[] noData = TrayIconRenderer.Render(null, null, MeterLevel.Unknown, MeterLevel.Unknown);
Check("ikon: veri yoksa çubuk boş kalıyor",
    TrayIconRenderer.PixelAt(noData, 7, 10).B == 0x50);

byte[] dimmed = TrayIconRenderer.Render(50, 50, MeterLevel.Ok, MeterLevel.Ok, dimmed: true);
Check("ikon: bayat veri soluk çiziliyor", TrayIconRenderer.PixelAt(dimmed, 16, 4).A == 0xB4);

Check("ikon seviyesi: %50 → Ok", TrayIconRenderer.LevelFor(50, 75, 90) == MeterLevel.Ok);
Check("ikon seviyesi: %80 → Warn", TrayIconRenderer.LevelFor(80, 75, 90) == MeterLevel.Warn);
Check("ikon seviyesi: %95 → Crit", TrayIconRenderer.LevelFor(95, 75, 90) == MeterLevel.Crit);
Check("ikon seviyesi: null → Unknown", TrayIconRenderer.LevelFor(null, 75, 90) == MeterLevel.Unknown);

// --- Tepsi araç ipucu (128 karakter sınırı) ---
var tipSettings = new AppSettings().Normalized();
Check("ipucu: snapshot yoksa açıklayıcı metin",
    TrayTooltip.Build(null, tipSettings, t0).Contains("henüz veri yok"));

var tipSnapshot = new DashboardSnapshot
{
    GeneratedAt = t0,
    Host = new HostInfo { CodexBarVersion = "t", RefreshIntervalSeconds = 120 },
    Providers =
    [
        ProviderRowFactory.Create(ProviderIds.Claude, "oauth",
            [RateWindowFactory.Create(WindowKinds.Session, "Oturum", 47, t0.AddHours(3)),
             RateWindowFactory.Create(WindowKinds.Weekly, "Haftalık", 78, t0.AddDays(3))], t0),
        ProviderRowFactory.Create(ProviderIds.Codex, "oauth",
            [RateWindowFactory.Create(WindowKinds.Session, "Oturum", 18, t0.AddHours(4)),
             RateWindowFactory.Create(WindowKinds.Weekly, "Haftalık", 91, t0.AddDays(1))], t0),
    ],
};

string tip = TrayTooltip.Build(tipSnapshot, tipSettings, t0);
Check("ipucu: 128 karakter sınırına uyuyor", tip.Length <= TrayTooltip.MaxLength);
Check("ipucu: en kısıtlayıcı sağlayıcı ilk sırada", tip.StartsWith("Codex"));
Check("ipucu: satırlar \\r\\n ile ayrılıyor", tip.Contains("\r\n"));
Check("ipucu: yüzde ve geri sayım var", tip.Contains("%91") && tip.Contains("1g"));

// Uzun içerik sınırı aşmamalı: yapay olarak çok sağlayıcılı bir snapshot kur.
var manyRows = Enumerable.Range(0, 8).Select(i =>
    ProviderRowFactory.Create(ProviderIds.Claude, "oauth",
        [RateWindowFactory.Create(WindowKinds.Weekly, "Haftalık", 50 + i, t0.AddDays(3))], t0)).ToList();
string longTip = TrayTooltip.Build(tipSnapshot with { Providers = manyRows }, tipSettings, t0);
Check("ipucu: taşan satırlar atılıyor, sınır korunuyor", longTip.Length <= TrayTooltip.MaxLength);

// Temizlik
AppPaths.OverrideRoot(null);
try { Directory.Delete(testRoot, recursive: true); } catch { /* geçici dizin; sızıntı kritik değil */ }

Console.WriteLine();
Console.WriteLine(failed == 0 ? "TÜM TESTLER GEÇTI ✓" : $"{failed} TEST BAŞARISIZ ✗");
return failed == 0 ? 0 : 1;


// ══════════════════════════════════════════════════════════════════════
//  Test yardımcıları
// ══════════════════════════════════════════════════════════════════════

/// <summary>Zamanı elle ilerletilebilen saat. Geri çekilme ve bayatlık testleri için.</summary>
sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now;
    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Sabit yanıt döndüren HTTP işleyicisi — ağ olmadan eşleme testi.</summary>
sealed class StubHandler(string body, System.Net.HttpStatusCode status = System.Net.HttpStatusCode.OK)
    : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        });
}

/// <summary>Davranışı testte değiştirilebilen sağlayıcı; çağrı sayısını da sayar.</summary>
sealed class FakeProviderSource(string providerId, Func<ProviderRow> next) : IProviderSource
{
    public Func<ProviderRow> Next { get; set; } = next;
    public int Calls { get; private set; }
    public string ProviderId => providerId;

    public Task<ProviderRow> FetchAsync(CancellationToken ct = default)
    {
        Calls++;
        return Task.FromResult(Next());
    }
}
