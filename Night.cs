// Ночник. Тёплый свет зафиксирован (02.11.2026): R 0.72, G 0.60, B 0.50.
// Тусклость до этой точки ослабляет тот же цвет. Дальше драйвер не принимает
// канал ниже ~0.50, поэтому доп. темноту даёт прозрачная чёрная плёнка.
// Клики сквозь плёнку проходят. Подсветку монитора не трогает. Без админа.
// На выходе гамма обычная.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Ночник")]
[assembly: AssemblyProduct("Ночник")]
[assembly: AssemblyVersion("1.1.0.0")]

namespace Night
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                Run(args);
            }
            catch (Exception ex)
            {
                try
                {
                    File.WriteAllText(Path.Combine(AppDir(), "crash.txt"), ex.ToString(), new UTF8Encoding(false));
                }
                catch { }
                throw;
            }
        }

        static void Run(string[] args)
        {
            if (Has(args, "--check"))
            {
                Environment.Exit(Check());
            }
            if (Has(args, "--check-ui"))
            {
                Environment.Exit(CheckUi());
            }
            if (Has(args, "--reset"))
            {
                SignalOrIdentity(@"Local\Night.Quit");
                return;
            }

            bool background = Has(args, "--background");
            Mutex mutex = new Mutex(false, @"Local\Night.Single");
            bool owned;
            try { owned = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { owned = true; }
            if (!owned)
            {
                Signal(@"Local\Night.Show");
                mutex.Dispose();
                return;
            }

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate (object sender, ThreadExceptionEventArgs e) { Note(e.Exception); };
            AppDomain.CurrentDomain.UnhandledException += delegate (object sender, UnhandledExceptionEventArgs e)
            {
                Note(e.ExceptionObject as Exception);
            };
            AppDomain.CurrentDomain.ProcessExit += delegate { Mark("process-exit"); };
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            EventWaitHandle show = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Night.Show");
            EventWaitHandle quit = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Night.Quit");
            MainForm form = new MainForm(Config.Load(), !background);
            form.Start();

            Thread waiter = new Thread(delegate ()
            {
                WaitHandle[] both = new WaitHandle[] { show, quit };
                while (true)
                {
                    int which;
                    try { which = WaitHandle.WaitAny(both); }
                    catch { break; }
                    try
                    {
                        if (which == 1) form.BeginInvoke(new Action(form.ExitClean));
                        else form.BeginInvoke(new Action(form.ShowFromTray));
                    }
                    catch { break; }
                    if (which == 1) break;
                }
            });
            waiter.IsBackground = true;
            waiter.Start();

            try
            {
                Application.Run(form);
            }
            finally
            {
                try { Gamma.Identity(); } catch { }
                try { mutex.ReleaseMutex(); } catch { }
                mutex.Dispose();
            }
        }

        static int CheckUi()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (MainForm form = new MainForm(new Config(), false, false))
            {
                IntPtr handle = form.Handle;
                File.WriteAllText(
                    Path.Combine(AppDir(), "ui-check.txt"),
                    "ui-ok " + handle.ToInt64() + " " + form.ClientSize.Width + "x" + form.ClientSize.Height,
                    new UTF8Encoding(false));
            }
            return 0;
        }

        static void SignalOrIdentity(string name)
        {
            try { EventWaitHandle.OpenExisting(name).Set(); }
            catch (WaitHandleCannotBeOpenedException) { Gamma.Identity(); }
        }

        static void Signal(string name)
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    EventWaitHandle.OpenExisting(name).Set();
                    return;
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                    Thread.Sleep(100);
                }
            }
        }

        static bool Has(string[] args, string name)
        {
            if (args == null) return false;
            foreach (string a in args)
            {
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public static string AppDir()
        {
            return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        }

        public static void Mark(string line)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Night");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "log.txt"), DateTime.Now.ToString("HH:mm:ss") + " " + line + "\r\n", new UTF8Encoding(false));
            }
            catch { }
        }

        public static void Note(Exception ex)
        {
            Mark(ex == null ? "unknown" : ex.ToString());
        }

        static int Check()
        {
            StringBuilder sb = new StringBuilder();
            int bad = 0;

            double strength;
            double veil;
            Look.Split(0, out strength, out veil);
            bad += Eq(sb, "off-s", strength, 0);
            bad += Eq(sb, "off-v", veil, 0);
            Look.Split(Look.Liked, out strength, out veil);
            bad += Eq(sb, "liked-s", strength, 1);
            bad += Eq(sb, "liked-v", veil, 0);
            Look.Split(35, out strength, out veil);
            bad += Eq(sb, "mid-s", strength, 0.5);
            bad += Eq(sb, "mid-v", veil, 0);
            Look.Split(85, out strength, out veil);
            bad += Eq(sb, "deep-s", strength, 1);
            bad += Eq(sb, "deep-v", veil, 0.25);
            Look.Split(100, out strength, out veil);
            bad += Eq(sb, "max-s", strength, 1);
            bad += Eq(sb, "max-v", veil, Look.MaxVeil);
            bad += Eq(sb, "ch-half", Look.Channel(0.5, Look.Red), 0.86);
            bad += Eq(sb, "ch-full", Look.Channel(1, Look.Red), Look.Red);

            ushort[] liked = Gamma.Ramp(1);
            ushort[] plain = Gamma.Ramp(0);
            ushort[] half = Gamma.Ramp(0.5);
            bad += One(sb, "r70", liked[128], 23685);
            bad += One(sb, "g70", liked[384], 19738);
            bad += One(sb, "b70", liked[640], 16448);
            bad += One(sb, "id", plain[128], 32896);
            bad += One(sb, "idg", plain[384], 32896);
            bad += One(sb, "idb", plain[640], 32896);
            bad += One(sb, "half", half[128], 28291);

            ushort[] again = Gamma.Ramp(1);
            bool same = liked.Length == again.Length;
            for (int i = 0; same && i < liked.Length; i++) if (liked[i] != again[i]) same = false;
            sb.Append(same ? "ok   " : "FAIL ").Append("stable").Append('\n');
            if (!same) bad++;

            bool mono = true;
            for (int ch = 0; ch < 3; ch++)
            {
                int prev = -1;
                for (int i = 0; i < 256; i++)
                {
                    int v = liked[ch * 256 + i];
                    if (v < prev) mono = false;
                    prev = v;
                }
            }
            sb.Append(mono ? "ok   " : "FAIL ").Append("mono").Append('\n');
            if (!mono) bad++;

            sb.Append("bad=").Append(bad).Append('\n');
            File.WriteAllText(Path.Combine(AppDir(), "check.txt"), sb.ToString(), new UTF8Encoding(false));
            return bad == 0 ? 0 : 1;
        }

        static int Eq(StringBuilder sb, string name, double got, double expect)
        {
            bool ok = Math.Abs(got - expect) < 0.001;
            sb.Append(ok ? "ok   " : "FAIL ").Append(name)
                .Append(" got=").Append(got.ToString("0.000"))
                .Append(" expect=").Append(expect.ToString("0.000")).Append('\n');
            return ok ? 0 : 1;
        }

        static int One(StringBuilder sb, string name, int got, int expect)
        {
            bool ok = got == expect;
            sb.Append(ok ? "ok   " : "FAIL ").Append(name)
                .Append(" got=").Append(got)
                .Append(" expect=").Append(expect).Append('\n');
            return ok ? 0 : 1;
        }
    }

    static class Look
    {
        public const double Red = 0.72;
        public const double Green = 0.60;
        public const double Blue = 0.50;
        public const int Liked = 70;
        public const double MaxVeil = 0.50;

        public static void Split(int dim, out double strength, out double veil)
        {
            if (dim < 0) dim = 0;
            if (dim > 100) dim = 100;
            if (dim <= Liked)
            {
                strength = dim / (double)Liked;
                veil = 0;
            }
            else
            {
                strength = 1;
                veil = (dim - Liked) / (double)(100 - Liked) * MaxVeil;
            }
        }

        public static double Channel(double strength, double night)
        {
            if (strength >= 1) return night;
            if (strength <= 0) return 1;
            return 1.0 - strength * (1.0 - night);
        }
    }

    sealed class Config
    {
        public int Dim = Look.Liked;
        public bool Autostart;

        public static string FilePath()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Night");
            return Path.Combine(dir, "config.txt");
        }

        public static Config Load()
        {
            Config c = new Config();
            string path = FilePath();
            if (!File.Exists(path)) return c;
            try
            {
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                foreach (string line in lines)
                {
                    int eq = line.IndexOf('=');
                    if (eq < 1) continue;
                    string k = line.Substring(0, eq).Trim();
                    string v = line.Substring(eq + 1).Trim();
                    if (k == "dim") c.Dim = Clamp(Parse(v, c.Dim), 0, 100);
                    else if (k == "autostart") c.Autostart = v == "1";
                }
            }
            catch { }
            return c;
        }

        public void Save()
        {
            string path = FilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            StringBuilder sb = new StringBuilder();
            sb.Append("dim=").Append(Dim).Append('\n');
            sb.Append("autostart=").Append(Autostart ? "1" : "0").Append('\n');
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
            File.Copy(tmp, path, true);
            try { File.Delete(tmp); } catch { }
        }

        static int Parse(string s, int fallback)
        {
            int n;
            if (int.TryParse(s, out n)) return n;
            return fallback;
        }

        static int Clamp(int v, int lo, int hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }
    }

    static class Autostart
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static void Set(bool on)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Key, true))
            {
                if (key == null) return;
                if (on)
                {
                    string exe = Assembly.GetExecutingAssembly().Location;
                    key.SetValue("Night", "\"" + exe + "\" --background");
                }
                else key.DeleteValue("Night", false);
            }
        }
    }

    static class Gamma
    {
        static readonly object Gate = new object();

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr CreateDC(string driver, string device, string output, IntPtr init);

        [DllImport("gdi32.dll")]
        static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll", SetLastError = true)]
        static extern bool SetDeviceGammaRamp(IntPtr hdc, IntPtr ramp);

        public static void Identity()
        {
            Apply(0);
        }

        public static bool Apply(double strength)
        {
            lock (Gate)
            {
                ushort[] ramp = Ramp(strength);
                Screen[] screens = Screen.AllScreens;
                if (screens == null || screens.Length == 0) return false;
                bool all = true;
                foreach (Screen s in screens)
                {
                    IntPtr dc = CreateDC(s.DeviceName, null, null, IntPtr.Zero);
                    if (dc == IntPtr.Zero) { all = false; continue; }
                    try
                    {
                        int err;
                        if (!Write(dc, ramp, out err)) all = false;
                    }
                    finally { DeleteDC(dc); }
                }
                return all;
            }
        }

        public static ushort[] Ramp(double strength)
        {
            ushort[] ramp = new ushort[768];
            Fill(ramp, 0, Look.Channel(strength, Look.Red));
            Fill(ramp, 256, Look.Channel(strength, Look.Green));
            Fill(ramp, 512, Look.Channel(strength, Look.Blue));
            return ramp;
        }

        static void Fill(ushort[] ramp, int offset, double mul)
        {
            if (mul < 0) mul = 0;
            if (mul > 1) mul = 1;
            int prev = 0;
            for (int i = 0; i < 256; i++)
            {
                int v = (int)Math.Round(i * 257.0 * mul);
                if (v < prev) v = prev;
                if (v > 65535) v = 65535;
                ramp[offset + i] = (ushort)v;
                prev = v;
            }
        }

        static bool Write(IntPtr dc, ushort[] ramp, out int err)
        {
            IntPtr mem = Marshal.AllocHGlobal(ramp.Length * 2);
            try
            {
                short[] bits = new short[ramp.Length];
                Buffer.BlockCopy(ramp, 0, bits, 0, ramp.Length * 2);
                Marshal.Copy(bits, 0, mem, bits.Length);
                bool ok = SetDeviceGammaRamp(dc, mem);
                err = Marshal.GetLastWin32Error();
                return ok;
            }
            finally { Marshal.FreeHGlobal(mem); }
        }
    }

    sealed class Veil : Form
    {
        const uint SWP_NOACTIVATE = 0x0010;
        const uint SWP_SHOWWINDOW = 0x0040;

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        public Veil()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Black;
            Opacity = 0.02;
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000080;
                cp.ExStyle |= 0x00000020;
                cp.ExStyle |= 0x08000000;
                cp.ExStyle |= 0x00000008;
                return cp;
            }
        }

        public void Place(Rectangle bounds, double opacity)
        {
            if (opacity < 0.02)
            {
                if (Visible) Hide();
                return;
            }
            if (opacity > Look.MaxVeil) opacity = Look.MaxVeil;
            Bounds = bounds;
            Opacity = opacity;
            if (!IsHandleCreated) CreateHandle();
            SetWindowPos(Handle, new IntPtr(-1), bounds.X, bounds.Y, bounds.Width, bounds.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }
    }

    sealed class DimBar : Control
    {
        int _value = Look.Liked;
        bool _drag;

        public event EventHandler ValueChanged;

        public int Value
        {
            get { return _value; }
            set
            {
                int v = value;
                if (v < 0) v = 0;
                if (v > 100) v = 100;
                if (v == _value) return;
                _value = v;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        public DimBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 36;
            TabStop = true;
            Cursor = Cursors.Hand;
        }

        public void SetQuiet(int value)
        {
            int v = value;
            if (v < 0) v = 0;
            if (v > 100) v = 100;
            _value = v;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color back = Parent != null ? Parent.BackColor : Color.FromArgb(28, 28, 30);
            g.Clear(back);
            int pad = 10;
            int cy = Height / 2;
            int x1 = pad;
            int x2 = Width - pad;
            if (x2 < x1 + 20) x2 = x1 + 20;
            using (SolidBrush track = new SolidBrush(Color.FromArgb(58, 58, 62)))
                g.FillRectangle(track, x1, cy - 2, x2 - x1, 4);
            float tx = x1 + (x2 - x1) * (_value / 100f);
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(232, 176, 112)))
            {
                g.FillRectangle(fill, x1, cy - 2, Math.Max(1f, tx - x1), 4);
                g.FillEllipse(fill, tx - 7f, cy - 7f, 14f, 14f);
            }
            float mx = x1 + (x2 - x1) * (Look.Liked / 100f);
            using (Pen mark = new Pen(Color.FromArgb(220, 220, 224), 1.5f))
                g.DrawLine(mark, mx, cy - 11, mx, cy - 6);
        }

        int FromX(int x)
        {
            int pad = 10;
            int x1 = pad;
            int x2 = Width - pad;
            if (x2 <= x1) return 0;
            double t = (x - x1) / (double)(x2 - x1);
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            return (int)Math.Round(t * 100.0);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Focus();
            _drag = true;
            Capture = true;
            Value = FromX(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_drag) Value = FromX(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _drag = false;
            Capture = false;
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left) Value = Look.Liked;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Value = _value + (e.Delta > 0 ? 1 : -1);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys code = keyData & Keys.KeyCode;
            if (code == Keys.Left || code == Keys.Right) return true;
            return base.IsInputKey(keyData);
        }
    }

    sealed class MainForm : Form
    {
        static readonly Color Bg = Color.FromArgb(28, 28, 30);
        static readonly Color Fg = Color.FromArgb(232, 232, 234);
        static readonly Color Muted = Color.FromArgb(150, 150, 156);
        static readonly Color Accent = Color.FromArgb(232, 176, 112);
        static readonly Color Warn = Color.FromArgb(255, 170, 130);

        readonly Config _config;
        readonly bool _live;
        bool _allowShow;
        bool _loading = true;
        bool _quitting;
        bool _started;
        bool _dirty;
        bool _inTick;
        bool _appliedOk = true;
        bool _gammaOk = true;
        int _dim = Look.Liked;
        int _shownDim = int.MinValue;
        volatile int _force;
        DateTime _lastSave = DateTime.MinValue;
        DateTime _appliedAt = DateTime.MinValue;
        DateTime _pauseUntil = DateTime.MinValue;

        NotifyIcon _tray;
        Icon _icon;
        System.Windows.Forms.Timer _timer;
        DimBar _bar;
        Label _value;
        Label _status;
        CheckBox _auto;
        LinkLabel _pause;
        ToolStripMenuItem _miLevel;
        ToolStripMenuItem _miPause;
        ToolStripMenuItem _miStart;
        List<Veil> _veils = new List<Veil>();
        string _veilKey = "";

        public MainForm(Config config, bool openWindow)
            : this(config, openWindow, true)
        {
        }

        public MainForm(Config config, bool openWindow, bool live)
        {
            _config = config;
            _live = live;
            _allowShow = openWindow;
            _dim = config.Dim;

            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            Font = new Font("Segoe UI", 10f);
            Text = "Ночник";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(420, 312);
            BackColor = Bg;
            ForeColor = Fg;
            ShowInTaskbar = openWindow;
            KeyPreview = true;

            Build();
            if (live)
            {
                BuildTray();
                Icon = _icon;
            }
            _loading = false;
        }

        public void Start()
        {
            _started = true;
            if (_config.Autostart) Autostart.Set(true);
            SystemEvents.DisplaySettingsChanged += OnDisplay;
            SystemEvents.SessionSwitch += OnSession;
            SystemEvents.PowerModeChanged += OnPower;
            SystemEvents.SessionEnding += OnEnding;
            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 400;
            _timer.Tick += OnTick;
            _timer.Start();
            ApplyNow(true);
            RefreshTexts();
        }

        protected override void SetVisibleCore(bool value)
        {
            if (!_allowShow) value = false;
            base.SetVisibleCore(value);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            Program.Mark("closing " + e.CloseReason);
            if (!_quitting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            if (_started) try { Gamma.Identity(); } catch { }
            HideVeils();
            base.OnFormClosing(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized) HideToTray();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys code = keyData & Keys.KeyCode;
            int step = (keyData & Keys.Shift) != 0 ? 5 : 1;
            if (code == Keys.Left) { Nudge(-step); return true; }
            if (code == Keys.Right) { Nudge(step); return true; }
            if (code == Keys.Home) { SetDim(0); return true; }
            if (code == Keys.End) { SetDim(100); return true; }
            if (keyData == Keys.Escape)
            {
                HideToTray();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            Nudge(e.Delta > 0 ? 1 : -1);
            base.OnMouseWheel(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_timer != null) _timer.Stop();
                try { SystemEvents.DisplaySettingsChanged -= OnDisplay; } catch { }
                try { SystemEvents.SessionSwitch -= OnSession; } catch { }
                try { SystemEvents.PowerModeChanged -= OnPower; } catch { }
                try { SystemEvents.SessionEnding -= OnEnding; } catch { }
                if (_started) try { Gamma.Identity(); } catch { }
                HideVeils();
                if (_tray != null)
                {
                    _tray.Visible = false;
                    _tray.Dispose();
                    _tray = null;
                    _icon = null;
                }
            }
            base.Dispose(disposing);
        }

        public void ShowFromTray()
        {
            if (_quitting) return;
            _allowShow = true;
            ShowInTaskbar = true;
            WindowState = FormWindowState.Normal;
            TopMost = true;
            Show();
            Activate();
            RaiseAboveVeil();
        }

        public void ExitClean()
        {
            Program.Mark("exit-clean");
            if (_quitting) return;
            _quitting = true;
            SaveNow();
            try { Gamma.Identity(); } catch { }
            HideVeils();
            if (_tray != null) _tray.Visible = false;
            Application.Exit();
        }

        void OnDisplay(object sender, EventArgs e) { _force = 1; }
        void OnSession(object sender, SessionSwitchEventArgs e) { _force = 1; }
        void OnPower(object sender, PowerModeChangedEventArgs e) { _force = 1; }
        void OnEnding(object sender, SessionEndingEventArgs e)
        {
            try { Gamma.Identity(); } catch { }
        }

        void Build()
        {
            L("Ночник", 20, 14, 380, 28, Fg, 16f, true);
            L("Тёплый свет не трогаю. Крутится тусклость.", 20, 44, 380, 22, Muted, 9.5f, false);

            L("Тусклость", 20, 78, 180, 24, Fg, 0, false);
            _value = L(Look.Liked.ToString(), 200, 74, 200, 28, Accent, 14f, true);
            _value.TextAlign = ContentAlignment.MiddleRight;

            _bar = new DimBar();
            _bar.Left = 10;
            _bar.Top = 108;
            _bar.Width = 400;
            _bar.Height = 36;
            _bar.BackColor = Bg;
            _bar.SetQuiet(_dim);
            _bar.ValueChanged += OnDim;
            Controls.Add(_bar);

            L("светлее", 20, 146, 160, 18, Muted, 8.5f, false);
            Label darker = L("темнее", 240, 146, 160, 18, Muted, 8.5f, false);
            darker.TextAlign = ContentAlignment.MiddleRight;

            _status = L("", 20, 172, 380, 44, Muted, 9.5f, false);

            _pause = Link("На час обычный экран", 20, 222, 280);
            _pause.Click += delegate { TogglePause(); };

            _auto = new CheckBox();
            _auto.Text = "Запускать вместе с Windows";
            _auto.Left = 20;
            _auto.Top = 250;
            _auto.AutoSize = true;
            _auto.ForeColor = Fg;
            _auto.BackColor = Bg;
            _auto.FlatStyle = FlatStyle.Flat;
            _auto.FlatAppearance.BorderSize = 0;
            _auto.FlatAppearance.CheckedBackColor = Bg;
            _auto.FlatAppearance.MouseOverBackColor = Color.FromArgb(40, 40, 44);
            _auto.Checked = _config.Autostart;
            _auto.CheckedChanged += OnAuto;
            Controls.Add(_auto);

            L("Крестик прячет в трей.", 20, 282, 220, 22, Muted, 9f, false);
            LinkLabel exit = Link("Выйти", 320, 280, 80);
            exit.TextAlign = ContentAlignment.MiddleRight;
            exit.Click += delegate { ExitClean(); };
        }

        void BuildTray()
        {
            _icon = MakeIcon();
            _tray = new NotifyIcon();
            _tray.Icon = _icon;
            _tray.Text = "Ночник";
            _tray.Visible = true;
            _tray.MouseClick += delegate (object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) ShowFromTray();
            };

            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem open = new ToolStripMenuItem("Открыть");
            open.Click += delegate { ShowFromTray(); };
            _miLevel = new ToolStripMenuItem("Тусклость");
            _miLevel.Enabled = false;
            ToolStripMenuItem lighter = new ToolStripMenuItem("Светлее");
            ToolStripMenuItem darker = new ToolStripMenuItem("Темнее");
            ToolStripMenuItem liked = new ToolStripMenuItem("Как было");
            ToolStripMenuItem off = new ToolStripMenuItem("Выключить");
            lighter.Click += delegate { Nudge(-5); };
            darker.Click += delegate { Nudge(5); };
            liked.Click += delegate { SetDim(Look.Liked); };
            off.Click += delegate { SetDim(0); };
            _miPause = new ToolStripMenuItem("На час обычный экран");
            _miPause.Click += delegate { TogglePause(); };
            _miStart = new ToolStripMenuItem("Запускать вместе с Windows");
            _miStart.Click += delegate { _auto.Checked = !_auto.Checked; };
            ToolStripMenuItem exit = new ToolStripMenuItem("Выйти");
            exit.Click += delegate { ExitClean(); };

            menu.Items.Add(open);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_miLevel);
            menu.Items.Add(lighter);
            menu.Items.Add(darker);
            menu.Items.Add(liked);
            menu.Items.Add(off);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_miPause);
            menu.Items.Add(_miStart);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exit);
            _tray.ContextMenuStrip = menu;
        }

        Label L(string text, int x, int y, int w, int h, Color color, float size, bool bold)
        {
            Label l = new Label();
            l.Text = text;
            l.Left = x;
            l.Top = y;
            l.Width = w;
            l.Height = h;
            l.ForeColor = color;
            l.BackColor = Bg;
            l.AutoSize = false;
            if (size > 0 || bold)
            {
                float sz = size > 0 ? size : Font.Size;
                l.Font = new Font(Font.FontFamily, sz, bold ? FontStyle.Bold : FontStyle.Regular);
            }
            Controls.Add(l);
            return l;
        }

        LinkLabel Link(string text, int x, int y, int w)
        {
            LinkLabel l = new LinkLabel();
            l.Text = text;
            l.Left = x;
            l.Top = y;
            l.Width = w;
            l.Height = 22;
            l.BackColor = Bg;
            l.LinkColor = Accent;
            l.ActiveLinkColor = Color.White;
            l.VisitedLinkColor = Accent;
            l.LinkBehavior = LinkBehavior.HoverUnderline;
            Controls.Add(l);
            return l;
        }

        void OnDim(object sender, EventArgs e)
        {
            if (_loading) return;
            _dim = _bar.Value;
            _pauseUntil = DateTime.MinValue;
            _dirty = true;
            ApplyNow(true);
            RefreshTexts();
        }

        void OnAuto(object sender, EventArgs e)
        {
            if (_loading) return;
            _config.Autostart = _auto.Checked;
            try { Autostart.Set(_auto.Checked); } catch { }
            _dirty = true;
            RefreshTexts();
        }

        void Nudge(int delta)
        {
            SetDim(_dim + delta);
        }

        void SetDim(int dim)
        {
            if (dim < 0) dim = 0;
            if (dim > 100) dim = 100;
            bool paused = _pauseUntil > DateTime.Now;
            _pauseUntil = DateTime.MinValue;
            if (_bar.Value == dim)
            {
                if (!paused) return;
                _dirty = true;
                ApplyNow(true);
                RefreshTexts();
                return;
            }
            _bar.Value = dim;
        }

        void TogglePause()
        {
            if (_pauseUntil > DateTime.Now) _pauseUntil = DateTime.MinValue;
            else _pauseUntil = DateTime.Now.AddHours(1);
            ApplyNow(true);
            RefreshTexts();
        }

        void OnTick(object sender, EventArgs e)
        {
            if (_inTick || _quitting) return;
            _inTick = true;
            try
            {
                SaveIfDirty();
                bool force = _force != 0;
                if (force) _force = 0;
                ApplyNow(force);
                RefreshTexts();
            }
            catch (Exception ex)
            {
                SetStatus("Сбой: " + ex.Message, Warn);
            }
            finally { _inTick = false; }
        }

        void SaveIfDirty()
        {
            if (!_dirty) return;
            if ((DateTime.Now - _lastSave).TotalMilliseconds < 350) return;
            SaveNow();
        }

        void SaveNow()
        {
            _config.Dim = _dim;
            _config.Autostart = _auto.Checked;
            try { _config.Save(); } catch { }
            _dirty = false;
            _lastSave = DateTime.Now;
        }

        void HideToTray()
        {
            SaveNow();
            _allowShow = false;
            ShowInTaskbar = false;
            TopMost = false;
            Hide();
        }

        int EffectiveDim()
        {
            if (_pauseUntil > DateTime.Now) return 0;
            return _dim;
        }

        void ApplyNow(bool force)
        {
            if (_quitting) return;
            int dim = EffectiveDim();
            if (!force && dim == _shownDim && _appliedOk) return;

            double strength;
            double veil;
            Look.Split(dim, out strength, out veil);
            bool ok = true;
            try { ok = Gamma.Apply(strength); }
            catch { ok = false; }

            _shownDim = dim;
            _appliedAt = DateTime.Now;
            _gammaOk = ok;
            _appliedOk = ok || dim == 0;

            if (dim == 0)
            {
                HideVeils();
                return;
            }
            if (!ok)
            {
                try { Gamma.Identity(); } catch { }
                Cover(dim / 100.0 * Look.MaxVeil);
                _appliedOk = true;
                return;
            }
            if (veil >= 0.02) Cover(veil);
            else HideVeils();
        }

        void Cover(double opacity)
        {
            Screen[] screens = Screen.AllScreens;
            StringBuilder key = new StringBuilder();
            key.Append(opacity.ToString("0.00")).Append('|');
            int n = 0;
            foreach (Screen s in screens)
            {
                if (s.Bounds.Width < 10) continue;
                key.Append(s.Bounds.X).Append(',').Append(s.Bounds.Y).Append(',').Append(s.Bounds.Width).Append(',').Append(s.Bounds.Height).Append(';');
                n++;
            }
            if (n == 0)
            {
                HideVeils();
                return;
            }
            string text = key.ToString();
            if (text != _veilKey || _veils.Count != n)
            {
                HideVeils();
                _veilKey = text;
                foreach (Screen s in screens)
                {
                    if (s.Bounds.Width < 10) continue;
                    Veil v = new Veil();
                    _veils.Add(v);
                    v.Place(s.Bounds, opacity);
                }
            }
            else
            {
                int i = 0;
                foreach (Screen s in screens)
                {
                    if (s.Bounds.Width < 10) continue;
                    if (i < _veils.Count) _veils[i].Place(s.Bounds, opacity);
                    i++;
                }
            }
            RaiseAboveVeil();
        }

        void RaiseAboveVeil()
        {
            if (!Visible) return;
            TopMost = true;
            BringToFront();
        }

        void HideVeils()
        {
            foreach (Veil v in _veils)
            {
                try { v.Hide(); v.Dispose(); } catch { }
            }
            _veils.Clear();
            _veilKey = "";
        }

        void RefreshTexts()
        {
            if (_value.Text != _dim.ToString()) _value.Text = _dim.ToString();

            bool paused = _pauseUntil > DateTime.Now;
            string text;
            Color col = Muted;
            if (paused)
            {
                text = "До " + _pauseUntil.ToString("HH:mm") + " экран обычный.";
            }
            else if (!_gammaOk && _dim > 0)
            {
                text = "Драйвер не взял гамму, держу только плёнку. Тепла нет.";
                col = Warn;
            }
            else if (_dim == 0)
            {
                text = "Выключено. Экран обычный.";
            }
            else if (_dim == Look.Liked)
            {
                text = "Тот тёплый, который зашёл.";
                col = Accent;
            }
            else if (_dim > Look.Liked)
            {
                text = "Темнее того самого. Цвет тот же, сверху плёнка.";
                col = Accent;
            }
            else
            {
                text = "Светлее. Тот же цвет, слабее.";
                col = Accent;
            }
            SetStatus(text, col);

            string pauseText = paused ? "Отменить этот час" : "На час обычный экран";
            if (_pause.Text != pauseText) _pause.Text = pauseText;
            if (_miPause != null)
            {
                _miPause.Text = pauseText;
                _miPause.Checked = paused;
                _miStart.Checked = _auto.Checked;
                string level = "Тусклость: " + _dim;
                if (_miLevel.Text != level) _miLevel.Text = level;
            }

            string tip = paused ? "Ночник — обычный до " + _pauseUntil.ToString("HH:mm") : "Ночник — тусклость " + _dim;
            if (tip.Length > 63) tip = tip.Substring(0, 63);
            if (_tray != null && _tray.Text != tip) _tray.Text = tip;
        }

        void SetStatus(string text, Color color)
        {
            if (_status.Text != text) _status.Text = text;
            if (_status.ForeColor != color) _status.ForeColor = color;
        }

        static Bitmap _iconBmp;

        static Icon MakeIcon()
        {
            _iconBmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(_iconBmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(255, 176, 96)))
                using (GraphicsPath path = new GraphicsPath(FillMode.Alternate))
                {
                    path.AddEllipse(6, 5, 20, 22);
                    path.AddEllipse(14, 3, 18, 22);
                    g.FillPath(brush, path);
                }
            }
            return Icon.FromHandle(_iconBmp.GetHicon());
        }
    }
}
