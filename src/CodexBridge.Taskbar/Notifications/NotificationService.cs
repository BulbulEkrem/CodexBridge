using CodexBridge.Core.Dashboard;
using CodexBridge.Core.Notifications;
using CodexBridge.Core.Providers;
using CodexBridge.Core.Settings;
using CodexBridge.Taskbar.Runtime;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace CodexBridge.Taskbar.Notifications;

/// <summary>
/// Windows bildirim katmanı. İki ayrı işi var:
///
/// <list type="number">
///   <item><b>Canlı kota kartı</b> — sağlayıcı başına, ilerleme çubuklu, Bildirim Merkezi'nde
///   <b>kalıcı</b> duran bir kart. Bir kez gönderilir, sonraki her yenilemede
///   <c>UpdateAsync</c> ile <b>yerinde</b> tazelenir: tekrar pop-up olmaz, listede yerinden
///   oynamaz. Windows'ta widget'a en yakın şey budur ve MSIX gerektirmez.</item>
///
///   <item><b>Eşik uyarıları</b> — <see cref="NotificationEngine"/> iki snapshot arasında
///   eşik geçişi bulduğunda gönderilir. Kritik geçişte <c>Urgent</c> senaryosu kullanılır ve
///   bildirim Rahatsız Etmeyin'i deler.</item>
/// </list>
///
/// <para>Ayrım önemli: canlı kart <b>güncellenir</b> (sessiz), uyarı <b>gönderilir</b> (pop-up).
/// Aynı kartı eşikte güncellemek kullanıcının o anı kaçırmasına yol açardı.</para>
/// </summary>
public sealed class NotificationService(AppHost host) : IDisposable
{
    private const string QuotaGroup = "quota";
    private const string AlertGroup = "alert";

    private readonly Dictionary<string, uint> _sequence = [];
    private readonly HashSet<string> _liveCardShown = [];

    private DashboardSnapshot? _previous;
    private bool _registered;

    /// <summary>Kullanıcı bildirime tıkladı ve ayarların açılmasını istedi.</summary>
    public event Action? OpenSettingsRequested;

    public void Start()
    {
        try
        {
            AppNotificationManager.Default.NotificationInvoked += OnInvoked;
            // Paketsiz uygulamada da COM sunucu kaydını bu çağrı kuruyor.
            AppNotificationManager.Default.Register();
            _registered = true;
        }
        catch (Exception)
        {
            // Bildirim kaydı başarısız olabilir (yükseltilmiş süreç, kısıtlı ortam).
            // Diğer yüzeyler etkilenmemeli.
            _registered = false;
            return;
        }

        host.Updated += OnSnapshot;
    }

