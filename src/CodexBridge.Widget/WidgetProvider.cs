using System.Runtime.InteropServices;
using CodexBridge.Core;
using CodexBridge.Core.Rendering;
using CodexBridge.Core.Settings;
using CodexBridge.Core.Sources;
using Microsoft.Windows.Widgets.Providers;

// Core'daki WidgetSize ile Windows App SDK'nınki aynı adı taşıyor; hangisinin kastedildiği
// karışmasın diye SDK türü takma adla anılıyor.
using WinWidgetSize = Microsoft.Windows.Widgets.WidgetSize;

namespace CodexBridge.Widget;

/// <summary>
/// Windows Widget sağlayıcısı (<kbd>Win</kbd>+<kbd>W</kbd> panosu).
///
/// <para><b>Bu süreç sağlayıcıya HİÇ gitmez.</b> Kotayı ana uygulamanın yazdığı
/// <c>snapshot.json</c>'dan okur. Sebebi basit: widget host bu süreci pano açılınca
/// uyandırıp kapanınca öldürüyor; kendi çekimini yapsaydı aynı kotayı ikinci kez
/// tüketirdik ve iki süreç aynı token'ı yenilemeye çalışırdı.</para>
///
/// <para>Microsoft'un uyarısı: <c>Activate</c> ile <c>Deactivate</c> arasındaki pencere
/// çok kısa olabilir, güncelleme yolu hızlı olmalı. Dosyadan okuma tam da bu yüzden
/// doğru seçim — ağ beklemesi yok.</para>
/// </summary>
[ComVisible(true)]
[Guid(ClassId)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class WidgetProvider : IWidgetProvider
{
    /// <summary>COM sınıf kimliği. <b>Package.appxmanifest'teki değerle birebir aynı olmalı.</b></summary>
    public const string ClassId = "3f7c9b21-5d84-4a6e-9c13-8b2f4e7a06d5";

    /// <summary>Manifest'teki <c>Definition Id</c> ile aynı olmalı.</summary>
    private const string QuotaWidgetId = "CodexBridge_Quota";

    private readonly SnapshotStore _store = new();
    private readonly Lock _gate = new();
    private readonly Dictionary<string, ActiveWidget> _widgets = [];

    private FileSystemWatcher? _watcher;

    private sealed record ActiveWidget(string Id, string DefinitionId, WidgetSize Size, bool IsActive);

    // ---- IWidgetProvider ----

    public void CreateWidget(WidgetContext widgetContext)
    {
        lock (_gate)
        {
            _widgets[widgetContext.Id] = new ActiveWidget(
                widgetContext.Id, widgetContext.DefinitionId, ParseSize(widgetContext.Size), IsActive: false);
        }
        UpdateWidget(widgetContext.Id);
    }

    public void DeleteWidget(string widgetId, string customState)
    {
        lock (_gate) { _widgets.Remove(widgetId); }
        StopWatchingIfIdle();
    }

    public void OnActionInvoked(WidgetActionInvokedArgs actionInvokedArgs)
    {
        // Şu an widget salt görüntüleme. Yenileme isteği eylemi eklenirse buraya bağlanır;
        // ancak yenilemeyi biz yapmayız — ana uygulamaya sinyal göndeririz.
        UpdateWidget(actionInvokedArgs.WidgetContext.Id);
    }

    public void OnWidgetContextChanged(WidgetContextChangedArgs contextChangedArgs)
    {
        var context = contextChangedArgs.WidgetContext;
        lock (_gate)
        {
            if (_widgets.TryGetValue(context.Id, out var existing))
                _widgets[context.Id] = existing with { Size = ParseSize(context.Size) };
        }
        UpdateWidget(context.Id);
    }

    public void Activate(WidgetContext widgetContext)
    {
        lock (_gate)
        {
            if (_widgets.TryGetValue(widgetContext.Id, out var existing))
                _widgets[widgetContext.Id] = existing with { IsActive = true };
        }

        StartWatching();
        UpdateWidget(widgetContext.Id);
    }

    public void Deactivate(string widgetId)
    {
        lock (_gate)
        {
            if (_widgets.TryGetValue(widgetId, out var existing))
                _widgets[widgetId] = existing with { IsActive = false };
        }
        StopWatchingIfIdle();
    }

    /// <summary>Sağlayıcı yeniden başladığında (çökme, oturum açma) sabitlenmiş widget'ları geri yükler.</summary>
    public void RecoverRunningWidgets()
    {
        try
        {
            foreach (var info in WidgetManager.GetDefault().GetWidgetInfos())
            {
                var context = info.WidgetContext;
                lock (_gate)
                {
                    _widgets[context.Id] = new ActiveWidget(
                        context.Id, context.DefinitionId, ParseSize(context.Size), context.IsActive);
                }
                UpdateWidget(context.Id);
            }
        }
        catch (Exception)
        {
            // Kurtarma başarısızsa widget'lar bir sonraki etkileşimde güncellenir.
        }
    }

    /// <summary>Sabitlenmiş widget kalmadıysa süreç kapanabilir.</summary>
    public bool HasWidgets
    {
        get { lock (_gate) { return _widgets.Count > 0; } }
    }

    // ---- Güncelleme ----

    private void UpdateWidget(string widgetId)
    {
        ActiveWidget? widget;
        lock (_gate) { _widgets.TryGetValue(widgetId, out widget); }
        if (widget is null) return;

        var settings = AppSettings.Load();
        var snapshot = _store.Read();

        string template = WidgetCard.BuildTemplate();
        string data = WidgetCard.BuildData(snapshot, settings, DateTimeOffset.UtcNow, widget.Size);

        try
        {
            WidgetManager.GetDefault().UpdateWidget(new WidgetUpdateRequestOptions(widgetId)
            {
                Template = template,
                Data = data,
                CustomState = widget.DefinitionId,
            });
        }
        catch (Exception)
        {
            // Host kapanmış olabilir; bir sonraki Activate'te tekrar denenecek.
        }
    }

    private void UpdateAllActive()
    {
        List<string> ids;
        lock (_gate) { ids = _widgets.Values.Where(w => w.IsActive).Select(w => w.Id).ToList(); }
        foreach (string id in ids) UpdateWidget(id);
    }

    // ---- snapshot.json izleme ----

    /// <summary>Yalnızca pano açıkken dosyayı izler: kapalıyken dosya olayı dinlemek boşuna iş.</summary>
    private void StartWatching()
    {
        if (_watcher is not null) return;

        string dir = Path.GetDirectoryName(_store.Path) ?? AppPaths.LocalRoot;
        string file = Path.GetFileName(_store.Path);
        if (!Directory.Exists(dir)) return;

        _watcher = new FileSystemWatcher(dir, file)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += OnSnapshotFileChanged;
        _watcher.Created += OnSnapshotFileChanged;
        _watcher.Renamed += OnSnapshotFileChanged;
    }

    private void OnSnapshotFileChanged(object sender, FileSystemEventArgs e) => UpdateAllActive();

    private void StopWatchingIfIdle()
    {
        bool anyActive;
        lock (_gate) { anyActive = _widgets.Values.Any(w => w.IsActive); }
        if (anyActive || _watcher is null) return;

        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _watcher = null;
    }

    private static WidgetSize ParseSize(WinWidgetSize size) => size switch
    {
        WinWidgetSize.Small => WidgetSize.Small,
        WinWidgetSize.Large => WidgetSize.Large,
        _ => WidgetSize.Medium,
    };
}
