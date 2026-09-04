using System.Diagnostics;
using Microsoft.Win32;

namespace CodexBridge.Taskbar.Runtime;

/// <summary>
/// Windows açılışında otomatik başlatma. Paketsiz uygulama olduğumuz için yol
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> — makine geneli değil,
/// yalnızca bu kullanıcı (yönetici hakkı istemez).
/// </summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CodexBridge";

    /// <summary>Kayıtlı mı.</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) is string v && v.Length > 0;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    /// <summary>Kaydı ekler veya kaldırır. Başarısızlıkta <c>false</c> döner —
    /// ayar penceresi bunu kullanıcıya bildirir, sessizce yutmaz.</summary>
    public static bool Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null) return false;

            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                return true;
            }

            string? exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exe)) return false;

            // Yol boşluk içerebilir; tırnak içine al.
            key.SetValue(ValueName, $"\"{exe}\"", RegistryValueKind.String);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return false;
        }
    }
}
