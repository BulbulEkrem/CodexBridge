using System.Runtime.InteropServices;
using CodexBridge.Widget;

// Widget host bu süreci COM üzerinden başlatıyor: kendimizi sınıf fabrikası olarak kaydedip
// sabitlenmiş widget kalmayana kadar mesaj döngüsünde bekliyoruz.
//
// Süreç ömrü bilinçli olarak kısa: pano kapanınca widget'lar Deactivate alır, sabitlenmiş
// widget kalmazsa çıkarız. Ana uygulamadan bağımsızız — o kapalıyken de pano son yazılan
// snapshot.json'u gösterir.

var provider = new WidgetProvider();
uint cookie = 0;

try
{
    cookie = ComServer.Register(provider);
    provider.RecoverRunningWidgets();
    ComServer.RunMessageLoop(() => provider.HasWidgets);
}
finally
{
    if (cookie != 0) ComServer.Revoke(cookie);
}

return 0;

/// <summary>
/// Yönetilen bir nesneyi COM sınıf sunucusu olarak kaydeder. Windows App SDK'nın widget
/// örneği bunu C++ tarafında yapıyor; burada el yazımı P/Invoke ile aynısı kuruluyor
/// (depo kuralı: CsWin32 yerine el yazımı, öngörülebilir derleme için).
/// </summary>
internal static class ComServer
{
    private const uint CLSCTX_LOCAL_SERVER = 0x4;
    private const uint REGCLS_MULTIPLEUSE = 1;
    private const uint REGCLS_SUSPENDED = 4;

    private const uint WM_QUIT = 0x0012;

    public static uint Register(WidgetProvider provider)
    {
        var clsid = new Guid(WidgetProvider.ClassId);
        var factory = new ClassFactory(provider);

        int hr = CoRegisterClassObject(ref clsid, factory, CLSCTX_LOCAL_SERVER,
            REGCLS_MULTIPLEUSE | REGCLS_SUSPENDED, out uint cookie);
        Marshal.ThrowExceptionForHR(hr);

        Marshal.ThrowExceptionForHR(CoResumeClassObjects());
        return cookie;
    }

    public static void Revoke(uint cookie) => CoRevokeClassObject(cookie);

    /// <summary>Sabitlenmiş widget kalmayana kadar mesaj pompalar.</summary>
    public static void RunMessageLoop(Func<bool> keepRunning)
    {
        // Widget host çağrıları COM üzerinden geldiği için mesaj döngüsü şart.
        while (true)
        {
            if (PeekMessageW(out MSG msg, IntPtr.Zero, 0, 0, PM_REMOVE: 1))
            {
                if (msg.message == WM_QUIT) return;
                TranslateMessage(ref msg);
                DispatchMessageW(ref msg);
                continue;
            }

            // Boşta: widget kalmadıysa çık, yoksa CPU yakmadan bekle.
            if (!keepRunning()) return;
            Thread.Sleep(50);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [DllImport("ole32.dll")]
    private static extern int CoRegisterClassObject(
        ref Guid rclsid,
        [MarshalAs(UnmanagedType.IUnknown)] object pUnk,
        uint dwClsContext, uint flags, out uint lpdwRegister);

    [DllImport("ole32.dll")]
    private static extern int CoRevokeClassObject(uint dwRegister);

    [DllImport("ole32.dll")]
    private static extern int CoResumeClassObjects();

    [DllImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessageW(out MSG lpMsg, IntPtr hWnd, uint min, uint max, uint PM_REMOVE);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static extern IntPtr DispatchMessageW(ref MSG lpMsg);
}

/// <summary>Tek örnekli sınıf fabrikası: her istekte aynı sağlayıcıyı döndürür.</summary>
[ComVisible(true)]
[ComDefaultInterface(typeof(IClassFactory))]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class ClassFactory(WidgetProvider provider) : IClassFactory
{
    public int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject)
    {
        ppvObject = IntPtr.Zero;
        if (pUnkOuter != IntPtr.Zero) return unchecked((int)0x80040110); // CLASS_E_NOAGGREGATION

        IntPtr unknown = Marshal.GetIUnknownForObject(provider);
        try
        {
            return Marshal.QueryInterface(unknown, ref riid, out ppvObject);
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    public int LockServer(bool fLock) => 0; // S_OK — süreç ömrünü widget sayısı yönetiyor.
}

[ComImport]
[Guid("00000001-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IClassFactory
{
    [PreserveSig] int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject);
    [PreserveSig] int LockServer([MarshalAs(UnmanagedType.Bool)] bool fLock);
}
