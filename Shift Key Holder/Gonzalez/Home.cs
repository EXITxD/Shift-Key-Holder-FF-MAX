using GonzalezShiftHolder.Properties;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace GonzalezShiftHolder
{
    public partial class Home : Form
    {
        private string streamMessageEnabled = "Stream Mode Enabled!";
        private string streamMessageDisabled = "Stream Mode Disabled!";
        private bool isStreamModeEnabled = false;
        private bool isenable = false;
        private const uint WDA_NONE = 0x00000000;
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;
        private const uint WS_EX_NOACTIVATE = 0x08000000;
        private static bool Streaming;
        [DllImport("user32.dll")]
        public static extern uint SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, uint dwNewLong);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowLong(IntPtr hWnd, int nIndex);
        private const int GWL_EXSTYLE = -20;
        private const uint WS_EX_TOOLWINDOW = 0x00000080;
        [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr interception_create_context();
        [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void interception_destroy_context(IntPtr context);
        [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int interception_wait(IntPtr context);
        [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int interception_is_keyboard(int device);
        [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int interception_receive(IntPtr context, int device, ref KeyStroke stroke, int nstroke);
        [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int interception_send(IntPtr context, int device, ref KeyStroke stroke, int nstroke);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int InterceptionPredicate(int device);
        [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void interception_set_filter(IntPtr context, InterceptionPredicate predicate, ushort filter);
        [StructLayout(LayoutKind.Sequential)]
        private struct KeyStroke
        {
            public ushort code;
            public ushort state;
            public uint information;
        }
        private const ushort KEY_UP = 0x01;
        private static bool IsKeyDown(ushort state) { return (state & KEY_UP) == 0; }
        private const ushort SC_LSHIFT = 0x2A;
        private const ushort SC_RSHIFT = 0x36;
        private const ushort FILTER_KEY_DOWN = 0x0001;
        private const ushort FILTER_KEY_UP = 0x0002;
        private static readonly InterceptionPredicate KeyboardPredicate = dev => interception_is_keyboard(dev);
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT Point);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        private const uint GA_ROOT = 2;
        public string[] TargetProcNames { get; set; } = new[] { "HD-Player" };
        public bool IsRunning { get { return _running; } }
        public bool ShiftHeld { get { return _shiftHeld; } }
        public event Action<bool> RunningChanged;
        public event Action<bool> ShiftHeldChanged;
        public event Action<string> Info;
        public event Action<string> Error;
        private volatile bool _running = false;
        private volatile bool _shiftHeld = false;
        private IntPtr _ctx = IntPtr.Zero;
        private int _lastShiftDevice = 0;
        private Thread _mainLoop;
        private readonly object _locker = new object();
        private const int WH_MOUSE_LL = 14;
        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONDOWN = 0x0201;
        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }
        [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
        private IntPtr _mouseHook = IntPtr.Zero;
        private LowLevelMouseProc _mouseProc;
        private System.Threading.Timer _boundaryTimer;
        private IntPtr _cachedHwnd = IntPtr.Zero;
        private uint _cachedPid = 0;
        private string _cachedName = null;
        public Home()
        {
            InitializeComponent();
            InitializePrompt();
            this.Text = "Gonzalez Shift Holder";
        }
        private void InitializePrompt()
        {
            AppendText("\r\n", Color.White);
            AppendText(" Waiting", Color.White);
            AppendText("\r\n", Color.White);
        }
        private void AppendText(string text, Color color)
        {
            prompt.SelectionStart = prompt.TextLength;
            prompt.SelectionLength = 0;
            prompt.SelectionColor = color;
            prompt.AppendText(text);
            prompt.SelectionColor = prompt.ForeColor;
        }
        public void Start()
        {
            lock (_locker)
            {
                if (_running) return;
                _ctx = interception_create_context();
                if (_ctx == IntPtr.Zero)
                {
                    RaiseError("create context fail");
                    return;
                }
                interception_set_filter(_ctx, KeyboardPredicate, (ushort)(FILTER_KEY_DOWN | FILTER_KEY_UP));
                _running = true;
                RaiseRunningChanged(true);
                _mainLoop = new Thread(MainLoop) { IsBackground = true, Name = "Interception-Main" };
                _mainLoop.Start();
                InstallMouseAndBoundary();
                RaiseInfo("started");
            }
        }
        public void Stop()
        {
            lock (_locker)
            {
                if (!_running) return;
                _running = false;
                UninstallMouseAndBoundary();
                if (_shiftHeld && _ctx != IntPtr.Zero)
                {
                    SendShiftUp(_ctx, _lastShiftDevice);
                    _shiftHeld = false;
                    RaiseShiftHeldChanged(false);
                }
                Thread.Sleep(25);
                if (_ctx != IntPtr.Zero)
                {
                    interception_destroy_context(_ctx);
                    _ctx = IntPtr.Zero;
                }
                RaiseRunningChanged(false);
                RaiseInfo("stopped");
            }
        }
        private void MainLoop()
        {
            if (isenable == true)
            {
                try
                {
                    while (_running)
                    {
                        int device = interception_wait(_ctx);
                        if (!_running || device <= 0) continue;
                        if (interception_is_keyboard(device) == 0) continue;
                        KeyStroke stroke = new KeyStroke();
                        int got = interception_receive(_ctx, device, ref stroke, 1);
                        if (got <= 0) continue;
                        bool isShiftKey = (stroke.code == SC_LSHIFT) || (stroke.code == SC_RSHIFT);
                        if (isShiftKey && IsCursorOverTargetCached())
                        {
                            if (IsKeyDown(stroke.state))
                            {
                                _shiftHeld = !_shiftHeld;
                                RaiseShiftHeldChanged(_shiftHeld);
                                if (_shiftHeld)
                                {
                                    SendShiftDown(_ctx, device);
                                    _lastShiftDevice = device;
                                    RaiseInfo("shift held");
                                }
                                else
                                {
                                    SendShiftUp(_ctx, _lastShiftDevice == 0 ? device : _lastShiftDevice);
                                    RaiseInfo("shift released");
                                }
                            }
                            continue;
                        }
                        interception_send(_ctx, device, ref stroke, 1);
                    }
                }
                catch (Exception)
                {
                    RaiseError("main loop");
                    Stop();
                }
            }
            else if (isenable == false)
            {
                return;
            }
        }
        private void InstallMouseAndBoundary()
        {
            if (_mouseHook == IntPtr.Zero)
            {
                _mouseProc = MouseHookProc;
                IntPtr hMod = GetModuleHandle(null);
                _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, hMod, 0);
                if (_mouseHook == IntPtr.Zero)
                    RaiseError("mouse hook");
            }
            if (_boundaryTimer == null)
            {
                _boundaryTimer = new System.Threading.Timer(_ =>
                {
                    if (!_running) return;
                    bool inside = IsCursorOverTargetCached();
                    if (!inside && _shiftHeld && _ctx != IntPtr.Zero)
                    {
                        SendShiftUp(_ctx, _lastShiftDevice);
                        _shiftHeld = false;
                        RaiseShiftHeldChanged(false);
                        RaiseInfo("auto release");
                    }
                }, null, 150, 150);
            }
        }
        private void UninstallMouseAndBoundary()
        {
            if (_boundaryTimer != null)
            {
                _boundaryTimer.Dispose();
                _boundaryTimer = null;
            }
            if (_mouseHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
                _mouseProc = null;
            }
        }
        private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _running)
            {
                int msg = wParam.ToInt32();
                if (msg == WM_MOUSEMOVE)
                {
                    if (_shiftHeld && _ctx != IntPtr.Zero && !IsCursorOverTargetCached())
                    {
                        SendShiftUp(_ctx, _lastShiftDevice);
                        _shiftHeld = false;
                        RaiseShiftHeldChanged(false);
                        RaiseInfo("move out");
                    }
                }
                else if (msg == WM_LBUTTONDOWN)
                {
                    if (_shiftHeld && _ctx != IntPtr.Zero && IsCursorOverTargetCached())
                    {
                        SendShiftUp(_ctx, _lastShiftDevice);
                        _shiftHeld = false;
                        RaiseShiftHeldChanged(false);
                        RaiseInfo("lmb release");
                    }
                }
            }
            return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }
        private bool IsCursorOverTargetCached()
        {
            POINT pt;
            if (!GetCursorPos(out pt)) return false;
            IntPtr hwnd = WindowFromPoint(pt);
            if (hwnd == IntPtr.Zero) return false;
            IntPtr top = GetAncestor(hwnd, GA_ROOT);
            if (top != IntPtr.Zero) hwnd = top;
            if (hwnd != _cachedHwnd)
            {
                _cachedHwnd = hwnd;
                _cachedPid = 0;
                _cachedName = null;
                uint pid;
                if (GetWindowThreadProcessId(hwnd, out pid) != 0 && pid != 0)
                {
                    _cachedPid = pid;
                    try
                    {
                        var proc = Process.GetProcessById((int)pid);
                        _cachedName = proc.ProcessName;
                    }
                    catch { }
                }
            }
            if (_cachedPid == 0 || string.IsNullOrEmpty(_cachedName)) return false;
            for (int i = 0; i < TargetProcNames.Length; i++)
            {
                if (_cachedName.IndexOf(TargetProcNames[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }
        private static void SendShiftDown(IntPtr ctx, int device)
        {
            var k = new KeyStroke { code = SC_LSHIFT, state = 0, information = 0 };
            interception_send(ctx, device, ref k, 1);
        }
        private static void SendShiftUp(IntPtr ctx, int device)
        {
            var up = new KeyStroke { code = SC_LSHIFT, state = KEY_UP, information = 0 };
            interception_send(ctx, device, ref up, 1);
            up.code = SC_RSHIFT;
            interception_send(ctx, device, ref up, 1);
        }
        private void btn_GonzalezShiftHolder_Click(object sender, EventArgs e)
        {
            if (isenable == true)
            {
                Start();
            }
            else if (isenable == false)
            {
                AppendText("First Enable Functions\r\n", Color.Red);
            }
        }
        private void guna2Button1_Click(object sender, EventArgs e)
        {
            if (isenable == true)
            {
                Stop();
            }
            else if (isenable == false)
            {
                AppendText("First Enable Functions\r\n", Color.Red);
            }
        }
        private void btn_stream_Click(object sender, EventArgs e)
        {
            prompt.Clear();
            AppendText("\r\n", Color.White);
            AppendText(" C:\\> ", Color.Lime);
            if (isStreamModeEnabled)
            {
                AppendText(streamMessageDisabled + "\r\n", Color.Green);
                base.ShowInTaskbar = true;
                Streaming = false;
                SetWindowDisplayAffinity(base.Handle, WDA_NONE);
                uint exStyle = GetWindowLong(base.Handle, GWL_EXSTYLE);
                SetWindowLong(base.Handle, GWL_EXSTYLE, exStyle & ~WS_EX_TOOLWINDOW & ~WS_EX_NOACTIVATE);
            }
            else
            {
                AppendText(streamMessageEnabled + "\r\n", Color.Green);
                base.ShowInTaskbar = false;
                Streaming = true;
                SetWindowDisplayAffinity(base.Handle, WDA_EXCLUDEFROMCAPTURE);
                uint exStyle = GetWindowLong(base.Handle, GWL_EXSTYLE);
                SetWindowLong(base.Handle, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
            }
            isStreamModeEnabled = !isStreamModeEnabled;
        }
        private void btn_darkmode_Click(object sender, EventArgs e)
        {
            bool isStreamModeActive = Streaming;
            if (BackColor == ColorTranslator.FromHtml("#181818"))
            {
                BackColor = Color.WhiteSmoke;
                navbar.BackColor = Color.Silver;
                panel2.BackColor = ColorTranslator.FromHtml("#555555");
                panel3.BackColor = Color.Silver;
                label2.ForeColor = Color.Black;
                prompt.BackColor = ColorTranslator.FromHtml("#2d2d2d");
                logo.ForeColor = Color.Black;
                btn_darkmode.Image = Properties.Resources.dark;
                BtnMinimize.Image = Properties.Resources.minimize_dark;
                BtnExit.Image = Properties.Resources.close_dark;
                label1.Image = Properties.Resources.prompt_dark;
            }
            else
            {
                BackColor = ColorTranslator.FromHtml("#181818");
                navbar.BackColor = ColorTranslator.FromHtml("#323232");
                panel2.BackColor = ColorTranslator.FromHtml("#323232");
                panel3.BackColor = ColorTranslator.FromHtml("#323232");
                prompt.BackColor = ColorTranslator.FromHtml("#222222");
                label2.ForeColor = Color.Silver;
                logo.ForeColor = Color.Silver;
                btn_darkmode.Image = Properties.Resources.white;
                BtnMinimize.Image = Properties.Resources.minimize_white;
                BtnExit.Image = Properties.Resources.close_white;
                label1.Image = Properties.Resources.prompt_white;
            }
            if (isStreamModeActive)
            {
                SetWindowDisplayAffinity(Handle, WDA_EXCLUDEFROMCAPTURE);
                uint exStyle = GetWindowLong(Handle, GWL_EXSTYLE);
                SetWindowLong(Handle, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
            }
        }
        private void BtnMinimize_Click(object sender, EventArgs e)
        {
            this.WindowState = FormWindowState.Minimized;
        }
        private void BtnExit_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }
        private void RaiseRunningChanged(bool on) { }
        private void RaiseShiftHeldChanged(bool held) { }
        private void RaiseInfo(string msg)
        {
            string m = msg.ToLowerInvariant();
            if (m.Contains("shift") && m.Contains("held"))
                AppendText("SHIFT HOLD\r\n", Color.Green);
            else if (m.Contains("shift") && m.Contains("released"))
                AppendText("SHIFT RELEASE\r\n", Color.Yellow);
            else if (m.Contains("lmb"))
                AppendText("LMB RELEASE\r\n", Color.Yellow);
            else if (m.Contains("auto release") || m.Contains("move out"))
                AppendText("SHIFT RELEASE\r\n", Color.Yellow);
            else if (m.Contains("started"))
                AppendText("START\r\n", Color.Cyan);
            else if (m.Contains("stopped") || m.Contains("stop esc"))
                AppendText("STOP\r\n", Color.Red);
            else
                AppendText(msg + "\r\n", Color.White);
        }
        private void RaiseError(string msg)
        {
            string m = msg.ToLowerInvariant();
            if (m.Contains("create context"))
                AppendText("ERR: CONTEXT\r\n", Color.OrangeRed);
            else if (m.Contains("mouse hook"))
                AppendText("ERR: HOOK\r\n", Color.OrangeRed);
            else if (m.Contains("main loop"))
                AppendText("ERR: LOOP\r\n", Color.OrangeRed);
            else
                AppendText("ERR: " + msg + "\r\n", Color.OrangeRed);
        }
        private void panel2_Paint(object sender, PaintEventArgs e) { }
        private void panel3_Paint(object sender, PaintEventArgs e) { }
        int isenablecheck = 0;
        private void btn_startaction_Click(object sender, EventArgs e)
        {
            if (isenablecheck == 0)
            {
                isenable = true;
                AppendText("Functions Enabled\r\n", Color.Green);
                isenablecheck = 1;
            }
            else if (isenablecheck == 1)
            {
                isenable = false;
                AppendText("Functions Disabled\r\n", Color.Yellow);
                isenablecheck = 0;
            }
        }
        private void Home_Load(object sender, EventArgs e) 
        {

        }
        private void P1_Paint(object sender, PaintEventArgs e) 
        {

        }
    }
}
