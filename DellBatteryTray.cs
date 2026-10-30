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
        private readonly ToolStripMenuItem _customItem;
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
        private int _startPct = 50;
        private int _stopPct = 80;
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

            _limitItem = new ToolStripMenuItem();
            _limitItem.Click += (s, e) => ApplyState(true);

            _unlimitItem = new ToolStripMenuItem("\uD83D\uDD34 Deslimitar bater\u00EDa (100%)");
            _unlimitItem.Click += (s, e) => ApplyState(false);

            _customItem = new ToolStripMenuItem("\u2699\uFE0F Configurar umbral...");
            _customItem.Click += (s, e) => PromptCustomThreshold();

            _startupItem = new ToolStripMenuItem("Iniciar con Windows");
            _startupItem.Checked = IsStartupEnabled();
            _startupItem.Click += ToggleStartup;

            ToolStripMenuItem exitItem = new ToolStripMenuItem("Salir");
            exitItem.Click += (s, e) => ExitThread();

            menu.Items.Add(_limitItem);
            menu.Items.Add(_unlimitItem);
            menu.Items.Add(_customItem);
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

        private void PromptCustomThreshold()
        {
            int s = _startPct;
            int end = _stopPct;
            if (ShowThresholdDialog(ref s, ref end))
            {
                _startPct = s;
                _stopPct = end;
                ApplyState(true);
            }
        }

        private bool ShowThresholdDialog(ref int startVal, ref int stopVal)
        {
            using (Form form = new Form())
            {
                form.Text = "Configurar umbral de carga";
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MaximizeBox = false;
                form.MinimizeBox = false;
                form.StartPosition = FormStartPosition.CenterScreen;
                form.ClientSize = new Size(320, 140);
                form.TopMost = true;

                Label lbl = new Label() { Left = 16, Top = 16, Width = 288, Text = "Porcentaje de carga (ej: 80 o 50-85):" };
                TextBox tb = new TextBox() { Left = 16, Top = 40, Width = 288, Text = startVal + "-" + stopVal };
                Label hint = new Label() { Left = 16, Top = 66, Width = 288, Font = new Font(form.Font.FontFamily, 8f), ForeColor = Color.Gray, Text = "Min inicio: 50% | Max fin: 100% | Dif min: 5%" };

                Button btnOk = new Button() { Text = "Aplicar", Left = 136, Width = 80, Top = 96, DialogResult = DialogResult.OK };
                Button btnCancel = new Button() { Text = "Cancelar", Left = 224, Width = 80, Top = 96, DialogResult = DialogResult.Cancel };

                form.Controls.AddRange(new Control[] { lbl, tb, hint, btnOk, btnCancel });
                form.AcceptButton = btnOk;
                form.CancelButton = btnCancel;

                if (form.ShowDialog() == DialogResult.OK)
                {
                    string txt = tb.Text.Trim();
                    int st = 50, sp = 80;
                    if (txt.Contains("-"))
                    {
                        string[] parts = txt.Split('-');
                        if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out st) && int.TryParse(parts[1].Trim(), out sp))
                        {
                            if (st >= 50 && st <= 95 && sp >= 55 && sp <= 100 && (sp - st) >= 5)
                            {
                                startVal = st;
                                stopVal = sp;
                                return true;
                            }
                        }
                    }
                    else if (int.TryParse(txt, out sp))
                    {
                        if (sp >= 55 && sp <= 100)
                        {
                            st = 50;
                            if (sp - st < 5) st = sp - 5;
                            startVal = st;
                            stopVal = sp;
                            return true;
                        }
                    }
                    MessageBox.Show("Valores inv\u00E1lidos. Inicio debe ser 50-95%, fin 55-100% y diferencia al menos 5%.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            return false;
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
                            ParseCustomRange(output);
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
                    ParseStateText(text);
                    _lastStateWrite = File.GetLastWriteTimeUtc(_stateFile);
                    return;
                }
                catch { }
            }

            _isLimited = true;
            SaveStateFile(true);
        }

        private void ParseCustomRange(string output)
        {
            int idx = output.IndexOf("Custom:", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                string sub = output.Substring(idx + 7).Trim();
                string[] parts = sub.Split(new char[] { '-', ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    int.TryParse(parts[0], out _startPct);
                    int.TryParse(parts[1], out _stopPct);
                }
            }
        }

        private void ParseStateText(string text)
        {
            if (string.Equals(text, "UNLIMITED", StringComparison.OrdinalIgnoreCase))
            {
                _isLimited = false;
            }
            else
            {
                _isLimited = true;
                if (text.StartsWith("LIMITED:", StringComparison.OrdinalIgnoreCase))
                {
                    string r = text.Substring(8).Trim();
                    string[] parts = r.Split('-');
                    if (parts.Length == 2)
                    {
                        int.TryParse(parts[0], out _startPct);
                        int.TryParse(parts[1], out _stopPct);
                    }
                }
            }
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
                    bool wasLimited = _isLimited;
                    int oldStop = _stopPct;
                    ParseStateText(text);
                    if (_isLimited != wasLimited || _stopPct != oldStop)
                    {
                        UpdateTrayVisuals();
                    }
                }
            }
            catch { }
        }

        private void ApplyState(bool limitToCustom)
        {
            try
            {
                string rangeArg = _startPct + "-" + _stopPct;
                if (IsAdministrator() && File.Exists(_cctkExe))
                {
                    string arg = limitToCustom ? ("--PrimaryBattChargeCfg=Custom:" + rangeArg) : "--PrimaryBattChargeCfg=Adaptive";
                    int code = RunCctkDirect(arg);
                    if (!limitToCustom && code != 0)
                    {
                        code = RunCctkDirect("--PrimaryBattChargeCfg=Standard");
                    }
                    if (code == 0)
                    {
                        _isLimited = limitToCustom;
                        SaveStateFile(_isLimited);
                        UpdateTrayVisuals();
                    }
                }
                else
                {
                    string script = limitToCustom ? _limitScript : _unlimitScript;
                    string args = limitToCustom ? rangeArg : "";
                    ProcessStartInfo psi = new ProcessStartInfo(script, args)
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
                string val = limited ? ("LIMITED:" + _startPct + "-" + _stopPct) : "UNLIMITED";
                File.WriteAllText(_stateFile, val);
                _lastStateWrite = File.GetLastWriteTimeUtc(_stateFile);
            }
            catch { }
        }

        private void UpdateTrayVisuals()
        {
            _notifyIcon.Icon = _isLimited ? _greenIcon : _redIcon;
            string rangeDesc = _startPct + "% - " + _stopPct + "%";
            _limitItem.Text = "\uD83D\uDFE2 Limitar bater\u00EDa (" + rangeDesc + ")";
            _notifyIcon.Text = _isLimited
                ? ("\uD83D\uDFE2 Limitada (" + rangeDesc + ") | Click: alternar")
                : "\uD83D\uDD34 Deslimitada (100%) | Click: alternar";
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
