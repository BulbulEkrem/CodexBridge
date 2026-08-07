using System.Collections.Concurrent;
using CodexBridge.Core.Dashboard;
using CodexBridge.Core.Notifications;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CodexBridge.Host.Push;

/// <summary>
/// Faz 7 arka plan servisi: host'un tek yenileme noktasından (SnapshotCache) periyodik snapshot
/// çeker, <see cref="NotificationEngine"/> ile eşik geçişlerini bulur ve kayıtlı cihazlara iter.
/// Telefon bütçe kısıtının (iOS 40–70/gün, Android min 15 dk) çözümü: telefon yoklamak yerine
/// host önemli değişimde uyandırır.
///
/// Spam koruması: her <c>dedupeKey</c> için cooldown penceresi (varsayılan 30 dk).
/// </summary>
public sealed class PushNotificationService(
    SnapshotCache cache,
    IDeviceRegistry devices,
    IPushDispatcher dispatcher,
    BridgeHostOptions options,
    ILogger<PushNotificationService> log) : BackgroundService
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastSent = new(StringComparer.Ordinal);
    private DashboardSnapshot? _previous;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.PushEnabled)
        {
            log.LogInformation("Push devre dışı (CODEXBRIDGE_PUSH_ENABLED=false).");
            return;
        }

        // Yenileme aralığı push tarama aralığını da belirler (host'un doğal kadansı).
        var interval = TimeSpan.FromSeconds(Math.Max(15, options.RefreshIntervalSeconds));
        log.LogInformation("Push servisi başladı: her {Seconds}s tarama, {Cooldown}dk cooldown.",
            interval.TotalSeconds, options.PushCooldownMinutes);

        using var timer = new PeriodicTimer(interval);
        do
        {
            try { await TickAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { log.LogWarning(ex, "Push tarama turu hata verdi (atlanıyor)."); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var current = await cache.GetAsync(ct);
        var events = NotificationEngine.Diff(_previous, current, NotificationThresholds.Default);
        _previous = current;
        if (events.Count == 0) return;

        var deviceList = await devices.ListAsync(ct);
        if (deviceList.Count == 0)
        {
            log.LogInformation("{Count} bildirim olayı üretildi ama kayıtlı cihaz yok.", events.Count);
            return;
        }

        var cooldown = TimeSpan.FromMinutes(options.PushCooldownMinutes);
        foreach (var ev in events)
        {
            if (!TryClaimCooldown(ev.DedupeKey, cooldown)) continue;
            await FanOutAsync(ev, deviceList, ct);
        }
    }

    private async Task FanOutAsync(NotificationEvent ev, IReadOnlyList<DeviceRegistration> deviceList, CancellationToken ct)
    {
        foreach (var device in deviceList)
        {
            var result = await dispatcher.SendAsync(device, ev, ct);
            if (result.ShouldUnregister)
            {
                await devices.RemoveAsync(device.Token, ct);
                log.LogInformation("Cihaz kayıttan düşürüldü ({Platform}): {Detail}", device.Platform, result.Detail);
            }
            else if (!result.Ok)
            {
                log.LogWarning("Push başarısız ({Platform}): {Detail}", device.Platform, result.Detail);
            }
        }
    }

    /// <summary>dedupeKey cooldown dışındaysa "gönder" der ve saati damgalar; içindeyse false.</summary>
    private bool TryClaimCooldown(string dedupeKey, TimeSpan cooldown)
    {
        var now = DateTimeOffset.UtcNow;
        while (true)
        {
            if (_lastSent.TryGetValue(dedupeKey, out var last))
            {
                if (now - last < cooldown) return false;
                if (_lastSent.TryUpdate(dedupeKey, now, last)) return true;
            }
            else if (_lastSent.TryAdd(dedupeKey, now))
            {
                return true;
            }
            // Yarış: başka thread güncelledi → yeniden değerlendir.
        }
    }
}
