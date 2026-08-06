# CodexBridge — Telefon İstemcileri (Faz 4)

Tek Android + tek iOS uygulaması, `dashboard/v1` konuşan **üç OS host'una da** bağlanır
(macOS/Linux `codexbar serve`, Windows `codexbridge-host`). İstemci host'un ne olduğunu bilmez.

> ⚠️ **Bu klasördeki kod bu depoda DERLENMEDİ.** Android için Android Studio + SDK, iOS için
> macOS + Xcode gerekir; ikisi de bu geliştirme ortamında yok. Kod, doğru mimariyle yazılmış
> iskelelerdir — ilgili araç zincirinde derlenip test edilmelidir.

## Tasarımı belirleyen kısıt: yenileme bütçesi

| Platform | Mekanizma | Pratik alt sınır |
|---|---|---|
| iOS | WidgetKit timeline | ~40–70 yenileme/gün |
| Android | WorkManager PeriodicWork | 15 dakika |

Bu yüzden widget **canlı gösterge değil**: her zaman **"değer + veri yaşı"** gösterir
("%28 · 12 dk önce"). Acil bilgi (kota bitmek üzere) widget yenilemesiyle değil, **PC'den
push** ile gelir (Faz 7). Bütçe, sabit aralık yerine `resetAt`'e hizalanır.

## Güvenlik

`serve`/host'ta TLS yok → bearer token düz HTTP'de açık geçer. Öneri:
1. **Tailscale** (en pratik): host loopback'te kalır, Tailscale arayüzüne bağlanılır.
2. **TLS ters proxy** (Caddy) — bkz. `docs/00-MIMARI...`.
3. Yalnızca güvenilen LAN'da düz HTTP (bilinçli kabul).

Token asla query string'de değil, daima `Authorization: Bearer` başlığında.

## Ortak sözleşme

Her iki istemci `GET {host}/dashboard/v1/snapshot` (Bearer) çağırır; şema
[docs/00-MIMARI-VE-YOL-HARITASI.md](../docs/00-MIMARI-VE-YOL-HARITASI.md) §7'de.
`staleAfterSeconds` alanı "veri eski" rozetinin eşiğini **sunucudan** verir (telefonda sabit kodlanmaz).

## Çok-makineli birleşik görünüm (Öncelik 1 özelliği)

İstemci birden fazla host'a bağlanıp snapshot'ları birleştirebilir. **Kritik kural:**
maliyet toplanır, **kota yüzdeleri toplanmaz** (hesap bazlı → tekilleştir). Aynı `id`'li
satırlar aynı hesabı raporluyorsa yüzde tek sayılır.
