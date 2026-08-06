# Faz 2 · 3 · 4 — Veri Kaynağı, HTTP Host ve Telefon

> Tarih: 2026-08-06 · Bu belge tek oturumda otonom yazılan Windows-arkası + telefon iskeleti işini özetler.

## Faz 3 — `dashboard/v1` HTTP host ✅ (CANLI DOĞRULANDI)

`src/CodexBridge.Host` — ASP.NET Core minimal API. **Gerçekten çalıştırılıp curl ile test edildi:**

| Uç nokta | Davranış | Test |
|---|---|---|
| `GET /health` | `{status:ok, version}`, daima açık | 200 ✓ |
| `GET /dashboard/v1/snapshot` | Bearer korumalı, token yoksa fails-closed | tokensiz→401 ✓, yanlış→401 ✓, doğru→200 ✓ |

- `Cache-Control: no-store` ✓, sabit zamanlı SHA-256 token karşılaştırması, loopback Host kontrolü (DNS-rebinding).
- Loopback dışı bind token + `--allow-plain-http` şart (üst akış güvenlik modeli).
- `SnapshotCache` — `refresh-interval` önbelleği + coalescing (telefon asla doğrudan sağlayıcıya gitmez).
- Yapılandırma: `CODEXBRIDGE_HOST/PORT/REFRESH_INTERVAL`, `CODEXBAR_DASHBOARD_TOKEN`, `--allow-plain-http`.

Çalıştırma: `codexbridge-host.exe` (apphost exe'yi doğrudan çalıştır — aşağıdaki SAC notu).

## Faz 2 — Win-CodexBar `serve` adaptörü ✅ (EŞLEME TEST EDİLDİ)

`src/CodexBridge.Core/Sources/WinCodexBar/WinCodexBarSource.cs` — Win-CodexBar'ın `/usage`
(+`/cost`) çıktısını `dashboard/v1`'e çevirir. `MapUsage` saf/statik → örnek JSON ile test edildi
(self-test'te 9 assertion). **Canlı entegrasyon** (çalışan Win-CodexBar serve'e bağlanma) beklemede;
Win-CodexBar Rust/Tauri, bu ortamda derlenmedi.

Ek: `HttpUsageSource` — bir `dashboard/v1` host'undan okuyan istemci (görev çubuğu "host'tan oku"
modu + telefon + çok-makine birleştirmenin çekirdeği).

## Faz 1 — kaynak seçimi bağlandı

Görev çubuğu uygulaması `CODEXBRIDGE_HOST_URL` ayarlıysa `HttpUsageSource` ile gerçek host'tan,
yoksa `FakeUsageSource` ile sahte veri okur.

## Faz 4 — Telefon istemcileri (iskele, bu ortamda derlenmedi)

- `phone/android/` — Glance widget + WorkManager + kotlinx.serialization. dashboard/v1 modelleri,
  çok-host birleştirme (maliyet toplanır/kota tekil), "veri yaşı" göstergesi, Data Saver/pil tuzağı notları.
- `phone/ios/` — WidgetKit. Codable modeller, DashboardClient, **resetAt'e hizalı** TimelineProvider.
  Üst akışın 6 WidgetKit görünümü yeniden kullanılır (tek değişen veri kaynağı).
- Derleme: Android Studio+SDK / macOS+Xcode gerekir (bu Windows ortamında yok).

## Testler — self-test konsolu (22/22 ✓)

`src/CodexBridge.SelfTest` — AdaptiveRefresh karar tablosu, dashboard/v1 serileştirme sözleşmesi
(camelCase, küçük harf enum, null atlama, round-trip), WinCodexBar `MapUsage` eşlemesi.

**Neden xunit değil:** bu makinede **Smart App Control (SAC) enforce** modda; `dotnet test` ve
`dotnet run` imzasız DLL'leri engelliyor (`0x800711C7`). Ama derlenmiş **apphost .exe**'ler
çalışıyor. Bu yüzden testleri assertion çalıştıran bir konsol exe olarak koşuyoruz:
`codexbridge-selftest.exe`. (Aynı sebeple host da `dotnet run` yerine exe olarak çalıştırılır.)

## Ortam notları (gelecek oturumlar için)
- **Windows 11 SDK 26100** kuruldu (winget) → WinUI derleniyor.
- **SAC enforce** → derlenmiş exe'leri doğrudan çalıştır; `dotnet run`/`dotnet test` imza engeline takılır.
- `dotnet new sln` → `.slnx`; karışık platformlu çözümü `-p:Platform` override'sız derle.
