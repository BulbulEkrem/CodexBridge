# Faz 5 — Kendi Sağlayıcı Katmanı (ClearScript / V8)

> Durum: **fizibilite CANLI KANITLANDI** (11/11 prob geçti) · Tarih: 2026-08-06

## Sonuç: JS eklentileri V8'de çalışıyor

Araştırmanın #4 açık sorusu — *"`provider-plugin-prelude.js` + Sucrase JavaScriptCore'a özel
davranışa dayanıyor mu?"* — **kesin cevaplandı: hayır.** Üst akışın gerçek `xai.js` eklentisi,
prelude ile birlikte, **ClearScript/V8** üzerinde çalıştırıldı ve doğru çıktı üretti.

Bu, mimarideki en değerli bahsi doğrular: **15 hazır `.js` sağlayıcı Swift değil**, C# host'ta
olduğu gibi çalışır. Yukarı akış fiyat/uç nokta değiştirince tek yapılacak `.js`'i kopyalamak.

## Nasıl çalışıyor (Swift runtime'ın C# karşılığı)

`ProviderPluginRuntime.swift`'in birebir yeniden kurulumu (`JsProviderRuntime.cs`):
1. `defineProvider` global'i tanımlanır (eklenti tanımını yakalar).
2. Prelude eval edilir → `applyPrelude(ctx, host)` fonksiyonu.
3. Eklenti kaynağı eval edilir (`defineProvider(...)` çağırır).
4. `ctx = applyPrelude({}, host)`, `await def.fetchUsage(ctx)`, sonuç `JSON.stringify` ile
   düz JSON'a çevrilir (Date'ler ISO string olur → C# tarafı temiz parse eder).

`host` nesnesi **JS tarafında C# delegate'lerine** bağlanır (ClearScript member-adı eşlemesine
güvenmeden — `[ScriptMember]` yolu `host.http is not a function` verdi, delegate yolu sağlam).

## Bileşenler (`src/CodexBridge.JsHost`)
| Dosya | Sorumluluk |
|---|---|
| `JsProviderRuntime.cs` | V8 motoru + prelude/eklenti yükleme + fetchUsage çalıştırma |
| `IJsHostBridge.cs` | 9 host API'sinin arkasındaki yetenek (http/settings/cookie/log/cache) |
| `HttpJsHostBridge.cs` | Üretim köprüsü — gerçek HttpClient, ayar/sır/çerez |
| `JsSnapshotMapper.cs` | fetchUsage sonucu → dashboard/v1 ProviderRow (kimlik maskeli) |
| `JsUsageSource.cs` | Bir dizi eklentiyi çalıştıran IUsageSource (kısmi başarı korunur) |
| `plugins/*.js` | Üst akıştan (MIT) senkronlanan prelude + sucrase + örnek sağlayıcılar |

## Kanıt (`src/CodexBridge.JsProbe`, 11/11 ✓)
- **A:** Gerçek `xai.js` V8'de + mock http → `cost.used=5`, `identity.loginMethod="Management API"`.
- **B:** primary/secondary/credits/identity'li eklenti → dashboard/v1 eşleme; `ctx.pct`,
  `ctx.date.unixSeconds` prelude özellikleri çalıştı; e-posta maskelendi (`redacted@acme.com`).

Çalıştırma: `codexbridge-jsprobe.exe` (SAC nedeniyle apphost exe; `dotnet run` değil).

## Sınırlar / sıradaki
- Prob **mock http** kullanır — gerçek sağlayıcı API'lerine (kimlik bilgisiyle) canlı çağrı
  test edilmedi (kimlik yok). Runtime kanıtlı; `HttpJsHostBridge` gerçek çağrı için hazır.
- **Sucrase** (TS→JS) henüz devrede değil: bundled `.js` sağlayıcılar zaten düz JS. Kullanıcı
  `.ts` eklentileri için Sucrase transpile adımı eklenecek (`sucrase-3.35.1.min.js` kopyalandı).
- 31 `web` stratejisi için **çerez katmanı (DPAPI)** = Faz 6.
