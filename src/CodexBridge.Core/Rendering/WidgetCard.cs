using System.Text.Json;
using System.Text.Json.Nodes;
using CodexBridge.Core.Dashboard;
using CodexBridge.Core.Providers;
using CodexBridge.Core.Settings;

namespace CodexBridge.Core.Rendering;

/// <summary>Widget boyutu. Windows üç boyut sunuyor; şablon <c>$host.widgetSize</c> ile dallanıyor.</summary>
public enum WidgetSize { Small, Medium, Large }

/// <summary>
/// Windows Widget'ının Adaptive Cards içeriğini üretir — <b>platformdan bağımsız</b>,
/// böylece Windows olmadan test edilebiliyor. Widget sağlayıcısı yalnızca bu iki dizeyi
/// (şablon + veri) kabuğa uzatır.
///
/// <para><b>Neden metin ölçer, renkli kutu değil:</b> Adaptive Cards'ta yerel bir ilerleme
/// çubuğu yok. Sütun genişliği + arka plan stiliyle çubuk taklit edilebilir ama sürümler
/// arası davranışı oynak. <c>TextBlock.color</c> (<c>good/warning/attention</c>) ise
/// her sürümde güvenilir — ölçer blok karakterlerle çizilip renk oradan veriliyor.</para>
///
/// <para>Aynı JSON ileride Start menü companion'ına da verilebilir: companion da
/// Adaptive Cards konuşuyor. Bugün resmî API'si olmadığı için o yüzey kapsam dışı,
/// ama şablonu paylaşmak bedava bir opsiyon.</para>
/// </summary>
public static class WidgetCard
{
    /// <summary>Ölçerin blok sayısı. Küçük widget'ta dar, orta/büyükte geniş.</summary>
    private const int SmallMeterCells = 6;
    private const int WideMeterCells = 10;

    private const char FilledCell = '▰';
    private const char EmptyCell = '▱';

    /// <summary>
    /// Adaptive Card <b>şablonu</b>. Veri <see cref="BuildData"/>'dan gelir; şablon sabittir,
    /// yalnızca sağlayıcı listesi üzerinde <c>$data</c> ile tekrarlanır.
    /// </summary>
    public static string BuildTemplate() =>
        """
        {
          "type": "AdaptiveCard",
          "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
          "version": "1.5",
          "body": [
            {
              "type": "Container",
              "$data": "${providers}",
              "spacing": "Small",
              "items": [
                {
                  "type": "ColumnSet",
                  "spacing": "None",
                  "columns": [
                    {
                      "type": "Column",
                      "width": "stretch",
                      "items": [
                        {
                          "type": "TextBlock",
                          "text": "${name}",
                          "weight": "Bolder",
                          "size": "Default",
                          "wrap": false
                        }
                      ]
                    },
                    {
                      "type": "Column",
                      "width": "auto",
                      "items": [
                        {
                          "type": "TextBlock",
                          "text": "${headline}",
                          "color": "${severity}",
                          "weight": "Bolder",
                          "horizontalAlignment": "Right",
                          "wrap": false
                        }
                      ]
                    }
                  ]
                },
                {
                  "type": "TextBlock",
                  "text": "${sessionMeter}",
                  "color": "${sessionSeverity}",
                  "size": "Small",
                  "spacing": "None",
                  "wrap": false,
                  "$when": "${$host.widgetSize != 'small'}"
                },
                {
                  "type": "TextBlock",
                  "text": "${weeklyMeter}",
                  "color": "${weeklySeverity}",
                  "size": "Small",
                  "spacing": "None",
                  "wrap": false,
                  "$when": "${$host.widgetSize != 'small'}"
                },
                {
                  "type": "TextBlock",
                  "text": "${detail}",
                  "isSubtle": true,
                  "size": "Small",
                  "spacing": "None",
                  "wrap": true
                }
              ]
            },
            {
              "type": "TextBlock",
              "text": "${footer}",
              "isSubtle": true,
              "size": "Small",
              "horizontalAlignment": "Right",
              "spacing": "Small",
              "wrap": false
            }
          ]
        }
        """;

