using System.Diagnostics;
using System.Runtime.InteropServices;
using CodexBridge.Core.Refresh;

namespace CodexBridge.Taskbar.Runtime;

/// <summary>
/// Adaptif yenilemenin Windows girdileri.
/// <list type="bullet">
///   <item><b>Pil tasarrufu:</b> <c>GetSystemPowerStatus</c> → <c>SystemStatusFlag</c>.
///   Açıksa yenileme 30 dakikaya iner.</item>
///   <item><b>Yerel ajan etkinliği:</b> <c>claude</c> / <c>codex</c> süreçleri çalışıyorsa
///   kota hızlı hareket ediyor demektir; aralık kısalır.</item>
/// </list>
/// Süreç taraması pahalı olduğu için sonuç kısa süre önbelleklenir.
/// </summary>
public sealed class WindowsRefreshSignals : IRefreshSignals
{
    private static readonly string[] AgentProcessNames = ["claude", "codex", "node"];
    private static readonly TimeSpan ProbeCacheTtl = TimeSpan.FromSeconds(30);

    private DateTimeOffset _agentProbedAt = DateTimeOffset.MinValue;
    private bool _agentSeen;

    public bool LowPowerOrThermalPressure
    {
        get
        {
            try
            {
                return GetSystemPowerStatus(out var status) && status.SystemStatusFlag == 1;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
        }
    }

    public bool LocalAgentActivityWithin5Min
    {
        get
        {
            var now = DateTimeOffset.UtcNow;
            if (now - _agentProbedAt < ProbeCacheTtl) return _agentSeen;

            _agentProbedAt = now;
            _agentSeen = false;
            foreach (string name in AgentProcessNames)
            {
                try
                {
                    if (Process.GetProcessesByName(name).Length > 0)
                    {
                        _agentSeen = true;
                        break;
                    }
                }
                catch (InvalidOperationException) { /* süreç listesi anlık değişebilir */ }
            }
            return _agentSeen;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
}
