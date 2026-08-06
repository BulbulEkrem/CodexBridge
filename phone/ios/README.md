# CodexBridge iOS Widget — iskele

> Derlenmedi (macOS + Xcode gerekir; bu ortam Windows). Xcode'da bir Widget Extension'a taşınmalı.

## En kısa yol: üst akışın görünümlerini yeniden kullan

Üst akış CodexBar'da `Sources/CodexBarWidget/` altında **6 WidgetKit görünümü** zaten yazılı
(Switcher, Usage, History, Metric, Burn Down, Burn Down Combined). Bunlar SwiftUI/WidgetKit —
iOS'ta **aynı framework**. Değişen tek şey **veri kaynağı**:

| | macOS (üst akış) | iOS (bizim) |
|---|---|---|
| Kaynak | App-group container'daki yerel JSON | `{host}/dashboard/v1/snapshot` HTTP |
| Model | `WidgetSnapshot` | `DashboardSnapshot` (bu klasör) |

## Bu klasördeki dosyalar
| Dosya | Sorumluluk |
|---|---|
| `DashboardModels.swift` | `dashboard/v1` şemasının Codable karşılığı |
| `DashboardClient.swift` | snapshot çekme (Bearer) + çok-host birleştirme |
| `UsageTimelineProvider.swift` | WidgetKit TimelineProvider — **resetAt'e hizalı** timeline |

## Yenileme bütçesi (kritik)
iOS ~40–70 yenileme/gün verir. Sabit aralık ziyan; timeline'ı `resetAt`'e hizala:
kota 17:15'te sıfırlanıyorsa yenilemeyi 17:16'ya koy. WidgetKit'in timeline modeli tam bunun için.

## Tuzak: statik sağlayıcı listesi
Widget yapılandırma UI'ı AppIntents ile **derleme zamanında** sabit; sağlayıcı listesi
çalışma zamanında sunucudan öğrenilemez. Sonuç: sağlayıcı listesini geniş tut, seyrek güncelle.

## İleri (Faz 7)
Kota tükenmesi için **Live Activity / Dynamic Island** push, widget yenilemesinden daha uygun
olabilir — ayrı yenileme kurallarına tabi; araştırılmalı.
