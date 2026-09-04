using CodexBridge.Core.Dashboard;
using CodexBridge.Core.Providers;

namespace CodexBridge.Core.Sources;

/// <summary>
/// Birden çok <see cref="IProviderSource"/>'u tek bir <c>dashboard/v1</c> snapshot'ında toplar.
///
/// <para><b>Hata izolasyonu:</b> bir sağlayıcının çekimi patlarsa diğerleri etkilenmez.
/// Ayrıca o sağlayıcının <b>son bilinen değeri</b> saklanır ve hata sırasında onu göstermeye
/// devam ederiz — satırın <c>updatedAt</c>'i eski kalır, böylece yüzeyler "veri yaşı"nı
/// dürüstçe gösterebilir. Hız sınırında (429) davranış budur: sayı kaybolmaz, yaşlanır.</para>
/// </summary>
public sealed class AggregateUsageSource(
    IReadOnlyList<IProviderSource> sources,
    TimeProvider? clock = null,
    string? version = null) : IUsageSource
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, ProviderRow> _lastGood = [];

    /// <summary>Snapshot'ın istemciye bildirdiği bayatlık eşiği. Şema minimumu 180 sn.</summary>
    public int StaleAfterSeconds { get; init; } = 180;

    /// <summary>Snapshot'a yazılacak yenileme aralığı ipucu.</summary>
    public int RefreshIntervalSeconds { get; set; }

    /// <summary>
    /// Son bilinen değerleri önceki bir snapshot'tan doldurur (diskteki kalıcı snapshot veya
    /// kaynak değiştirilirken devralınan bellek içi snapshot).
    ///
    /// <para><b>Neden gerekli:</b> <c>_lastGood</c> yalnızca bu nesnenin ömrü boyunca yaşıyor.
    /// Beslenmezse süreç yeniden başladıktan ya da ayarlar kaydedildikten (yeni
    /// <see cref="AggregateUsageSource"/> kurulur) sonraki İLK çekim patladığında
    /// <see cref="Degrade"/> gösterecek değer bulamıyor; boş hata satırı dönüyor ve bu satır
    /// diskteki iyi snapshot'ın üzerine yazılıyor. Canlı testte Claude 429'unda görüldü:
    /// pill sayıyı yaşlandırmak yerine <c>—</c> gösterdi.</para>
    ///
    /// <para>Yalnızca <b>gerçekten veri taşıyan</b> satırlar alınır: hata satırlarını devralmak
    /// bir hatayı sonsuza kadar "son bilinen değer" diye saklamak olurdu. Zaten var olan bir
    /// giriş ezilmez — canlı çekim her zaman diskten üstündür.</para>
    /// </summary>
    public void SeedLastGood(DashboardSnapshot? snapshot)
    {
        if (snapshot is null) return;

        lock (_gate)
        {
            foreach (var row in snapshot.Providers)
            {
                if (row.Error is not null || row.Windows.Count == 0) continue;
                if (_lastGood.ContainsKey(row.Id)) continue;
                _lastGood[row.Id] = row;
            }
        }
    }

    public async Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        var tasks = sources.Select(s => FetchOneAsync(s, ct)).ToArray();
        var rows = await Task.WhenAll(tasks);

        return new DashboardSnapshot
        {
            SchemaVersion = 1,
            GeneratedAt = _clock.GetUtcNow(),
            StaleAfterSeconds = StaleAfterSeconds,
            Host = new HostInfo
            {
                CodexBarVersion = version ?? "codexbridge",
                RefreshIntervalSeconds = RefreshIntervalSeconds,
            },
            Providers = [.. rows.OrderBy(r => r.Display?.SortKey ?? int.MaxValue)],
        };
    }

    private async Task<ProviderRow> FetchOneAsync(IProviderSource source, CancellationToken ct)
    {
        try
        {
            var row = await source.FetchAsync(ct);
            lock (_gate) { _lastGood[source.ProviderId] = row; }
            return row;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (ProviderSourceException ex)
        {
            return Degrade(source.ProviderId, ex.Kind, ex.Message);
        }
        catch (Exception ex)
        {
            // Beklenmeyen hata da bir sağlayıcıyı düşürmemeli. Mesaj tür adıyla sınırlı:
            // ham istisna metni token/hesap bilgisi taşıyabilir.
            return Degrade(source.ProviderId, ProviderErrorKind.Unknown,
                $"Beklenmeyen hata ({ex.GetType().Name}).");
        }
    }

    /// <summary>Hatada son bilinen satırı hata etiketiyle döndürür; hiç yoksa boş hata satırı.</summary>
    private ProviderRow Degrade(string providerId, ProviderErrorKind kind, string message)
    {
        var error = new ProviderError { Code = kind.ToString(), Message = message };

        lock (_gate)
        {
            if (_lastGood.TryGetValue(providerId, out var stale))
            {
                // UpdatedAt bilerek DEĞİŞTİRİLMİYOR: yüzeyler verinin yaşını buradan okuyor.
                return stale with { Error = error };
            }
        }

        return ProviderRowFactory.CreateError(providerId, kind, message, _clock.GetUtcNow());
    }
}
