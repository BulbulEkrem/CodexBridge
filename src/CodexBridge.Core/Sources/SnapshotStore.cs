using CodexBridge.Core.Dashboard;

namespace CodexBridge.Core.Sources;

/// <summary>
/// Süreçler arası paylaşılan <c>snapshot.json</c>. <b>Tek yazıcı, çok okuyucu:</b>
/// yalnızca ana uygulama yazar; widget sağlayıcısı gibi kısa ömürlü süreçler okur.
///
/// <para>Neden dosya, named pipe değil: widget süreci Widgets host'u tarafından pano açılınca
/// uyandırılıp kapanınca öldürülüyor. Pipe için bizim sürecin ayakta olması gerekir; kullanıcı
/// band'ı kapattıysa widget boş kalırdı. Dosya her koşulda okunur ve "son bilinen durum + yaşı"
/// ilkemiz zaten bunu bekliyor.</para>
///
/// <para>Yazım <see cref="AtomicFile"/> ile yapılır; okuyucu asla yarım JSON görmez.</para>
/// </summary>
public sealed class SnapshotStore(string? path = null)
{
    /// <summary>Snapshot dosyasının yolu.</summary>
    public string Path => path ?? AppPaths.SnapshotFile;

    public void Write(DashboardSnapshot snapshot) => AtomicFile.WriteAllText(Path, snapshot.ToJson());

    /// <summary>Snapshot'ı okur. Dosya yoksa, kilitliyse veya bozuksa <c>null</c> döner —
    /// okuyucu yüzeyler bunu "henüz veri yok" olarak gösterir.</summary>
    public DashboardSnapshot? Read()
    {
        try
        {
            if (!File.Exists(Path)) return null;
            return DashboardSnapshot.FromJson(File.ReadAllText(Path));
        }
        catch (IOException) { return null; }
        catch (System.Text.Json.JsonException) { return null; }
    }
}

/// <summary>Diskteki snapshot'ı okuyan <see cref="IUsageSource"/>. Ana uygulamanın dışındaki
/// süreçler (widget) bunu kullanır — kendileri sağlayıcıya <b>gitmez</b>.</summary>
public sealed class SnapshotFileSource(SnapshotStore? store = null) : IUsageSource
{
    private readonly SnapshotStore _store = store ?? new SnapshotStore();

    public Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken ct = default) =>
        Task.FromResult(_store.Read()
            ?? throw new InvalidOperationException("Henüz snapshot yazılmamış."));
}
