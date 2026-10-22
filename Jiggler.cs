// Mouse Jiggler - keeps the PC awake and "active" by nudging the mouse 1 pixel
// and back when you've been idle. Tiny window, no install, no internet.
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

class Jiggler : Form
{
    [StructLayout(LayoutKind.Sequential)]
    struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public MOUSEINPUT mi; }

    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
    [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint flags);

    const uint ES_CONTINUOUS = 0x80000000, ES_SYSTEM_REQUIRED = 0x1, ES_DISPLAY_REQUIRED = 0x2;
    const uint MOUSEEVENTF_MOVE = 0x1;

    Timer timer = new Timer();
    Button toggle = new Button();
    Label status = new Label();
    NumericUpDown seconds = new NumericUpDown();
    NotifyIcon tray = new NotifyIcon();
    int jiggles;

    Jiggler()
    {
        Text = "Mouse Jiggler";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        ClientSize = new Size(260, 130);
        Font = new Font("Segoe UI", 10);
        Icon = SystemIcons.Application;

        var lbl = new Label { Text = "Jiggle after idle (sec):", Location = new Point(12, 15), AutoSize = true };
        seconds.Location = new Point(175, 12);
        seconds.Width = 70;
        seconds.Minimum = 5; seconds.Maximum = 3600; seconds.Value = 30;

        toggle.Location = new Point(12, 48);
        toggle.Size = new Size(233, 36);
        toggle.Click += (s, e) => SetRunning(!timer.Enabled);

        status.Location = new Point(12, 95);
        status.AutoSize = true;
        status.ForeColor = Color.DimGray;

        Controls.AddRange(new Control[] { lbl, seconds, toggle, status });

        // Minimize to the tray so it stays out of the way.
        tray.Icon = SystemIcons.Application;
        tray.Text = "Mouse Jiggler";
        tray.DoubleClick += (s, e) => { Show(); WindowState = FormWindowState.Normal; tray.Visible = false; };
        Resize += (s, e) => { if (WindowState == FormWindowState.Minimized) { Hide(); tray.Visible = true; } };
        FormClosing += (s, e) => { tray.Visible = false; SetThreadExecutionState(ES_CONTINUOUS); };

        timer.Interval = 1000;
        timer.Tick += (s, e) => Tick();
        SetRunning(true);
    }

    void SetRunning(bool on)
    {
        timer.Enabled = on;
        // Also tell Windows not to sleep or turn off the screen while running.
        SetThreadExecutionState(on ? ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED : ES_CONTINUOUS);
        toggle.Text = on ? "ON  -  click to stop" : "OFF  -  click to start";
        toggle.BackColor = on ? Color.FromArgb(200, 240, 200) : Color.FromArgb(240, 210, 210);
        status.Text = on ? "Running. Jiggles so far: " + jiggles : "Stopped.";
    }

    void Tick()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
        GetLastInputInfo(ref info);
        uint idleMs = (uint)Environment.TickCount - info.dwTime;
        if (idleMs < (uint)seconds.Value * 1000) return;

        // Move 1px right, then 1px back. Invisible in practice.
        var size = Marshal.SizeOf(typeof(INPUT));
        SendInput(1, new[] { new INPUT { mi = new MOUSEINPUT { dx = 1, dwFlags = MOUSEEVENTF_MOVE } } }, size);
        SendInput(1, new[] { new INPUT { mi = new MOUSEINPUT { dx = -1, dwFlags = MOUSEEVENTF_MOVE } } }, size);
        jiggles++;
        status.Text = "Running. Jiggles so far: " + jiggles;
    }

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.Run(new Jiggler());
    }
}
