using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CodexBridge.Core.Security;

/// <summary>
/// Windows DPAPI (<c>CryptProtectData</c> / <c>CryptUnprotectData</c>) için el yazımı P/Invoke.
/// <c>System.Security.Cryptography.ProtectedData</c> paketi yerine doğrudan çağrı tercih edildi:
/// depo kuralı el yazımı P/Invoke (öngörülebilir derleme, ek bağımlılık yok).
///
/// Koruma kapsamı <b>kullanıcı</b>: şifreli veri yalnızca aynı Windows kullanıcısı tarafından
/// çözülebilir; başka bir kullanıcı veya makine çözemez.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class Dpapi
{
    private const uint CryptprotectUiForbidden = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob pDataIn, string? szDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved,
        IntPtr pPromptStruct, uint dwFlags, out DataBlob pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob pDataIn, IntPtr ppszDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved,
        IntPtr pPromptStruct, uint dwFlags, out DataBlob pDataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    public static byte[] Protect(byte[] plain) => Run(plain, protect: true);

    public static byte[] Unprotect(byte[] cipher) => Run(cipher, protect: false);

    private static byte[] Run(byte[] input, bool protect)
    {
        var inBlob = new DataBlob();
        var outBlob = new DataBlob();
        try
        {
            inBlob.cbData = input.Length;
            inBlob.pbData = Marshal.AllocHGlobal(Math.Max(input.Length, 1));
            Marshal.Copy(input, 0, inBlob.pbData, input.Length);

            bool ok = protect
                ? CryptProtectData(ref inBlob, "CodexBridge", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    CryptprotectUiForbidden, out outBlob)
                : CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    CryptprotectUiForbidden, out outBlob);

            if (!ok)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),
                    protect ? "DPAPI koruma başarısız." : "DPAPI çözme başarısız.");

            var result = new byte[outBlob.cbData];
            Marshal.Copy(outBlob.pbData, result, 0, outBlob.cbData);
            return result;
        }
        finally
        {
            if (inBlob.pbData != IntPtr.Zero) Marshal.FreeHGlobal(inBlob.pbData);
            if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
        }
    }
}
