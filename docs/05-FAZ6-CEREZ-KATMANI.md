# Faz 6 — Çerez Katmanı (Chrome/Edge, DPAPI + AES-GCM)

> Durum: **kripto doğrulandı (sentetik veri)** · canlı okuma bilinçli olarak çalıştırılmadı (gizlilik)
> Tarih: 2026-08-06

## Ne açar
31 `web` stratejisi (ör. Perplexity) tarayıcı oturum çerezine dayanır. Bu katman onları
Windows'ta çözerek JS eklentilerin `ctx.browser.cookieHeader(domain)` çağrısını besler.

## Nasıl çalışır (`WindowsCookieStore`)
1. `Local State` JSON'undaki `os_crypt.encrypted_key` → "DPAPI" öneki atılır → `ProtectedData.Unprotect`
   (CurrentUser) → 32 baytlık AES anahtarı.
2. `Cookies` SQLite'ı (salt-okunur) → her `encrypted_value` AES-256-GCM ile çözülür.
   Biçim: `[3 bayt "v10"/"v20"][12 bayt nonce][şifreli][16 bayt tag]`.
3. `GetCookieHeader(domain)` → eşleşen çerezlerden `name=value; ...` üretir; `HttpJsHostBridge`'in
   `cookieResolver`'ına bağlanır.

## Doğrulama (JsProbe, Test C — 3/3 ✓)
Kriptonun doğruluğu **sentetik veriyle** test edildi (gerçek oturum token'larına dokunmadan):
- v10 biçim öneki doğru
- AES-256-GCM şifrele→çöz round-trip
- yanlış anahtar → `null` (GCM kimlik doğrulaması reddi)

## Bilinçli sınır: canlı çerez okuması otomatik çalıştırılmadı
`GetCookieHeader` gerçek tarayıcı çerezlerini (canlı oturum kimlik bilgileri) okur. Bu hassas
olduğundan otonom oturumda **çalıştırılmadı**. Kod hazır; kullanıcı makine başında ve bilinçli
onayla denemeli.

## Gizlilik ilkeleri (kodda uygulanmış)
- Çözülen çerez değerleri **asla loglanmaz/dışarı verilmez** — kimlik bilgisi PC'de kalır.
- Telefon istemcisine çerez değil, yalnızca maskeli `dashboard/v1` snapshot gider.

## Sınır: v20 app-bound şifreleme
Chrome 127+ bazı çerezlerde v20 "app-bound" şifreleme kullanır (ek uygulama-bağlı katman).
Bu iskele v10'u tam çözer; v20'nin app-bound katmanı ek iş gerektirir (gelecek tur).
