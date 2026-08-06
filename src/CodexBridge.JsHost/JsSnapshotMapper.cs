using System.Text.Json;
using System.Text.Json.Serialization;
using CodexBridge.Core.Dashboard;

namespace CodexBridge.JsHost;

/// <summary>
/// JS eklentinin <c>fetchUsage</c> sonucunu (düz JSON) dashboard/v1 <see cref="ProviderRow"/>'a çevirir.
/// Kimlik daima maskelenir (e-posta yerel kısmı gizli) — sözleşme gereği.
/// </summary>
public static class JsSnapshotMapper
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public static ProviderRow Map(string resultJson, string id, string name, int sortKey = 0)
    {
        var r = JsonSerializer.Deserialize<JsResult>(resultJson, Opts) ?? new JsResult();

        var windows = new List<RateWindow>();
        AddWindow(windows, "session", "Session", r.Primary);
        AddWindow(windows, "weekly", "Weekly", r.Secondary);
        AddWindow(windows, "tertiary", "Tertiary", r.Tertiary);

        double max = 0;
        foreach (var w in windows) max = Math.Max(max, w.UsedPercent ?? 0);
        var level = max >= 90 ? StatusLevel.Critical : max >= 75 ? StatusLevel.Warning : StatusLevel.Ok;

        CreditInfo? credits = r.Cost?.Balance is { } bal
            ? new CreditInfo { Remaining = bal, Unit = r.Cost.Currency ?? "credits" }
            : null;

        return new ProviderRow
        {
            Id = id,
            Name = name,
            Enabled = true,
            Source = "js-plugin",
            Status = new ProviderStatus { Level = level, UpdatedAt = DateTimeOffset.UtcNow },
            Identity = r.Identity is null ? null : new ProviderIdentity
            {
                AccountEmail = MaskEmail(r.Identity.Email),
                Plan = r.Identity.LoginMethod ?? r.Identity.Organization,
            },
            Windows = windows,
            Credits = credits,
            Display = new DisplayHints { SortKey = sortKey, Priority = "normal" },
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static void AddWindow(List<RateWindow> list, string kind, string label, JsWindow? w)
    {
        if (w?.UsedPercent is not { } used) return;
        list.Add(new RateWindow
        {
            Kind = kind,
            Label = label,
            UsedPercent = Math.Round(used, 1),
            RemainingPercent = Math.Round(Math.Clamp(100 - used, 0, 100), 1),
            ResetAt = w.ResetsAt,
        });
    }

    /// <summary>E-posta yerel kısmını gizle: user@acme.com → redacted@acme.com.</summary>
    private static string? MaskEmail(string? email)
    {
        if (string.IsNullOrEmpty(email)) return null;
        int at = email.IndexOf('@');
        return at <= 0 ? "redacted" : "redacted" + email[at..];
    }

    // fetchUsage sonucunun ilgili alt kümesi (docs/plugins.md snapshot şekli).
    private sealed class JsResult
    {
        public JsWindow? Primary { get; set; }
        public JsWindow? Secondary { get; set; }
        public JsWindow? Tertiary { get; set; }
        public JsCost? Cost { get; set; }
        public JsIdentity? Identity { get; set; }
    }
    private sealed class JsWindow
    {
        public double? UsedPercent { get; set; }
        public DateTimeOffset? ResetsAt { get; set; }
    }
    private sealed class JsCost
    {
        public double? Used { get; set; }
        public double? Limit { get; set; }
        public double? Balance { get; set; }
        public string? Currency { get; set; }
    }
    private sealed class JsIdentity
    {
        public string? Email { get; set; }
        public string? Organization { get; set; }
        public string? LoginMethod { get; set; }
    }
}
