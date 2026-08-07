using CodexBridge.Core.Notifications;
using Microsoft.Extensions.Logging;

namespace CodexBridge.Host.Push;

/// <summary>
/// Kimlik bilgisi (APNs .p8 / FCM service account) yapılandırılmadığında devreye giren yedek.
/// Gerçek push atmaz; olayı loglar. Böylece Faz 7 boru hattı kimlik bilgisi olmadan da uçtan
/// uca çalıştırılıp doğrulanabilir. GİZLİLİK: token loglanmaz, yalnızca platform + kısaltma.
/// </summary>
public sealed class LoggingPushDispatcher(ILogger<LoggingPushDispatcher> logger) : IPushDispatcher
{
    public bool Supports(PushPlatform platform) => true;

    public Task<PushResult> SendAsync(DeviceRegistration device, NotificationEvent ev, CancellationToken ct = default)
    {
        logger.LogInformation("PUSH (log) → {Platform} {TokenHint} · {Kind} · {Title}",
            device.Platform, TokenHint(device.Token), ev.Kind, ev.Title);
        return Task.FromResult(PushResult.Success("logged"));
    }

    internal static string TokenHint(string token)
        => token.Length <= 8 ? "…" : token[..4] + "…" + token[^4..];
}
