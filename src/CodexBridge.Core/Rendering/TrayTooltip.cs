using System.Text;
using CodexBridge.Core.Dashboard;
using CodexBridge.Core.Providers;
using CodexBridge.Core.Settings;

namespace CodexBridge.Core.Rendering;

/// <summary>
/// Tepsi ikonu araç ipucu metnini kurar.
///
/// <para><b>Sert sınır: 128 karakter</b> (sonlandırıcı dahil) — <c>NOTIFYICONDATAW.szTip</c>
/// bundan fazlasını kabul etmiyor. Satırlar <c>\r\n</c> ile ayrılıyor. Metin sığmazsa
/// sağlayıcılar sondan atılıyor; ilk satır her zaman en kısıtlayıcı olan.</para>
/// </summary>
public static class TrayTooltip
{
    /// <summary>Win32 sınırı: sonlandırıcı dahil 128 karakter.</summary>
    public const int MaxLength = 127;

    public static string Build(DashboardSnapshot? snapshot, AppSettings settings, DateTimeOffset now)
    {
        if (snapshot is null) return "CodexBridge · henüz veri yok";

        // En kısıtlayıcısı en üstte: ipucu kırpılırsa önce önemsiz olan gider.
        var rows = snapshot.Providers
            .Where(p => settings.IsEnabled(p.Id))
            .OrderByDescending(p => p.MostRestrictive()?.UsedPercent ?? -1)
            .ToList();

        if (rows.Count == 0) return "CodexBridge · sağlayıcı seçilmedi";

        var lines = new List<string>(rows.Count);
        foreach (var row in rows)
        {
            var worst = row.MostRestrictive();
            if (row.Error is not null && worst is null)
            {
                lines.Add($"{row.Name}: hata");
                continue;
            }
            if (worst is null)
            {
                lines.Add($"{row.Name}: —");
                continue;
            }

            string countdown = RateWindowFactory.FormatCountdown(worst.ResetAt, now) is { } c ? $" · {c}" : "";
            string stale = row.Error is not null ? " (eski)" : "";
            lines.Add($"{row.Name} %{(int)Math.Round(worst.UsedPercent ?? 0)}{countdown}{stale}");
        }

        return Fit(lines);
    }

    /// <summary>Satırları 128 karakterlik sınıra sığdırır; sığmayan satırları sondan atar.</summary>
    private static string Fit(List<string> lines)
    {
        var sb = new StringBuilder();
        foreach (string line in lines)
        {
            int extra = (sb.Length == 0 ? 0 : 2) + line.Length;
            if (sb.Length + extra > MaxLength) break;
            if (sb.Length > 0) sb.Append("\r\n");
            sb.Append(line);
        }

        // Tek satır bile sığmıyorsa kes: ipucunun hiç görünmemesindense kırpılmışı iyidir.
        if (sb.Length == 0 && lines.Count > 0)
            return lines[0][..Math.Min(lines[0].Length, MaxLength)];

        return sb.ToString();
    }
}
