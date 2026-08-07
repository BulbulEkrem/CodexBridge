using CodexBridge.Core.Notifications;
using Microsoft.Extensions.Logging;

namespace CodexBridge.Host.Push;

/// <summary>
/// Olayı cihaz platformuna göre doğru dispatcher'a yönlendirir (APNs→iOS, FCM→Android).
/// Bir platform için gerçek dispatcher yapılandırılmamışsa <see cref="LoggingPushDispatcher"/>'a
/// düşer — böylece boru hattı kimlik bilgisi olmadan da uçtan uca çalışır.
/// </summary>
public sealed class CompositePushDispatcher(
    IEnumerable<IPushDispatcher> dispatchers,
    LoggingPushDispatcher fallback,
    ILogger<CompositePushDispatcher> log) : IPushDispatcher
{
    private readonly IReadOnlyList<IPushDispatcher> _dispatchers = dispatchers.ToList();

    public bool Supports(PushPlatform platform) => true;

    public async Task<PushResult> SendAsync(DeviceRegistration device, NotificationEvent ev, CancellationToken ct = default)
    {
        var target = _dispatchers.FirstOrDefault(d => d.Supports(device.Platform)) ?? (IPushDispatcher)fallback;
        try
        {
            return await target.SendAsync(device, ev, ct);
        }
        catch (Exception impl) when (target != fallback)
        {
            // Gerçek dispatcher patlarsa olayı düşürme — logla (kısmi başarı doğru davranış).
            log.LogWarning(impl, "Push dispatcher {Platform} hata verdi, loga düşülüyor.", device.Platform);
            return await fallback.SendAsync(device, ev, ct);
        }
    }
}
