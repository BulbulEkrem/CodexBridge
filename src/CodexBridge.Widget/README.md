# CodexBridge.Widget

Windows Widget panosu (<kbd>Win</kbd>+<kbd>W</kbd>) için sağlayıcı.

## Ne yapar

`snapshot.json`'u okur ve Adaptive Cards olarak panoya verir. **Sağlayıcıya hiç gitmez** —
kotayı ana uygulama çeker, bu süreç yalnızca gösterir. Sebebi: widget host bu süreci pano
açılınca uyandırıp kapanınca öldürüyor; kendi çekimini yapsa aynı kotayı ikinci kez tüketir
ve iki süreç aynı token'ı yenilemeye çalışırdı.

Kart içeriği `CodexBridge.Core/Rendering/WidgetCard.cs`'te üretiliyor — platformsuz ve
SelfTest'te doğrulanıyor. Bu projede yalnızca COM ve host bağlantısı var.

## Neden ayrı bir proje

Widget sağlayıcısı **paketli** bir uygulama olmak zorunda (Microsoft'un mevcut sürümdeki
kısıtı). Ana uygulamamız paketsiz çalışıyor; kimlik "external location" (sparse) paketiyle
veriliyor, böylece kurulum akışı ve dosya konumları değişmiyor.

## Windows'ta tamamlanması gerekenler

Bu klasördeki kod **Linux'ta derlenmedi ve çalıştırılmadı.** Sırayla:

1. `dotnet build src/CodexBridge.Widget/CodexBridge.Widget.csproj -c Debug -p:Platform=x64`
2. `Assets/` üretimi:
   - `StoreLogo.png`, `Square150x150Logo.png`, `Square44x44Logo.png`, `WidgetIcon.png`
   - `WidgetScreenshot.png` — **300×304 px**, şeffaf yuvarlatılmış köşe (seçici şartı)
3. `Package.appxmanifest` içinde `Publisher`, imzalama sertifikasının `CN`'iyle birebir aynı yapılmalı.
4. `uap10:AllowExternalContent` ile exe'lerin gerçek klasörü bildirilmeli.
5. Paket imzalanıp kaydedilmeli:
   `Add-AppxPackage -Register <manifest> -ExternalLocation <exe klasörü>`
6. Pano açılıp widget seçicide "AI kotası" görünmeli.

## Doğrulanacaklar (canlı test)

- [ ] Widget seçicide görünüyor, sabitlenebiliyor
- [ ] Küçük ve orta boyut ayrı ayrı doğru render ediliyor
- [ ] `snapshot.json` değişince pano açıkken kart tazeleniyor
- [ ] Ana uygulama kapalıyken kart son bilinen değeri "x dk önce" ile gösteriyor
- [ ] Açık ve koyu temada okunabilir