    private void OnInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        if (args.Arguments.TryGetValue("action", out string? action) && action == "settings")
            OpenSettingsRequested?.Invoke();
        else
            host.Coordinator.NoteInteraction();
    }

    private async void OnSnapshot(DashboardSnapshot snapshot)
    {
        if (!_registered) return;

        var settings = host.Settings;
        var previous = _previous;
        _previous = snapshot;

        try
        {
            await UpdateLiveCardsAsync(snapshot, settings);

            if (settings.NotificationsEnabled)
                PublishAlerts(previous, snapshot, settings);
        }
        catch (Exception)
        {
            // Bildirim gönderimi hiçbir koşulda yenileme döngüsünü ya da diğer yüzeyleri
            // düşürmemeli; bir tur atlanması kabul edilebilir.
        }
    }

    // ---- 1) Canlı kota kartı ----

    private async Task UpdateLiveCardsAsync(DashboardSnapshot snapshot, AppSettings settings)
    {
        foreach (var row in snapshot.Providers)
        {
            if (!settings.IsEnabled(row.Id)) continue;

            var session = row.Window(WindowKinds.Session);
            var weekly = row.Window(WindowKinds.Weekly);
            if (session is null && weekly is null) continue;

            var data = BuildProgressData(row, session, weekly, snapshot.GeneratedAt);

            if (_liveCardShown.Contains(row.Id))
            {
                // Yerinde güncelleme: pop-up yok, Bildirim Merkezi'nde konum korunur.
                var result = await AppNotificationManager.Default.UpdateAsync(data, row.Id, QuotaGroup);

                // Kullanıcı kartı kapattıysa güncelleme başarısız olur; bir daha göster.
                // NOT: sonucun tip adı Windows App SDK sürümüne göre değişebildiği için
                // metin karşılaştırması yapılıyor. Windows'ta derlendikten sonra
                // doğrudan enum karşılaştırmasına çevrilmeli.
                if (!string.Equals(result.ToString(), "Succeeded", StringComparison.Ordinal))
                    _liveCardShown.Remove(row.Id);
            }

            if (!_liveCardShown.Contains(row.Id))
            {
                ShowLiveCard(row, data);
                _liveCardShown.Add(row.Id);
            }
        }
    }

    private void ShowLiveCard(ProviderRow row, AppNotificationProgressData data)
    {
        var builder = new AppNotificationBuilder()
            .AddArgument("action", "open")
            .AddArgument("provider", row.Id)
            .AddText(BuildCardTitle(row))
            .AddProgressBar(new AppNotificationProgressBar()
                .BindTitle().BindValue().BindValueStringOverride().BindStatus());

        var notification = builder.BuildNotification();
        notification.Tag = row.Id;
        notification.Group = QuotaGroup;
        notification.Progress = data;

        AppNotificationManager.Default.Show(notification);
    }

    /// <summary>
    /// Kart tek bir ilerleme çubuğu taşır: <b>en kısıtlayıcı</b> pencere. İki çubuk teknik
    /// olarak mümkün ama yalnızca ilk çubuk veri bağlamayla güncellenebildiği için, güncellenen
    /// çubuğun her zaman "önemli olan" olması gerekiyor.
    /// </summary>
    private static AppNotificationProgressData BuildProgressData(
        ProviderRow row, RateWindow? session, RateWindow? weekly, DateTimeOffset generatedAt)
    {
        var worst = row.MostRestrictive() ?? session ?? weekly!;
        double used = worst.UsedPercent ?? 0;

        string countdown = RateWindowFactory.FormatCountdown(worst.ResetAt, DateTimeOffset.UtcNow) is { } c
            ? $" · {c}" : "";

        // İkinci pencerenin özeti durum satırına sıkıştırılıyor: tek çubukta iki bilgi.
        string other = worst.Kind == WindowKinds.Session ? Summarize(weekly) : Summarize(session);
        string status = row.Error is { } err
            ? $"⚠ {err.Message}"
            : other.Length > 0 ? other : $"Yenilendi {generatedAt.ToLocalTime():HH:mm}";

        var sequence = NextSequence(row.Id);
        return new AppNotificationProgressData(sequence)
        {
            Title = worst.Label ?? worst.Kind,
            Value = Math.Clamp(used / 100.0, 0, 1),
            ValueStringOverride = $"%{(int)Math.Round(used)}{countdown}",
            Status = status,
        };
    }

    private static string Summarize(RateWindow? window) =>
        window?.UsedPercent is { } p ? $"{window.Label ?? window.Kind} %{(int)Math.Round(p)}" : "";

    /// <summary>Her güncellemede artan dizi numarası; platform hangisinin yeni olduğunu bundan anlar.</summary>
    private uint NextSequence(string providerId)
    {
        uint next = _sequence.TryGetValue(providerId, out uint current) ? current + 1 : 1;
        _sequence[providerId] = next;
        return next;
    }

    private static string BuildCardTitle(ProviderRow row) =>
        row.Identity?.Plan is { Length: > 0 } plan ? $"{row.Name} · {plan}" : row.Name;

    // ---- 2) Eşik uyarıları ----

    private void PublishAlerts(DashboardSnapshot? previous, DashboardSnapshot current, AppSettings settings)
    {
        var thresholds = new NotificationThresholds
        {
            WarningPercent = settings.WarnPercent,
            CriticalPercent = settings.CritPercent,
        };

        foreach (var evt in NotificationEngine.Diff(previous, current, thresholds))
        {
            if (!settings.IsEnabled(evt.ProviderId)) continue;
            ShowAlert(evt);
        }
    }

    private static void ShowAlert(NotificationEvent evt)
    {
        var builder = new AppNotificationBuilder()
            .AddArgument("action", "open")
            .AddArgument("provider", evt.ProviderId)
            .AddText(evt.Title)
            .AddText(evt.Body)
            .AddButton(new AppNotificationButton("Ayarlar").AddArgument("action", "settings"));

        // Kota bitmek üzereyse bildirim Rahatsız Etmeyin'i delmeli: ajan duracak,
        // kullanıcının bunu sonradan öğrenmesinin bir faydası yok.
        if (evt.Kind == NotificationKind.QuotaCritical && AppNotificationBuilder.IsUrgentScenarioSupported())
            builder.SetScenario(AppNotificationScenario.Urgent);

        var notification = builder.BuildNotification();
        // Aynı geçişin tekrarı eskisinin yerine geçsin diye dedupe anahtarı etiket oluyor.
        notification.Tag = Sanitize(evt.DedupeKey);
        notification.Group = AlertGroup;

        AppNotificationManager.Default.Show(notification);
    }

    /// <summary>Etiket alanı sınırlı karakter kabul ediyor; anahtarı güvenli hale getir.</summary>
    private static string Sanitize(string key)
    {
        Span<char> buffer = stackalloc char[Math.Min(key.Length, 60)];
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] = char.IsLetterOrDigit(key[i]) ? key[i] : '-';
        return new string(buffer);
    }

    public void Dispose()
    {
        host.Updated -= OnSnapshot;
        if (!_registered) return;

        try
        {
            AppNotificationManager.Default.NotificationInvoked -= OnInvoked;
            AppNotificationManager.Default.Unregister();
        }
        catch (Exception)
        {
            // Kapanışta kayıt silinemezse bir sonraki açılışta üzerine yazılır.
        }
        _registered = false;
    }
}
