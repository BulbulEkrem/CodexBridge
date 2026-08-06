using CodexBridge.Core.Dashboard;

namespace CodexBridge.Core.Sources;

/// <summary>
/// Faz 1 veri kaynağı: gerçek sağlayıcıya gitmeden, yüzeyi geliştirip test etmek için
/// makul sahte bir snapshot üretir. Her çağrıda yüzdeleri hafifçe oynatır ki yüzeyin
/// güncellemeye tepki verdiği görülebilsin.
/// </summary>
public sealed class FakeUsageSource : IUsageSource
{
    private readonly Random _rng = new(1);
    private double _codexUsed = 28, _claudeUsed = 69, _geminiUsed = 12;

    public Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        _codexUsed = Wobble(_codexUsed);
        _claudeUsed = Wobble(_claudeUsed);
        _geminiUsed = Wobble(_geminiUsed);

        var snapshot = new DashboardSnapshot
        {
            SchemaVersion = 1,
            GeneratedAt = now,
            StaleAfterSeconds = 180,
            Host = new HostInfo { CodexBarVersion = "codexbridge-0.1-fake", RefreshIntervalSeconds = 60 },
            Providers =
            [
                Provider("codex", "Codex", "oauth", _codexUsed, "#49A3B0", 0,
                    plan: "Pro 20x", resetIn: TimeSpan.FromHours(3.2), todayUsd: 1.04, month: 18.22, now: now),
                Provider("claude", "Claude", "oauth", _claudeUsed, "#D97757", 1,
                    plan: "Max 20x", resetIn: TimeSpan.FromHours(6.0), todayUsd: 2.31, month: 41.7, now: now),
                Provider("gemini", "Gemini", "apiToken", _geminiUsed, "#4285F4", 2,
                    plan: "Free", resetIn: TimeSpan.FromHours(20.5), todayUsd: 0.0, month: 0.0, now: now),
            ],
        };
        return Task.FromResult(snapshot);
    }

    private double Wobble(double v) => Math.Clamp(v + (_rng.NextDouble() - 0.5) * 2.0, 0, 100);

    private static ProviderRow Provider(
        string id, string name, string source, double used, string color, int sort,
        string plan, TimeSpan resetIn, double todayUsd, double month, DateTimeOffset now)
    {
        double usedRounded = Math.Round(used, 1);
        var level = used >= 90 ? StatusLevel.Critical : used >= 75 ? StatusLevel.Warning : StatusLevel.Ok;
        return new ProviderRow
        {
            Id = id,
            Name = name,
            Enabled = true,
            Source = source,
            Status = new ProviderStatus { Level = level, Label = level.ToString(), UpdatedAt = now },
            Identity = new ProviderIdentity { AccountEmail = "redacted@example.com", Plan = plan },
            Windows =
            [
                new RateWindow
                {
                    Kind = "session", Label = "Session",
                    UsedPercent = usedRounded, RemainingPercent = Math.Round(100 - used, 1),
                    ResetAt = now + resetIn,
                },
            ],
            Cost = new CostInfo { TodayUsd = todayUsd, Last30DaysUsd = month },
            Display = new DisplayHints { AccentColor = color, SortKey = sort, Priority = "normal" },
            Error = null,
            UpdatedAt = now,
        };
    }
}
