using CodexBridge.Core.Notifications;

namespace CodexBridge.Host.Push;

/// <summary>Bir bildirim olayını tek bir cihaza iten platform servisi (APNs/FCM/log).</summary>
public interface IPushDispatcher
{
    /// <summary>Bu dispatcher verilen platforma gönderebiliyor mu.</summary>
    bool Supports(PushPlatform platform);
    Task<PushResult> SendAsync(DeviceRegistration device, NotificationEvent ev, CancellationToken ct = default);
}

/// <summary>Tek bir push girişiminin sonucu.</summary>
public readonly record struct PushResult(bool Ok, bool ShouldUnregister, string? Detail)
{
    public static PushResult Success(string? detail = null) => new(true, false, detail);
    public static PushResult Fail(string detail) => new(false, false, detail);
    /// <summary>Cihaz token'ı geçersiz/artık yok (APNs 410 / FCM UNREGISTERED) → kayıttan düş.</summary>
    public static PushResult Gone(string detail) => new(false, true, detail);
}
