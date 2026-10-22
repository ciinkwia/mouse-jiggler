// Mouse Jiggler - keeps the PC awake and "active" by moving the mouse like a
// person would when you've been idle: random waits, random spots, curved glides.
// Tiny window, no install, no internet.
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
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
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint flags);

    const uint ES_CONTINUOUS = 0x80000000, ES_SYSTEM_REQUIRED = 0x1, ES_DISPLAY_REQUIRED = 0x2;
    const uint MOUSEEVENTF_MOVE = 0x1, MOUSEEVENTF_VIRTUALDESK = 0x4000, MOUSEEVENTF_ABSOLUTE = 0x8000;
    const byte VK_F15 = 0x7E; // a key no keyboard has; apps ignore it but it counts as activity
    const uint KEYEVENTF_KEYUP = 0x2;

    System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    Button toggle = new Button();
    Label status = new Label();
    NumericUpDown minWait = new NumericUpDown(), maxWait = new NumericUpDown();
    NotifyIcon tray = new NotifyIcon();
    Random rng = new Random();
    volatile bool moving;
    int nextWaitMs, moves;

    Jiggler()
    {
        Text = "Mouse Jiggler";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        ClientSize = new Size(290, 130);
        Font = new Font("Segoe UI", 10);
        Icon = SystemIcons.Application;

        var lbl = new Label { Text = "Move every", Location = new Point(12, 15), AutoSize = true };
        var to = new Label { Text = "to", Location = new Point(146, 15), AutoSize = true };
        var sec = new Label { Text = "sec", Location = new Point(242, 15), AutoSize = true };
        minWait.Location = new Point(90, 12); minWait.Width = 52;
        maxWait.Location = new Point(170, 12); maxWait.Width = 68;
        minWait.Minimum = 3; minWait.Maximum = 3600; minWait.Value = 8;
        maxWait.Minimum = 3; maxWait.Maximum = 3600; maxWait.Value = 45;
        minWait.ValueChanged += (s, e) => { if (maxWait.Value < minWait.Value) maxWait.Value = minWait.Value; RollWait(); };
        maxWait.ValueChanged += (s, e) => { if (minWait.Value > maxWait.Value) minWait.Value = maxWait.Value; RollWait(); };

        toggle.Location = new Point(12, 48);
        toggle.Size = new Size(266, 36);
        toggle.Click += (s, e) => SetRunning(!timer.Enabled);

        status.Location = new Point(12, 95);
        status.AutoSize = true;
        status.ForeColor = Color.DimGray;

        Controls.AddRange(new Control[] { lbl, minWait, to, maxWait, sec, toggle, status });

        // Minimize to the tray so it stays out of the way.
        tray.Icon = SystemIcons.Application;
        tray.Text = "Mouse Jiggler";
        tray.DoubleClick += (s, e) => { Show(); WindowState = FormWindowState.Normal; tray.Visible = false; };
        Resize += (s, e) => { if (WindowState == FormWindowState.Minimized) { Hide(); tray.Visible = true; } };
        FormClosing += (s, e) => { tray.Visible = false; SetThreadExecutionState(ES_CONTINUOUS); };

        timer.Interval = 500;
        timer.Tick += (s, e) => Tick();
        RollWait();
        SetRunning(true);
    }

    void RollWait()
    {
        nextWaitMs = rng.Next((int)minWait.Value * 1000, (int)maxWait.Value * 1000 + 1);
    }

    void SetRunning(bool on)
    {
        timer.Enabled = on;
        // Also tell Windows not to sleep or turn off the screen while running.
        SetThreadExecutionState(on ? ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED : ES_CONTINUOUS);
        toggle.Text = on ? "ON  -  click to stop" : "OFF  -  click to start";
        toggle.BackColor = on ? Color.FromArgb(200, 240, 200) : Color.FromArgb(240, 210, 210);
        status.Text = on ? "Running. Moves so far: " + moves : "Stopped.";
    }

    void Tick()
    {
        if (moving) return;
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
        GetLastInputInfo(ref info);
        uint idleMs = (uint)Environment.TickCount - info.dwTime;
        if (idleMs < nextWaitMs) return;

        moving = true;
        RollWait(); // next wait is random again
        new Thread(() =>
        {
            try { MoveLikeAPerson(); }
            finally
            {
                moving = false;
                moves++;
                BeginInvoke((Action)(() => { if (timer.Enabled) status.Text = "Running. Moves so far: " + moves; }));
            }
        }) { IsBackground = true }.Start();
    }

    // One "visit": a glide to a random nearby spot, sometimes a few in a row,
    // sometimes overshooting a bit and correcting, like a real hand.
    void MoveLikeAPerson()
    {
        int hops = rng.NextDouble() < 0.3 ? rng.Next(2, 4) : 1;
        for (int h = 0; h < hops; h++)
        {
            var from = Cursor.Position;
            var area = Screen.FromPoint(from).WorkingArea; // stay on this screen, off the taskbar
            area.Inflate(-40, -40);

            double angle = rng.NextDouble() * Math.PI * 2;
            double dist = 40 + rng.NextDouble() * 260;
            var target = Clamp(new Point(from.X + (int)(Math.Cos(angle) * dist), from.Y + (int)(Math.Sin(angle) * dist)), area);

            if (rng.NextDouble() < 0.2)
            {
                // Overshoot past the target, then settle back onto it.
                var over = Clamp(new Point(target.X + (target.X - from.X) / 8, target.Y + (target.Y - from.Y) / 8), area);
                if (!Glide(from, over)) return;
                Thread.Sleep(rng.Next(60, 180));
                if (!Glide(over, target)) return;
            }
            else if (!Glide(from, target)) return;

            Thread.Sleep(rng.Next(150, 900)); // pause like you're reading something
        }

        // Also tap F15. Teams, Slack and screen-lock timers all see this as you being active.
        keybd_event(VK_F15, 0, 0, UIntPtr.Zero);
        keybd_event(VK_F15, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    // Glide along a gentle curve, slow at the start and end, with a tiny wobble.
    // Returns false (and stops) if the real user grabs the mouse.
    bool Glide(Point a, Point b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
        // Bend the path to one side by a random amount.
        double bend = (rng.NextDouble() - 0.5) * len * 0.5;
        double nx = -dy / len, ny = dx / len;
        var c1 = new PointF((float)(a.X + dx * 0.3 + nx * bend), (float)(a.Y + dy * 0.3 + ny * bend));
        var c2 = new PointF((float)(a.X + dx * 0.7 + nx * bend * 0.6), (float)(a.Y + dy * 0.7 + ny * bend * 0.6));

        int durationMs = (int)(200 + len * (1.2 + rng.NextDouble() * 1.5));
        var last = a;
        var start = Environment.TickCount;
        while (true)
        {
            double t = Math.Min(1.0, (Environment.TickCount - start) / (double)durationMs);
            double e = t * t * t * (10 - 15 * t + 6 * t * t); // smooth start and stop
            double u = 1 - e;
            double x = u * u * u * a.X + 3 * u * u * e * c1.X + 3 * u * e * e * c2.X + e * e * e * b.X;
            double y = u * u * u * a.Y + 3 * u * u * e * c1.Y + 3 * u * e * e * c2.Y + e * e * e * b.Y;
            if (t < 1) { x += rng.Next(-1, 2); y += rng.Next(-1, 2); }

            var now = Cursor.Position;
            if (Math.Abs(now.X - last.X) > 4 || Math.Abs(now.Y - last.Y) > 4) return false; // user took over

            last = new Point((int)Math.Round(x), (int)Math.Round(y));
            MoveTo(last);
            if (t >= 1) return true;
            Thread.Sleep(rng.Next(7, 16));
        }
    }

    static Point Clamp(Point p, Rectangle r)
    {
        return new Point(Math.Min(Math.Max(p.X, r.Left), r.Right), Math.Min(Math.Max(p.Y, r.Top), r.Bottom));
    }

    // Move to an exact spot. A tiny relative move can get swallowed by pointer
    // acceleration or high-DPI screens (Surface), so use absolute coordinates.
    static void MoveTo(Point p)
    {
        var screen = SystemInformation.VirtualScreen;
        var move = new MOUSEINPUT {
            dx = (int)Math.Round((p.X - screen.Left) * 65535.0 / Math.Max(1, screen.Width - 1)),
            dy = (int)Math.Round((p.Y - screen.Top) * 65535.0 / Math.Max(1, screen.Height - 1)),
            dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK
        };
        SendInput(1, new[] { new INPUT { mi = move } }, Marshal.SizeOf(typeof(INPUT)));
    }

    [STAThread]
    static void Main()
    {
        SetProcessDPIAware(); // use real pixels on scaled screens
        Application.EnableVisualStyles();
        Application.Run(new Jiggler());
    }
}
