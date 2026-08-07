using CodexBridge.Core.Dashboard;

namespace CodexBridge.Core.Notifications;

/// <summary>
/// Faz 7 çekirdeği: iki ardışık <see cref="DashboardSnapshot"/>'ı karşılaştırıp gönderilecek
/// bildirim olaylarını üretir. Saf fonksiyon — hiçbir ağ/platform bağımlılığı yok, doğrudan
/// test edilebilir. Host bunu her yenilemede çağırır, sonra dispatcher'a verir.
///
/// Kural: yalnızca EŞİK GEÇİŞLERİNDE olay üret (kenar tetikleme). Aynı yüksek kullanım her
/// yenilemede yeniden bildirmesin diye, önceki değer eşiğin altında / şimdiki üstünde olmalı.
/// </summary>
public static class NotificationEngine
{
    /// <param name="previous">Bir önceki snapshot; ilk çalıştırmada null (o zaman olay üretilmez).</param>
    /// <param name="current">Yeni snapshot.</param>
    public static IReadOnlyList<NotificationEvent> Diff(
        DashboardSnapshot? previous,
        DashboardSnapshot current,
        NotificationThresholds? thresholds = null)
    {
        var t = thresholds ?? NotificationThresholds.Default;
        var events = new List<NotificationEvent>();

        // İlk snapshot: karşılaştıracak taban yok → sessiz başla (açılışta bildirim yağmuru olmasın).
        if (previous is null) return events;

        var prevById = previous.Providers.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var now = current.GeneratedAt == default ? DateTimeOffset.UtcNow : current.GeneratedAt;

        foreach (var cur in current.Providers)
        {
            if (!prevById.TryGetValue(cur.Id, out var prev))
                continue; // yeni görülen sağlayıcı için taban yok → geçiş sayma.

            EmitErrorTransitions(events, prev, cur, now);
            EmitQuotaTransitions(events, prev, cur, t, now);
        }

        return events;
    }

    private static void EmitErrorTransitions(List<NotificationEvent> events, ProviderRow prev, ProviderRow cur, DateTimeOffset now)
    {
        bool prevError = prev.Error is not null;
        bool curError = cur.Error is not null;

        if (!prevError && curError)
        {
            events.Add(new NotificationEvent
            {
                ProviderId = cur.Id,
                ProviderName = cur.Name,
                Kind = NotificationKind.ProviderError,
                Title = $"{cur.Name}: hata",
                Body = cur.Error?.Message is { Length: > 0 } m ? m : "Sağlayıcı verisi alınamıyor.",
                DedupeKey = $"{cur.Id}:error",
                OccurredAt = now,
            });
        }
        else if (prevError && !curError)
        {
            events.Add(new NotificationEvent
            {
                ProviderId = cur.Id,
                ProviderName = cur.Name,
                Kind = NotificationKind.ProviderRecovered,
                Title = $"{cur.Name}: yeniden çevrimiçi",
                Body = "Sağlayıcı verisi tekrar akıyor.",
                DedupeKey = $"{cur.Id}:error",
                OccurredAt = now,
            });
        }
    }

    private static void EmitQuotaTransitions(List<NotificationEvent> events, ProviderRow prev, ProviderRow cur, NotificationThresholds t, DateTimeOffset now)
    {
        // Sağlayıcının en kötü (en yüksek kullanımlı) penceresini baz al.
        var (curWin, curUsed) = Worst(cur);
        if (curWin is null) return;
        var (_, prevUsed) = Worst(prev);

        // Kritik geçiş uyarı geçişini gölgeler (aynı yenilemede ikisini birden gönderme).
        if (CrossedUp(prevUsed, curUsed, t.CriticalPercent))
        {
            events.Add(Quota(cur, curWin, curUsed, NotificationKind.QuotaCritical,
                $"{cur.Name}: kota kritik", $"%{Fmt(curUsed)} kullanıldı — {WindowLabel(curWin)} neredeyse doldu.", now));
        }
        else if (CrossedUp(prevUsed, curUsed, t.WarningPercent))
        {
            events.Add(Quota(cur, curWin, curUsed, NotificationKind.QuotaWarning,
                $"{cur.Name}: kota uyarısı", $"%{Fmt(curUsed)} kullanıldı — {WindowLabel(curWin)}.", now));
        }
        else if (prevUsed is { } pu && pu >= t.WarningPercent && curUsed < t.RecoverBelowPercent)
        {
            // Yüksekten belirgin biçimde düşüş → kota sıfırlandı / toparlandı.
            events.Add(Quota(cur, curWin, curUsed, NotificationKind.QuotaReset,
                $"{cur.Name}: kota tazelendi", $"Kullanım %{Fmt(curUsed)}'e düştü — {WindowLabel(curWin)} sıfırlandı.", now));
        }
    }

    /// <summary>Bir sağlayıcının en yüksek kullanımlı penceresi (kind, usedPercent).</summary>
    private static (RateWindow? Window, double? Used) Worst(ProviderRow row)
    {
        RateWindow? best = null;
        double? bestUsed = null;
        foreach (var w in row.Windows)
        {
            if (w.UsedPercent is not { } u) continue;
            if (bestUsed is null || u > bestUsed) { bestUsed = u; best = w; }
        }
        return (best, bestUsed);
    }

    /// <summary>Önceki değer eşiğin altındayken (veya bilinmezken) şimdiki değer eşiği geçtiyse true.</summary>
    private static bool CrossedUp(double? prev, double? cur, double threshold)
        => cur is { } c && c >= threshold && (prev is not { } p || p < threshold);

    private static NotificationEvent Quota(ProviderRow row, RateWindow win, double? used, NotificationKind kind, string title, string body, DateTimeOffset now)
        => new()
        {
            ProviderId = row.Id,
            ProviderName = row.Name,
            Kind = kind,
            WindowKind = win.Kind,
            UsedPercent = used,
            Title = title,
            Body = body,
            // Uyarı ve kritik ayrı anahtarlarda: uyarıdan sonra kritik de gelebilsin.
            DedupeKey = $"{row.Id}:quota:{win.Kind}:{kind}",
            OccurredAt = now,
        };

    private static string Fmt(double? v) => v is { } d ? Math.Round(d, 0).ToString("0") : "?";

    private static string WindowLabel(RateWindow w) => w.Label ?? w.Kind switch
    {
        "session" => "oturum penceresi",
        "weekly" => "haftalık pencere",
        "tertiary" => "üçüncül pencere",
        _ => w.Kind,
    };
}
