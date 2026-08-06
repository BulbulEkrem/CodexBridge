// CodexBridge — Faz 0 spike: görev çubuğuna pencere parent'lama (WinUI'siz, salt Win32/WinForms).
// Amaç: Deskband11'in MoveToTaskbar tekniğinin çekirdeğini bu makinede canlı kanıtlamak
// ve referansın ÇÖZEMEDİĞİ Explorer-restart (TaskbarCreated) hayatta kalmasını denemek.
using System.Runtime.InteropServices;

namespace TaskbarParentSpike;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new BandForm());
    }
}

sealed class BandForm : Form
{
    // --- Win32 ---
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindow(string? cls, string? win);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? win);
    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetParent(IntPtr child, IntPtr newParent);
    [DllImport("user32.dll", SetLastError = true)]
    static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll", SetLastError = true)]
    static extern int SetWindowLong(IntPtr hWnd, int index, int newLong);
    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")]
    static extern uint RegisterWindowMessage(string msg);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int left, top, right, bottom; public int Width => right - left; public int Height => bottom - top; }

    const int GWL_STYLE = -16;
    const int WS_POPUP = unchecked((int)0x80000000);
    const int WS_CHILD = 0x40000000;
    const uint SWP_NOACTIVATE = 0x0010, SWP_FRAMECHANGED = 0x0020, SWP_SHOWWINDOW = 0x0040;
    const int WM_DISPLAYCHANGE = 0x007E;

    readonly uint WM_TASKBARCREATED = RegisterWindowMessage("TaskbarCreated");
    readonly Label _label;
    int _reparentCount;

    public BandForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(0x1E, 0x88, 0x9A); // CodexBridge teal
        Width = 150; Height = 40;
        _label = new Label
        {
            Dock = DockStyle.Fill, ForeColor = Color.White, Text = "◧ CB  42%",
            TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 10f, FontStyle.Bold)
        };
        Controls.Add(_label);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        MoveToTaskbar();
    }

    void MoveToTaskbar()
    {
        IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero) { _label.Text = "Shell_TrayWnd YOK"; return; }
        IntPtr rebar = FindWindowEx(taskbar, IntPtr.Zero, "ReBarWindow32", null);

        // WS_POPUP çıkar, WS_CHILD ekle, sonra çubuğa parent'la.
        int style = GetWindowLong(Handle, GWL_STYLE);
        style = (style & ~WS_POPUP) | WS_CHILD;
        SetWindowLong(Handle, GWL_STYLE, style);
        SetParent(Handle, taskbar);

        GetWindowRect(taskbar, out RECT tb);
        int y = 0, h = tb.Height;
        if (rebar != IntPtr.Zero) { GetWindowRect(rebar, out RECT rb); y = rb.top - tb.top; h = rb.Height; }

        // Çubuğun soluna yerleştir (Win11 ortalanmış çubukta bu alan genelde boş).
        SetWindowPos(Handle, IntPtr.Zero, 12, y, Width, h,
            SWP_FRAMECHANGED | SWP_NOACTIVATE | SWP_SHOWWINDOW);

        _label.Text = _reparentCount == 0 ? "◧ CB  42%" : $"◧ CB  42%  (re×{_reparentCount})";
    }

    protected override void WndProc(ref Message m)
    {
        // Explorer yeniden başladı → çubuk penceresi yeni HWND aldı. Deskband11'in pes ettiği yer.
        // Denememiz: kapatmak yerine yeniden parent'la.
        if (m.Msg == WM_TASKBARCREATED)
        {
            _reparentCount++;
            BeginInvoke(new Action(() => { System.Threading.Thread.Sleep(400); MoveToTaskbar(); }));
        }
        else if (m.Msg == WM_DISPLAYCHANGE)
        {
            BeginInvoke(new Action(MoveToTaskbar));
        }
        base.WndProc(ref m);
    }
}
