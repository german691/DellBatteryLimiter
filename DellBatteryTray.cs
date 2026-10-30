using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DellBatteryLimiter
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--generate-icons")
            {
                string outDir = AppDomain.CurrentDomain.BaseDirectory;
                IconHelper.SaveCircleIcon(Path.Combine(outDir, "green-circle.ico"), Color.FromArgb(46, 204, 113), Color.FromArgb(24, 140, 74));
                IconHelper.SaveCircleIcon(Path.Combine(outDir, "red-circle.ico"), Color.FromArgb(231, 76, 60), Color.FromArgb(168, 35, 21));
                return;
            }

            bool createdNew;
            using (Mutex mutex = new Mutex(true, "Local\\DellBatteryLimiterTrayMutex", out createdNew))
            {
                if (!createdNew)
                {
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                string logPath = Path.Combine(Path.GetTempPath(), "DellBatteryTray.log");
                try
                {
                    Application.Run(new TrayContext());
                    File.AppendAllText(logPath, DateTime.Now + " Application.Run termino normalmente\r\n");
                }
                catch (Exception ex)
                {
                    File.AppendAllText(logPath, DateTime.Now + " ERROR: " + ex + "\r\n");
                }
            }
        }
    }

    static class IconHelper
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        public static Icon CreateCircleIcon(Color fillColor, Color borderColor)
        {
            using (Bitmap bmp = new Bitmap(32, 32))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddEllipse(2, 2, 27, 27);
                    using (LinearGradientBrush brush = new LinearGradientBrush(
                        new Rectangle(2, 2, 27, 27),
                        ControlPaint.Light(fillColor, 0.25f),
                        fillColor,
                        LinearGradientMode.ForwardDiagonal))
                    {
                        g.FillPath(brush, path);
                    }
                    using (Pen pen = new Pen(borderColor, 2.2f))
                    {
                        g.DrawPath(pen, path);
                    }
                }

                // Se mantiene el handle vivo durante toda la vida del proceso (evita icono invalido)
                IntPtr hIcon = bmp.GetHicon();
                return Icon.FromHandle(hIcon);
            }
        }

        public static void SaveCircleIcon(string filePath, Color fillColor, Color borderColor)
        {
            using (Bitmap bmp = new Bitmap(64, 64))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddEllipse(4, 4, 55, 55);
                    using (LinearGradientBrush brush = new LinearGradientBrush(
                        new Rectangle(4, 4, 55, 55),
                        ControlPaint.Light(fillColor, 0.25f),
                        fillColor,
                        LinearGradientMode.ForwardDiagonal))
                    {
                        g.FillPath(brush, path);
                    }
                    using (Pen pen = new Pen(borderColor, 4.0f))
                    {
                        g.DrawPath(pen, path);
                    }
                }

                byte[] pngBytes;
                using (MemoryStream ms = new MemoryStream())
                {
                    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    pngBytes = ms.ToArray();
                }

                using (FileStream fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                using (BinaryWriter bw = new BinaryWriter(fs))
                {
                    // Header ICO con payload PNG de 64x64 (soportado desde Windows Vista/10/11)
                    bw.Write((short)0);
                    bw.Write((short)1);
                    bw.Write((short)1);
                    bw.Write((byte)64);
                    bw.Write((byte)64);
                    bw.Write((byte)0);
                    bw.Write((byte)0);
                    bw.Write((short)1);
                    bw.Write((short)32);
                    bw.Write(pngBytes.Length);
                    bw.Write(22);
                    bw.Write(pngBytes);
                }
            }
        }
    }

    class TrayContext : ApplicationContext
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly Icon _greenIcon;
        private readonly Icon _redIcon;
        private readonly ToolStripMenuItem _limitItem;
        private readonly ToolStripMenuItem _unlimitItem;
        private readonly ToolStripMenuItem _startupItem;
        private readonly System.Windows.Forms.Timer _syncTimer;

        private readonly string _baseDir;
        private readonly string _limitScript;
        private readonly string _unlimitScript;
        private readonly string _stateFile;
        private readonly string _cctkExe = @"C:\Program Files (x86)\Dell\Command Configure\X86_64\cctk.exe";
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppRunName = "DellBatteryLimiterTray";

        private bool _isLimited = true;
        private DateTime _lastStateWrite = DateTime.MinValue;

        public TrayContext()
        {
            _baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _limitScript = Path.Combine(_baseDir, "Limitar-Bateria-80.cmd");
            _unlimitScript = Path.Combine(_baseDir, "Deslimitar-Bateria-100.cmd");

            string stateDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DellBatteryLimiter");
            if (!Directory.Exists(stateDir))
            {
                Directory.CreateDirectory(stateDir);
            }
            _stateFile = Path.Combine(stateDir, "state.txt");

            _greenIcon = IconHelper.CreateCircleIcon(Color.FromArgb(46, 204, 113), Color.FromArgb(24, 140, 74));
            _redIcon = IconHelper.CreateCircleIcon(Color.FromArgb(231, 76, 60), Color.FromArgb(168, 35, 21));

            ContextMenuStrip menu = new ContextMenuStrip();

            _limitItem = new ToolStripMenuItem("\uD83D\uDFE2 Limitar bater\u00EDa (50% - 80%)");
            _limitItem.Click += (s, e) => ApplyState(true);

            _unlimitItem = new ToolStripMenuItem("\uD83D\uDD34 Deslimitar bater\u00EDa (100%)");
            _unlimitItem.Click += (s, e) => ApplyState(false);

            _startupItem = new ToolStripMenuItem("Iniciar con Windows");
            _startupItem.Checked = IsStartupEnabled();
            _startupItem.Click += ToggleStartup;

            ToolStripMenuItem exitItem = new ToolStripMenuItem("Salir");
            exitItem.Click += (s, e) => ExitThread();

            menu.Items.Add(_limitItem);
            menu.Items.Add(_unlimitItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_startupItem);
            menu.Items.Add(exitItem);

            LoadInitialState();

            _notifyIcon = new NotifyIcon
            {
                ContextMenuStrip = menu,
                Icon = _isLimited ? _greenIcon : _redIcon,
                Text = "Dell Battery Limiter",
                Visible = true
            };
            _notifyIcon.MouseClick += OnTrayMouseClick;
            UpdateTrayVisuals();

            _syncTimer = new System.Windows.Forms.Timer { Interval = 1500 };
            _syncTimer.Tick += (s, e) => CheckExternalStateChange();
            _syncTimer.Start();
        }

        private void OnTrayMouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ApplyState(!_isLimited);
            }
        }

        private void LoadInitialState()
        {
            if (IsAdministrator() && File.Exists(_cctkExe))
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(_cctkExe, "--PrimaryBattChargeCfg")
                    {
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    };
                    using (Process p = Process.Start(psi))
                    {
                        string output = p.StandardOutput.ReadToEnd();
                        p.WaitForExit(3000);
                        if (!string.IsNullOrEmpty(output))
                        {
                            _isLimited = output.IndexOf("Custom", StringComparison.OrdinalIgnoreCase) >= 0
                                      || output.IndexOf("PrimAcUse", StringComparison.OrdinalIgnoreCase) >= 0;
                            SaveStateFile(_isLimited);
                            return;
                        }
                    }
                }
                catch { }
            }

            if (File.Exists(_stateFile))
            {
                try
                {
                    string text = File.ReadAllText(_stateFile).Trim();
                    _isLimited = !string.Equals(text, "UNLIMITED", StringComparison.OrdinalIgnoreCase);
                    _lastStateWrite = File.GetLastWriteTimeUtc(_stateFile);
                    return;
                }
                catch { }
            }

            _isLimited = true;
            SaveStateFile(true);
        }

        private void CheckExternalStateChange()
        {
            if (!File.Exists(_stateFile)) return;
            try
            {
                DateTime writeTime = File.GetLastWriteTimeUtc(_stateFile);
                if (writeTime != _lastStateWrite)
                {
                    _lastStateWrite = writeTime;
                    string text = File.ReadAllText(_stateFile).Trim();
                    bool newState = !string.Equals(text, "UNLIMITED", StringComparison.OrdinalIgnoreCase);
                    if (newState != _isLimited)
                    {
                        _isLimited = newState;
                        UpdateTrayVisuals();
                    }
                }
            }
            catch { }
        }

        private void ApplyState(bool limitTo80)
        {
            try
            {
                if (IsAdministrator() && File.Exists(_cctkExe))
                {
                    string arg = limitTo80 ? "--PrimaryBattChargeCfg=Custom:50-80" : "--PrimaryBattChargeCfg=Adaptive";
                    int code = RunCctkDirect(arg);
                    if (!limitTo80 && code != 0)
                    {
                        code = RunCctkDirect("--PrimaryBattChargeCfg=Standard");
                    }
                    if (code == 0)
                    {
                        _isLimited = limitTo80;
                        SaveStateFile(_isLimited);
                        UpdateTrayVisuals();
                    }
                }
                else
                {
                    string script = limitTo80 ? _limitScript : _unlimitScript;
                    ProcessStartInfo psi = new ProcessStartInfo(script)
                    {
                        UseShellExecute = true,
                        Verb = "runas",
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    using (Process p = Process.Start(psi))
                    {
                        if (p != null)
                        {
                            p.WaitForExit(8000);
                        }
                    }
                    CheckExternalStateChange();
                }
            }
            catch
            {
                // El usuario cancelo el prompt UAC
            }
        }

        private int RunCctkDirect(string arguments)
        {
            ProcessStartInfo psi = new ProcessStartInfo(_cctkExe, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using (Process p = Process.Start(psi))
            {
                p.WaitForExit(5000);
                return p.ExitCode;
            }
        }

        private void SaveStateFile(bool limited)
        {
            try
            {
                File.WriteAllText(_stateFile, limited ? "LIMITED" : "UNLIMITED");
                _lastStateWrite = File.GetLastWriteTimeUtc(_stateFile);
            }
            catch { }
        }

        private void UpdateTrayVisuals()
        {
            _notifyIcon.Icon = _isLimited ? _greenIcon : _redIcon;
            _notifyIcon.Text = _isLimited
                ? "\uD83D\uDFE2 Bater\u00EDa Limitada (50-80%) | Click: deslimitar"
                : "\uD83D\uDD34 Bater\u00EDa Deslimitada (100%) | Click: limitar";
            _limitItem.Checked = _isLimited;
            _unlimitItem.Checked = !_isLimited;
        }

        private bool IsStartupEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    return key != null && key.GetValue(AppRunName) != null;
                }
            }
            catch { return false; }
        }

        private void ToggleStartup(object sender, EventArgs e)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key == null) return;
                    if (_startupItem.Checked)
                    {
                        key.DeleteValue(AppRunName, false);
                        _startupItem.Checked = false;
                    }
                    else
                    {
                        key.SetValue(AppRunName, "\"" + Application.ExecutablePath + "\"");
                        _startupItem.Checked = true;
                    }
                }
            }
            catch { }
        }

        private static bool IsAdministrator()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    WindowsPrincipal principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch { return false; }
        }

        protected override void ExitThreadCore()
        {
            _syncTimer.Stop();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _greenIcon.Dispose();
            _redIcon.Dispose();
            base.ExitThreadCore();
        }
    }
}


