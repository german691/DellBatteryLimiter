using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.RegularExpressions;
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
                IconHelper.SaveCircleIcon(Path.Combine(outDir, "yellow-circle.ico"), Color.FromArgb(243, 156, 18), Color.FromArgb(180, 110, 10));
                return;
            }

            // 1. Garantizar elevacion desde el inicio para no pedir UAC en cada accion
            if (!IsAdministrator())
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath)
                    {
                        UseShellExecute = true,
                        Verb = "runas"
                    };
                    Process.Start(psi);
                }
                catch
                {
                    // Usuario cancelo el dialogo de UAC
                }
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

        public static bool IsAdministrator()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    WindowsPrincipal principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }
    }

    static class IconHelper
    {
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
        private readonly Icon _yellowIcon;

        private readonly ToolStripMenuItem _limitItem;
        private readonly ToolStripMenuItem _unlimitItem;
        private readonly ToolStripMenuItem _customItem;
        private readonly ToolStripMenuItem _refreshItem;
        private readonly ToolStripMenuItem _startupItem;
        private readonly SynchronizationContext _syncContext;

        private readonly string _cctkExe = @"C:\Program Files (x86)\Dell\Command Configure\X86_64\cctk.exe";
        private readonly string _configDir;
        private readonly string _configFile;
        private const string AppRunName = "DellBatteryLimiterTray";

        private bool _isLimited = true;
        private int _startPct = 50;
        private int _stopPct = 80;
        private bool _isBusy = false;

        public TrayContext()
        {
            _syncContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

            _configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DellBatteryLimiter");
            if (!Directory.Exists(_configDir))
            {
                Directory.CreateDirectory(_configDir);
            }
            _configFile = Path.Combine(_configDir, "config.ini");

            _greenIcon = IconHelper.CreateCircleIcon(Color.FromArgb(46, 204, 113), Color.FromArgb(24, 140, 74));
            _redIcon = IconHelper.CreateCircleIcon(Color.FromArgb(231, 76, 60), Color.FromArgb(168, 35, 21));
            _yellowIcon = IconHelper.CreateCircleIcon(Color.FromArgb(243, 156, 18), Color.FromArgb(180, 110, 10));

            ContextMenuStrip menu = new ContextMenuStrip();

            _limitItem = new ToolStripMenuItem();
            _limitItem.Click += (s, e) => TriggerApplyState(true);

            _unlimitItem = new ToolStripMenuItem("\uD83D\uDD34 Deslimitar bater\u00EDa (100%)");
            _unlimitItem.Click += (s, e) => TriggerApplyState(false);

            _customItem = new ToolStripMenuItem("\u2699\uFE0F Configurar umbral...");
            _customItem.Click += (s, e) => PromptCustomThreshold();

            _refreshItem = new ToolStripMenuItem("\uD83D\uDD04 Leer estado de hardware");
            _refreshItem.Click += (s, e) => RefreshHardwareStateAsync(showBalloon: true);

            _startupItem = new ToolStripMenuItem("Iniciar con Windows");
            _startupItem.Checked = IsStartupEnabled();
            _startupItem.Click += ToggleStartup;

            ToolStripMenuItem exitItem = new ToolStripMenuItem("Salir");
            exitItem.Click += (s, e) => ExitThread();

            menu.Items.Add(_limitItem);
            menu.Items.Add(_unlimitItem);
            menu.Items.Add(_customItem);
            menu.Items.Add(_refreshItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_startupItem);
            menu.Items.Add(exitItem);

            _notifyIcon = new NotifyIcon
            {
                ContextMenuStrip = menu,
                Icon = _yellowIcon,
                Text = "\u23F3 Leyendo estado de hardware...",
                Visible = true
            };
            _notifyIcon.MouseClick += OnTrayMouseClick;

            // Cargar configuracion guardada como punto de partida
            LoadSavedConfig();
            UpdateTrayVisuals();

            // Consultar inmediatamente el hardware en segundo plano
            RefreshHardwareStateAsync(showBalloon: false);
        }

        private void OnTrayMouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (_isBusy) return;
                TriggerApplyState(!_isLimited);
            }
        }

        private void PromptCustomThreshold()
        {
            if (_isBusy) return;

            int s = _startPct;
            int end = _stopPct;
            if (ShowThresholdDialog(ref s, ref end))
            {
                _startPct = s;
                _stopPct = end;
                TriggerApplyState(true);
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

        private void LoadSavedConfig()
        {
            if (File.Exists(_configFile))
            {
                try
                {
                    string[] lines = File.ReadAllLines(_configFile);
                    foreach (string line in lines)
                    {
                        string l = line.Trim();
                        if (l.StartsWith("Limited=", StringComparison.OrdinalIgnoreCase))
                        {
                            bool b;
                            if (bool.TryParse(l.Substring(8).Trim(), out b)) _isLimited = b;
                        }
                        else if (l.StartsWith("Start=", StringComparison.OrdinalIgnoreCase))
                        {
                            int s;
                            if (int.TryParse(l.Substring(6).Trim(), out s)) _startPct = s;
                        }
                        else if (l.StartsWith("Stop=", StringComparison.OrdinalIgnoreCase))
                        {
                            int sp;
                            if (int.TryParse(l.Substring(5).Trim(), out sp)) _stopPct = sp;
                        }
                    }
                }
                catch { }
            }
        }

        private void SaveConfig()
        {
            try
            {
                string content = string.Format("Limited={0}\r\nStart={1}\r\nStop={2}\r\nUpdated={3}\r\n",
                    _isLimited, _startPct, _stopPct, DateTime.Now.ToString("s"));
                File.WriteAllText(_configFile, content);

                // Mantener sincronizado el state.txt legado para compatibilidad con scripts
                string stateLegacy = Path.Combine(_configDir, "state.txt");
                File.WriteAllText(stateLegacy, _isLimited ? ("LIMITED:" + _startPct + "-" + _stopPct) : "UNLIMITED");
            }
            catch { }
        }

        // Consultar el hardware real en segundo plano sin bloquear la UI
        private void RefreshHardwareStateAsync(bool showBalloon)
        {
            if (_isBusy) return;
            SetBusyState(true, "\u23F3 Consultando hardware Dell...");

            ThreadPool.QueueUserWorkItem(_ =>
            {
                bool querySuccess = false;
                bool detectedLimited = _isLimited;
                int detectedStart = _startPct;
                int detectedStop = _stopPct;
                string rawOutput = "";

                if (File.Exists(_cctkExe))
                {
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo(_cctkExe, "--PrimaryBattChargeCfg")
                        {
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true,
                            WindowStyle = ProcessWindowStyle.Hidden
                        };
                        using (Process p = Process.Start(psi))
                        {
                            rawOutput = p.StandardOutput.ReadToEnd();
                            p.WaitForExit(4000);
                            if (p.ExitCode == 0 && !string.IsNullOrEmpty(rawOutput))
                            {
                                querySuccess = true;
                                if (rawOutput.IndexOf("Custom", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    detectedLimited = true;
                                    Match m = Regex.Match(rawOutput, @"Custom\s*:\s*(\d+)\s*-\s*(\d+)", RegexOptions.IgnoreCase);
                                    if (m.Success)
                                    {
                                        int.TryParse(m.Groups[1].Value, out detectedStart);
                                        int.TryParse(m.Groups[2].Value, out detectedStop);
                                    }
                                }
                                else if (rawOutput.IndexOf("PrimAcUse", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    detectedLimited = true;
                                }
                                else
                                {
                                    detectedLimited = false;
                                }
                            }
                        }
                    }
                    catch { }
                }

                _syncContext.Post(__ =>
                {
                    SetBusyState(false, null);
                    if (querySuccess)
                    {
                        _isLimited = detectedLimited;
                        if (detectedStart >= 50 && detectedStop >= 55)
                        {
                            _startPct = detectedStart;
                            _stopPct = detectedStop;
                        }
                        SaveConfig();
                        UpdateTrayVisuals();

                        if (showBalloon)
                        {
                            string msg = _isLimited
                                ? string.Format("Estado en BIOS: Limitado ({0}% - {1}%).", _startPct, _stopPct)
                                : "Estado en BIOS: Deslimitado (Carga al 100%).";
                            _notifyIcon.ShowBalloonTip(2000, "Dell Battery Limiter", msg, ToolTipIcon.Info);
                        }
                    }
                    else
                    {
                        UpdateTrayVisuals();
                        if (showBalloon)
                        {
                            _notifyIcon.ShowBalloonTip(2500, "Dell Battery Limiter", "No se pudo leer el estado del hardware.", ToolTipIcon.Warning);
                        }
                    }
                }, null);
            });
        }

        // Aplicar cambio de estado en hardware de forma asincrona con feedback visual
        private void TriggerApplyState(bool targetLimited)
        {
            if (_isBusy) return;

            string actionMsg = targetLimited
                ? string.Format("\u23F3 Limitando carga a {0}% - {1}%...", _startPct, _stopPct)
                : "\u23F3 Deslimitando carga a 100%...";

            SetBusyState(true, actionMsg);

            int start = _startPct;
            int stop = _stopPct;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                int exitCode = -1;
                string errorMsg = "";

                if (File.Exists(_cctkExe))
                {
                    try
                    {
                        if (targetLimited)
                        {
                            string arg = string.Format("--PrimaryBattChargeCfg=Custom:{0}-{1}", start, stop);
                            exitCode = RunCctk(arg, out errorMsg);
                        }
                        else
                        {
                            exitCode = RunCctk("--PrimaryBattChargeCfg=Adaptive", out errorMsg);
                            if (exitCode != 0)
                            {
                                exitCode = RunCctk("--PrimaryBattChargeCfg=Standard", out errorMsg);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        errorMsg = ex.Message;
                        exitCode = -99;
                    }
                }
                else
                {
                    errorMsg = "cctk.exe no encontrado en el sistema.";
                    exitCode = -404;
                }

                _syncContext.Post(__ =>
                {
                    SetBusyState(false, null);

                    if (exitCode == 0)
                    {
                        _isLimited = targetLimited;
                        SaveConfig();
                        UpdateTrayVisuals();

                        string successMsg = _isLimited
                            ? string.Format("Bater\u00EDa limitada a {0}% - {1}%. Carga cortada.", _startPct, _stopPct)
                            : "Bater\u00EDa deslimitada. Carga completa al 100% activada.";

                        _notifyIcon.ShowBalloonTip(2500, "Dell Battery Limiter", successMsg, ToolTipIcon.Info);
                    }
                    else
                    {
                        UpdateTrayVisuals();
                        string failMsg = "Error al aplicar configuraci\u00F3n en BIOS (" + exitCode + "). " + errorMsg;
                        _notifyIcon.ShowBalloonTip(3500, "Dell Battery Limiter", failMsg.Trim(), ToolTipIcon.Error);
                    }
                }, null);
            });
        }

        private int RunCctk(string arguments, out string outputMsg)
        {
            outputMsg = "";
            ProcessStartInfo psi = new ProcessStartInfo(_cctkExe, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using (Process p = Process.Start(psi))
            {
                string stdOut = p.StandardOutput.ReadToEnd();
                string stdErr = p.StandardError.ReadToEnd();
                p.WaitForExit(7000);
                outputMsg = (!string.IsNullOrEmpty(stdErr) ? stdErr : stdOut).Trim();
                return p.ExitCode;
            }
        }

        private void SetBusyState(bool busy, string busyTooltip)
        {
            _isBusy = busy;
            _limitItem.Enabled = !busy;
            _unlimitItem.Enabled = !busy;
            _customItem.Enabled = !busy;
            _refreshItem.Enabled = !busy;

            if (busy)
            {
                _notifyIcon.Icon = _yellowIcon;
                if (!string.IsNullOrEmpty(busyTooltip))
                {
                    if (busyTooltip.Length > 63) busyTooltip = busyTooltip.Substring(0, 60) + "...";
                    _notifyIcon.Text = busyTooltip;
                }
            }
        }

        private void UpdateTrayVisuals()
        {
            if (_isBusy) return;

            _notifyIcon.Icon = _isLimited ? _greenIcon : _redIcon;
            string rangeDesc = _startPct + "% - " + _stopPct + "%";
            _limitItem.Text = "\uD83D\uDFE2 Limitar bater\u00EDa (" + rangeDesc + ")";
            string tip = _isLimited
                ? ("\uD83D\uDFE2 Limitada (" + rangeDesc + ") | Clic: alternar")
                : "\uD83D\uDD34 Deslimitada (100%) | Clic: alternar";

            if (tip.Length > 63) tip = tip.Substring(0, 60) + "...";
            _notifyIcon.Text = tip;

            _limitItem.Checked = _isLimited;
            _unlimitItem.Checked = !_isLimited;
        }

        private static bool IsStartupEnabled()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe", "/query /tn \"" + AppRunName + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit(3000);
                    if (p.ExitCode == 0) return true;
                }
            }
            catch { }

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false))
                {
                    return key != null && key.GetValue(AppRunName) != null;
                }
            }
            catch
            {
                return false;
            }
        }

        private void ToggleStartup(object sender, EventArgs e)
        {
            try
            {
                bool enable = !_startupItem.Checked;
                string args = enable
                    ? string.Format("/create /tn \"{0}\" /tr \"\\\"{1}\\\"\" /sc onlogon /rl highest /f", AppRunName, Application.ExecutablePath)
                    : string.Format("/delete /tn \"{0}\" /f", AppRunName);

                ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe", args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit(4000);
                    if (p.ExitCode == 0)
                    {
                        _startupItem.Checked = enable;

                        // Limpiar clave de registro Run vieja si existia
                        try
                        {
                            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                            {
                                if (key != null) key.DeleteValue(AppRunName, false);
                            }
                        }
                        catch { }
                    }
                    else
                    {
                        MessageBox.Show("No se pudo configurar el inicio programado con Windows.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al modificar inicio con Windows: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        protected override void ExitThreadCore()
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _greenIcon.Dispose();
            _redIcon.Dispose();
            _yellowIcon.Dispose();
            base.ExitThreadCore();
        }
    }
}
