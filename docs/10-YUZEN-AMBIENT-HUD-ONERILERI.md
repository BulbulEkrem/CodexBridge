# Yüzen Ambient HUD — Tasarım Önerileri

> Durum: **A · Kapsül (D4 yerleşimi) SEÇİLDİ ve YAZILDI** · Diğer üçü hâlâ öneri · Tarih: 2026-09-04
>
> Görev çubuğu band'ına alternatif olarak, çubuğun **içinde değil üstünde** duran,
> sürüklenebilir, her zaman üstte bir HUD için hazırlanmış tasarım seçeneklerini toplar.
>
> **Uygulanan:** A · Kapsül, D4 yerleşimiyle (saatlik ve haftalık ayrı satırlarda, her satırın
> kendi yüzdesi/geri sayımı/tam genişlik barı) ve resmî marka logolarıyla —
> `src/CodexBridge.Taskbar/Hud/`. Uygulama sırasında çıkan çökmeler ve uç nokta bulguları
> `.claude/knowledge/decisions.md` içinde.

## Neden

Band görev çubuğunun içinde yaşıyor ve oradaki yer için yarışıyor: çubuk ortalıyken soldaki
boşluk pencere sayısına göre daralıyor, hava durumu widget'ı sola oturuyor, otomatik gizleme
açıksa band da gizleniyor. Yüzen bir pencerede bu kısıtların hiçbiri yok.

"Ekranı ayırmayan" kısmı önemli: bir **AppBar** (`SHAppBarMessage`) çalışma alanını rezerve
eder ve diğer pencereleri iter. Yüzen HUD sıradan bir top-level pencere — hiçbir şey rezerve
etmez, sadece üstte durur.

## Dosyalar

| Dosya | İçerik |
|---|---|
| [`tasarim/yuzen-ambient-hud.html`](tasarim/yuzen-ambient-hud.html) | Dört öneri (kapsül / sayaç / orb / kenar şeridi), 1:1 ölçek, sürüklenebilir masaüstü sahnesi, fizibilite dökümü |
| [`tasarim/kapsul-varyantlari.html`](tasarim/kapsul-varyantlari.html) | Kapsülün detaylandırılması: veri deposu, dört logo işlemi, üç veri yoğunluğu |

Tarayıcıda açılırlar; bağımlılıkları yok (yalnızca Google Fonts).

## Öneriler

Dinlenme hâlindeki ayak izine göre, büyükten küçüğe:

| | Öneri | Boyut | Özet |
|---|---|---|---|
| A | Kapsül | 272 × 44 dip | Band'ın yüzen hâli. Bilgi aynı, en ucuz uygulama |
| B | Sayaç | 168 × 46 dip | Geri sayım büyük, yüzdeler küçük. Çubuklar gider |
| C | Orb | 44 × 44 dip | Tek halka + en kısıtlayıcı %; hover'da açılır. Teknik olarak en zoru |
| D | Kenar şeridi | 12 × 62 dip | Yazısız iki renk sütunu. "Ambient" olan tek öneri |

A ve D aynı eksenin iki ucu: A en çok bilgiyi verir, D en az rahatsız eder.

## Kapsül için veri deposu

Kapsüle konabilecekler üç kovada. Ortadaki kova **şemada tanımlı ama Claude/Codex
kaynaklarımız doldurmuyor**.

- **Şu an dolu:** oturum/haftalık %, sıfırlanma zamanları, plan, durum seviyesi, kaynak,
  veri yaşı, hata kodu.
- **Şemada var, boş:** `cost.todayUSD`, `cost.last30DaysUSD`, `credits.remaining`,
  maskeli e-posta, `tertiary` pencere, `status.label`.
- **Türetilebilir (şemada yok):** yakma hızı (%/saat), tahmini tükeniş, "sıfırlanmaya
  yetişir mi", pencere ilerlemesi, eğilim. `NotificationEngine` zaten iki snapshot
  karşılaştırdığı için sağlayıcıya dokunmadan hesaplanabilir.

En değerli ekleme **yakma hızı**: yüzde ve geri sayım "ne kadar kaldı"yı söylüyor ama
"yetişir miyim"i söylemiyor.

## Logo işlemleri

Ad metnini logo ile değiştirmek pill başına ~34 dip kazandırıyor.

- **L1 · Ad yerine logo** — en doğrudan takas, kazanılan yer veriye gider.
- **L2 · Taşan filigran** — logo büyür, saydamlaşır, pill kenarından taşar. Yer maliyeti yok
  (arkada), riski rakamlarla kontrast yarışı.
- **L3 · Logo göstergenin kendisi** — işaret aşağıdan yukarı doluyor; marka hem kimlik hem
  ölçer, ayrı çubuk gerekmiyor.
- **L4 · Logo + renk kenarı** — marka rengi ince kenar şeridine çekilir, **dolgu rengi durum
  için serbest kalır**. Bugün dolgu hem markayı hem durumu anlatmaya çalışıyor.

Yoğunluk (D1 kompakt / D2 dengeli / D3 detaylı) logo işleminden bağımsız seçilebilir.
D3 (476 × 62 dip) artık kapsül değil kart — sürekli açık durmaz, kapsülün açılmış hâli olmalı.

## Fizibilite

Band'dan **daha basit**. Band'ın zor kısımlarının hiçbiri burada yok: `Shell_TrayWnd`'e
parent'lama, Explorer-restart gözcüsü, ReBar konumlandırması, `NIF_GUID` kaydı.

**Elimizde olan:** saydam pencere (`TransparentTintBackdrop`, canlı doğrulandı), kromsuz
pencere, `BuildPill` çizimi, geri sayım tikleyicisi, ekran/DPI olay dinleyicileri, ayar
kalıcılığı.

**Yazılacak olan:** `OverlappedPresenter.IsAlwaysOnTop`; sürükleme (`WM_NCHITTEST` →
`HTCAPTION`, mevcut subclass proc'un içine); konum hafızası (hangi monitör + orana göre,
ekran gidince güvenli geri düşüş); `WS_EX_TOOLWINDOW` ile Alt-Tab ve çubukta görünmeme.

**Riskli olan:**

- **Tam ekran uygulamalar** — oyun/video "her zaman üstte"yi örter ya da tersi olur.
  Davranışa karar vermek gerek.
- **Tıklama geçirgenliği** — dinlenirken tıklamayı alttaki pencereye geçirmek sürükleme ile
  çakışır; bir modifiye tuş gerekir.
- **Saydamlık ↔ okunabilirlik** — "ambient" düşük opaklık ister, sayı yüksek kontrast ister.
- **Band ile birlikte mi?** — ikisi açıkken aynı bilgi iki yerde durur.

## Notlar

- HTML'lerdeki yüzdeler, geri sayımlar ve plan **gerçek** (4 Eylül 2026, 21:26 snapshot'ı).
  Yakma hızı ve tükeniş tahmini değerleri **örnek** — o metrikler henüz hesaplanmıyor.
- Claude'un ışın demeti geometrik olduğu için birebir çizildi; **Codex/OpenAI düğümü altı
  loblu bir yaklaşıklama.** Uygulamada marka dosyaları kaynak olarak gömülmeli.