    /// <summary>Şablonun beklediği veri belgesini üretir.</summary>
    public static string BuildData(DashboardSnapshot? snapshot, AppSettings settings, DateTimeOffset now, WidgetSize size)
    {
        var root = new JsonObject();
        var providers = new JsonArray();

        if (snapshot is null)
        {
            root["providers"] = providers;
            root["footer"] = "Henüz veri yok";
            return root.ToJsonString(Json);
        }

        int cells = size == WidgetSize.Small ? SmallMeterCells : WideMeterCells;

        foreach (var row in snapshot.Providers
            .Where(p => settings.IsEnabled(p.Id))
            .OrderBy(p => p.Display?.SortKey ?? int.MaxValue))
        {
            providers.Add(BuildProviderNode(row, settings, now, cells));
        }

        root["providers"] = providers;
        root["footer"] = BuildFooter(snapshot, now);
        return root.ToJsonString(Json);
    }

    private static JsonObject BuildProviderNode(ProviderRow row, AppSettings settings, DateTimeOffset now, int cells)
    {
        var session = row.Window(WindowKinds.Session);
        var weekly = row.Window(WindowKinds.Weekly);
        var worst = row.MostRestrictive();

        string detail;
        if (row.Error is { } err && worst is null)
        {
            detail = err.Message;
        }
        else
        {
            string reset = RateWindowFactory.FormatCountdown(worst?.ResetAt, now) is { } c
                ? $"{worst?.Label ?? ""} {c}".Trim()
                : "";
            detail = row.Error is not null
                ? (reset.Length > 0 ? $"{reset} · son bilinen değer" : "son bilinen değer")
                : reset;
        }

        return new JsonObject
        {
            ["name"] = row.Name,
            ["headline"] = worst?.UsedPercent is { } p ? $"%{(int)Math.Round(p)}" : "—",
            ["severity"] = Severity(worst?.UsedPercent, settings),
            ["sessionMeter"] = Meter("Oturum", session?.UsedPercent, cells),
            ["sessionSeverity"] = Severity(session?.UsedPercent, settings),
            ["weeklyMeter"] = Meter("Hafta", weekly?.UsedPercent, cells),
            ["weeklySeverity"] = Severity(weekly?.UsedPercent, settings),
            ["detail"] = detail,
        };
    }

    /// <summary>Blok karakterlerden ölçer: <c>Oturum ▰▰▰▱▱▱ %47</c>.</summary>
    private static string Meter(string label, double? percent, int cells)
    {
        if (percent is not { } pct) return $"{label} —";

        double clamped = Math.Clamp(pct, 0, 100);
        int filled = (int)Math.Round(cells * clamped / 100.0);
        // %0 ile "veri yok" karışmasın diye sıfır olmayan kullanım en az bir blok gösterir.
        if (filled == 0 && clamped > 0) filled = 1;

        return $"{label} {new string(FilledCell, filled)}{new string(EmptyCell, cells - filled)} %{(int)Math.Round(clamped)}";
    }

    /// <summary>Adaptive Cards renk adı. Sayısal eşikler ayarlardan gelir ki band, tepsi ve
    /// widget aynı anda renk değiştirsin.</summary>
    private static string Severity(double? percent, AppSettings settings) => percent switch
    {
        null => "Default",
        var p when p >= settings.CritPercent => "Attention",
        var p when p >= settings.WarnPercent => "Warning",
        _ => "Good",
    };

    private static string BuildFooter(DashboardSnapshot snapshot, DateTimeOffset now)
    {
        var age = now - snapshot.GeneratedAt;
        if (age < TimeSpan.FromMinutes(1)) return "az önce";
        if (age.TotalHours >= 1) return $"{(int)age.TotalHours} sa önce";
        return $"{(int)age.TotalMinutes} dk önce";
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
}
