using CodexBridge.Core.Dashboard;
using CodexBridge.Core.Sources;

namespace CodexBridge.JsHost;

/// <summary>
/// Faz 5 veri kaynağı: bir dizi JS sağlayıcı eklentisini çalıştırıp <c>dashboard/v1</c> snapshot'ı
/// üretir. Üçüncü taraf ikiliden (Win-CodexBar) bağımsızlık — sağlayıcı mantığı üst akıştan
/// senkronlanan <c>.js</c> dosyaları.
/// </summary>
public sealed class JsUsageSource(string preludeSource, IReadOnlyList<JsUsageSource.PluginSpec> plugins) : IUsageSource
{
    /// <summary>Bir eklenti örneği: kaynak + o örneğe özel host köprüsü (ayarlar/sırlar/çerez).</summary>
    public sealed record PluginSpec(string Id, string Name, string Source, IJsHostBridge Bridge);

    public Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken ct = default)
        => Task.Run(() =>
        {
            var now = DateTimeOffset.UtcNow;
            var rows = new List<ProviderRow>(plugins.Count);
            int sort = 0;
            foreach (var spec in plugins)
            {
                try
                {
                    using var rt = new JsProviderRuntime(preludeSource, spec.Source, spec.Bridge);
                    rows.Add(JsSnapshotMapper.Map(rt.FetchUsageJson(), spec.Id, spec.Name, sort++));
                }
                catch (Exception ex)
                {
                    // Bir eklentinin hatası diğerlerini düşürmesin (kısmi başarı doğru davranış).
                    rows.Add(new ProviderRow
                    {
                        Id = spec.Id, Name = spec.Name, Enabled = true, Source = "js-plugin",
                        Status = new ProviderStatus { Level = StatusLevel.Unknown, UpdatedAt = now },
                        Error = new ProviderError { Message = ex.Message },
                        Display = new DisplayHints { SortKey = sort++ },
                        UpdatedAt = now,
                    });
                }
            }

            return new DashboardSnapshot
            {
                SchemaVersion = 1,
                GeneratedAt = now,
                StaleAfterSeconds = 180,
                Host = new HostInfo { CodexBarVersion = "codexbridge-js", RefreshIntervalSeconds = 0 },
                Providers = rows,
            };
        }, ct);
}
