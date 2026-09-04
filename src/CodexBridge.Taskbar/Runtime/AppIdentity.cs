using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CodexBridge.Taskbar.Runtime;

/// <summary>
/// Paketsiz uygulamanın Windows'a kendini tanıtması.
///
/// <para>Bildirimlerin üst şeridinde uygulama adı ve ikonu kabuk tarafından çizilir ve
/// <b>AppUserModelID</b>'den okunur. Paketsiz bir uygulamada bu kimliği süreç kendi
/// bildirmek zorunda; bildirmezse toast'lar exe adıyla görünür.</para>
///
/// <para>Kayıtlar <c>HKCU</c> altına yazılır — yönetici hakkı istemez, yalnızca bu kullanıcıyı
/// etkiler.</para>
/// </summary>
internal static class AppIdentity
{
    /// <summary>Uygulamanın AppUserModelID'si. Bildirim kaydı ve görev çubuğu gruplaması
    /// bu değere bağlı — <b>değiştirilirse mevcut bildirimler ve tepsi yeri sıfırlanır.</b></summary>
    public const string Aumid = "BulbulEkrem.CodexBridge";

    public const string DisplayName = "CodexBridge";

    /// <summary>Süreç kimliğini ayarlar ve kabuk kayıtlarını yazar. Başarısızlık ölümcül değil:
    /// bildirimler yine çalışır, sadece adı/ikonu daha çirkin görünür.</summary>
    public static void Apply()
    {
        try { SetCurrentProcessExplicitAppUserModelID(Aumid); }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException) { /* eski Windows */ }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{Aumid}", writable: true);
            if (key is null) return;

            key.SetValue("DisplayName", DisplayName, RegistryValueKind.String);

            string? exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exe))
            {
                // İkon exe'nin kendi kaynağından alınır; ayrı bir .ico dağıtmaya gerek yok.
                key.SetValue("IconUri", exe, RegistryValueKind.String);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // Kayıt yazılamadı; bildirim yine gönderilebilir.
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SetCurrentProcessExplicitAppUserModelID(
        [MarshalAs(UnmanagedType.LPWStr)] string appID);
}
