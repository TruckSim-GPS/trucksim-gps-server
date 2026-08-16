using System;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Funbit.Ets.Telemetry.Server.Controllers;
using Funbit.Ets.Telemetry.Server.Data;
using Funbit.Ets.Telemetry.Server.Helpers;
using AutoUpdaterDotNET;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Funbit.Ets.Telemetry.Server.Setup;

namespace Funbit.Ets.Telemetry.Server
{
    public partial class MainForm : Form
    {
        [DllImport("user32.dll")]
        static extern int RegisterWindowMessage(string message);
        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        const int SW_SHOWNORMAL = 1;

        int _showExistingInstanceMessage;

        MinimalHttpServer _server;
        bool _hasCheckedForUpdates;
        bool _startedMinimized;
        static readonly log4net.ILog Log = log4net.LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        readonly HttpClient _broadcastHttpClient = new HttpClient();
        static readonly Encoding Utf8 = new UTF8Encoding(false);
        static readonly string BroadcastUrl = ConfigurationManager.AppSettings["BroadcastUrl"];
        static readonly string BroadcastUserId = Convert.ToBase64String(
            Utf8.GetBytes(ConfigurationManager.AppSettings["BroadcastUserId"] ?? ""));
        static readonly string BroadcastUserPassword = Convert.ToBase64String(
            Utf8.GetBytes(ConfigurationManager.AppSettings["BroadcastUserPassword"] ?? ""));
        static readonly int BroadcastRateInSeconds = Math.Min(Math.Max(1, 
            Convert.ToInt32(ConfigurationManager.AppSettings["BroadcastRate"])), 86400);
        static readonly bool UseTestTelemetryData = Convert.ToBoolean(
            ConfigurationManager.AppSettings["UseEts2TestTelemetryData"]);

        public MainForm()
        {
            InitializeComponent();
            _showExistingInstanceMessage = RegisterWindowMessage(Program.ShowExistingInstanceMessage);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == _showExistingInstanceMessage)
            {
                // Another instance tried to start - bring this window to the foreground
                ShowWindow(Handle, SW_SHOWNORMAL);
                ShowInTaskbar = true;
                WindowState = FormWindowState.Normal;
                Activate();
                SetForegroundWindow(Handle);
                if (_startedMinimized && !_hasCheckedForUpdates)
                    CheckForUpdates();
            }
            base.WndProc(ref m);
        }

        static string IpToEndpointUrl(string host)
        {
            return $"http://{host}:{ConfigurationManager.AppSettings["Port"]}";
        }

        void Setup()
        {
            try
            {
                if (Program.UninstallMode && SetupManager.Steps.All(s => s.Status == SetupStatus.Uninstalled))
                {
                    MessageBox.Show(this, @"Server is not installed, nothing to uninstall.", @"Done",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Environment.Exit(0);
                }

                if (Program.UninstallMode || SetupManager.Steps.Any(s => s.Status != SetupStatus.Installed))
                {
                    // we wait here until setup is complete
                    var result = new SetupForm().ShowDialog(this);
                    if (result == DialogResult.Abort)
                        Environment.Exit(0);
                }

                // raise priority to make server more responsive (it does not eat CPU though!)
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.AboveNormal;
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                ex.ShowAsMessageBox(this, @"Setup error");
            }
        }

        void Start()
        {
            try
            {
                Log.Info("=== Server Start Begin ===");
                Log.InfoFormat("Current Date/Time: {0}", DateTime.Now);
                Log.InfoFormat(".NET Framework Version: {0}", Environment.Version);
                Log.InfoFormat("OS Version: {0}", Environment.OSVersion);

                // load list of available network interfaces
                var networkInterfaces = NetworkHelper.GetAllActiveNetworkInterfaces();
                Log.InfoFormat("Found {0} active network interfaces", networkInterfaces.Count());

                interfacesDropDown.Items.Clear();
                foreach (var networkInterface in networkInterfaces)
                {
                    Log.InfoFormat("Network Interface: {0} - {1}", networkInterface.Name, networkInterface.Ip);
                    interfacesDropDown.Items.Add(networkInterface);
                }

                // select remembered interface or default
                var rememberedInterface = networkInterfaces.FirstOrDefault(
                    i => i.Id == Settings.Instance.DefaultNetworkInterfaceId);
                if (rememberedInterface != null)
                {
                    Log.InfoFormat("Using remembered interface: {0}", rememberedInterface.Name);
                    interfacesDropDown.SelectedItem = rememberedInterface;
                }
                else
                {
                    Log.Info("Using default interface (first available)");
                    interfacesDropDown.SelectedIndex = 0; // select default interface
                }

                // Start minimal HTTP server (bypasses HTTP.SYS to avoid KB5066835/KB5065789 bug)
                var port = int.Parse(ConfigurationManager.AppSettings["Port"] ?? "31377");
                _server = new MinimalHttpServer(port);
                _server.Start();

                // start ETS2 process watchdog timer
                statusUpdateTimer.Enabled = true;

                // turn on broadcasting if set
                if (!string.IsNullOrEmpty(BroadcastUrl))
                {
                    _broadcastHttpClient.DefaultRequestHeaders.Add("X-UserId", BroadcastUserId);
                    _broadcastHttpClient.DefaultRequestHeaders.Add("X-UserPassword", BroadcastUserPassword);
                    broadcastTimer.Interval = BroadcastRateInSeconds * 1000;
                    broadcastTimer.Enabled = true;
                }

                // show tray icon
                trayIcon.Visible = true;
                
                // make sure that form is visible
                Activate();
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                ex.ShowAsMessageBox(this, @"Network error", MessageBoxIcon.Exclamation);
            }
        }
        
        void MainForm_Load(object sender, EventArgs e)
        {
            // log current version for debugging
            Log.InfoFormat("Running version {0} on {1} ({2}) {3}", AssemblyHelper.Version,
                Environment.OSVersion,
                Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit",
                Program.UninstallMode ? "[UNINSTALL MODE]" : "");
            Text += @" " + AssemblyHelper.Version;

            // initialize Start with Windows toggles from registry
            var startWithWindows = IsStartWithWindowsEnabled();
            startWithWindowsToolStripMenuItem.Checked = startWithWindows;
            trayStartWithWindowsMenuItem.Checked = startWithWindows;

            // install or uninstall server if needed
            Setup();

            // start WebApi server
            Start();

            if (Program.StartMinimized)
            {
                _startedMinimized = true;
                trayIcon.Tag = "Already shown";
                WindowState = FormWindowState.Minimized;
                ShowInTaskbar = false;
                trayIcon.ShowBalloonTip(3000, @"TruckSim GPS Telemetry Server",
                    @"Running in the background. Double-click to open.", ToolTipIcon.Info);
            }
            else
            {
                CheckForUpdates();
            }
        }

        void MainForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            try
            {
                Log.Info("Application closing, stopping server...");
                _server?.Stop();
                _server?.Dispose();
                trayIcon.Visible = false;
                Log.Info("Server stopped successfully");
            }
            catch (Exception ex)
            {
                Log.Error("Error during application shutdown", ex);
            }
        }
    
        void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            RestoreWindow();
        }

        void closeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }

        void trayIcon_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            RestoreWindow();
        }

        void RestoreWindow()
        {
            ShowWindow(Handle, SW_SHOWNORMAL);
            ShowInTaskbar = true;
            WindowState = FormWindowState.Normal;
            Activate();
            SetForegroundWindow(Handle);
            if (_startedMinimized && !_hasCheckedForUpdates)
                CheckForUpdates();
        }

        void statusUpdateTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                // Update the main status
                if (UseTestTelemetryData)
                {
                    statusLabel.Text = @"Connected to Ets2TestTelemetry.json";
                    statusLabel.ForeColor = Color.DarkGreen;
                } 
                else if (Ets2ProcessHelper.IsEts2Running && ScsTelemetryDataReader.Instance.IsConnected)
                {
                    statusLabel.Text = $"Connected to the simulator ({Ets2ProcessHelper.LastRunningGameName})";
                    statusLabel.ForeColor = Color.DarkGreen;
                }
                else if (Ets2ProcessHelper.IsEts2Running)
                {
                    statusLabel.Text = $"Simulator is running ({Ets2ProcessHelper.LastRunningGameName})";
                    statusLabel.ForeColor = Color.Teal;
                }
                else
                {
                    statusLabel.Text = @"Simulator is not running";
                    statusLabel.ForeColor = Color.FromArgb(240, 55, 30);
                }

                // Always show game installation info for both games
                UpdateGameInfo();
                
                // Auto-correct game paths if running game is detected but path is invalid
                TryAutoCorrectGamePath();
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                ex.ShowAsMessageBox(this, @"Process error");
                statusUpdateTimer.Enabled = false;
            }
        }


        void appUrlLabel_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            ProcessHelper.OpenUrl(((LinkLabel)sender).Text);
        }
        
        void MainForm_Resize(object sender, EventArgs e)
        {
            ShowInTaskbar = WindowState != FormWindowState.Minimized;
            if (!ShowInTaskbar && trayIcon.Tag == null)
            {
                trayIcon.ShowBalloonTip(1000, @"TruckSim GPS Telemetry Server", @"Double-click to restore.", ToolTipIcon.Info);
                trayIcon.Tag = "Already shown";
            }
        }

        void interfaceDropDown_SelectedIndexChanged(object sender, EventArgs e)
        {
            var selectedInterface = (NetworkInterfaceInfo) interfacesDropDown.SelectedItem;
            appUrlLabel.Text = IpToEndpointUrl(selectedInterface.Ip) + TelemetryEndpoints.TelemetryAppUriPath;
            ipAddressLabel.Text = selectedInterface.Ip;
            Settings.Instance.DefaultNetworkInterfaceId = selectedInterface.Id;
            Settings.Instance.Save();
        }

        async void broadcastTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                broadcastTimer.Enabled = false;
                var data = ScsTelemetryDataReader.Instance.Read();
                var json = JsonConvert.SerializeObject(data);
                using (var content = new StringContent(json, Utf8, "application/json"))
                {
                    await _broadcastHttpClient.PostAsync(BroadcastUrl, content);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
            broadcastTimer.Enabled = true;
        }
        
        void websiteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ProcessHelper.OpenUrl("https://trucksimgps.com/");
        }

        void donateToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ProcessHelper.OpenUrl("https://discord.gg/RdC99Er37U");
        }

        void helpToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ProcessHelper.OpenUrl("https://github.com/TruckSim-GPS/trucksim-gps-server");
        }

        void openLogFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string logFile = log4net.LogManager.GetRepository().GetAppenders()
                .OfType<log4net.Appender.FileAppender>().First().File;
            Process.Start("explorer.exe", File.Exists(logFile)
                ? $"/select,\"{logFile}\""
                : $"\"{Path.GetDirectoryName(logFile)}\"");
        }

        void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // TODO: implement later
        }

        void startWithWindowsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            trayStartWithWindowsMenuItem.Checked = startWithWindowsToolStripMenuItem.Checked;
            SetStartWithWindows(startWithWindowsToolStripMenuItem.Checked);
        }

        void trayStartWithWindowsMenuItem_Click(object sender, EventArgs e)
        {
            startWithWindowsToolStripMenuItem.Checked = trayStartWithWindowsMenuItem.Checked;
            SetStartWithWindows(trayStartWithWindowsMenuItem.Checked);
        }

        const string StartupRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string StartupValueName = "TruckSimGPS";

        bool IsStartWithWindowsEnabled()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, false))
            {
                return key?.GetValue(StartupValueName) != null;
            }
        }

        void SetStartWithWindows(bool enabled)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, true))
            {
                if (enabled)
                {
                    string exePath = Process.GetCurrentProcess().MainModule.FileName;
                    key.SetValue(StartupValueName, $"\"{exePath}\" -minimized");
                }
                else
                {
                    key.DeleteValue(StartupValueName, false);
                }
            }
        }

        void rerunSetupToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                // Make sure that game is not running during setup
                if (Ets2ProcessHelper.IsEts2Running)
                {
                    MessageBox.Show(this,
                        @"In order to proceed the ETS2/ATS game must not be running." + Environment.NewLine +
                        @"Please exit the game and try again.", @"Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // FORCE setup to reconfigure by temporarily clearing N/A paths
                string originalEts2Path = Settings.Instance.Ets2GamePath;
                string originalAtsPath = Settings.Instance.AtsGamePath;
#if DEBUG
                Console.WriteLine($"SETUP DEBUG - Original ETS2 path: '{originalEts2Path}'");
                Console.WriteLine($"SETUP DEBUG - Original ATS path: '{originalAtsPath}'");
#endif
                
                bool clearedPaths = false;
                
                if (originalEts2Path == "N/A")
                {
#if DEBUG
                    Console.WriteLine("SETUP DEBUG - Clearing ETS2 path to force reconfiguration");
#endif
                    Settings.Instance.Ets2GamePath = null; // Clear to force setup
                    clearedPaths = true;
                }
                
                if (originalAtsPath == "N/A")
                {
#if DEBUG
                    Console.WriteLine("SETUP DEBUG - Clearing ATS path to force reconfiguration");
#endif
                    Settings.Instance.AtsGamePath = null; // Clear to force setup
                    clearedPaths = true;
                }
                
                if (clearedPaths)
                {
                    Settings.Instance.Save();
#if DEBUG
                    Console.WriteLine("SETUP DEBUG - Cleared N/A paths, setup will now prompt for configuration");
#endif
                }

                // Temporarily disable the status timer to prevent interference
                statusUpdateTimer.Enabled = false;

                try
                {
                    // Set the force setup flag for this session and future elevated sessions
                    Program.ForceSetupMode = true;
                    
                    // Launch the setup form
                    var result = new SetupForm().ShowDialog(this);
                    
                    if (result == DialogResult.OK)
                    {
#if DEBUG
                        Console.WriteLine("SETUP DEBUG - Setup completed successfully");
#endif
                        // Refresh the status display immediately after successful setup
                        statusUpdateTimer_Tick(this, EventArgs.Empty);
                        // Try auto-correction after setup in case there were manual installs
                        TryAutoCorrectGamePath();
                    }
                    else
                    {
#if DEBUG
                        Console.WriteLine("SETUP DEBUG - Setup was cancelled or failed");
#endif
                        // Restore original path if setup was cancelled
                        if (originalAtsPath == "N/A")
                        {
                            Settings.Instance.AtsGamePath = originalAtsPath;
                            Settings.Instance.Save();
                        }
                    }
                }
                finally
                {
                    // Re-enable the status timer
                    statusUpdateTimer.Enabled = true;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                ex.ShowAsMessageBox(this, @"Setup error");
                
                // Ensure timer is re-enabled even if there was an error
                if (!statusUpdateTimer.Enabled)
                    statusUpdateTimer.Enabled = true;
            }
        }

        void trayIcon_BalloonTipClicked(object sender, EventArgs e)
        {
            RestoreWindow();
        }

        void checkForUpdatesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CheckForUpdates(reportErrors: true);
        }

        void CheckForUpdates(bool reportErrors = false)
        {
            _hasCheckedForUpdates = true;

            // Fetch release data ourselves with explicit UTF-8 encoding.
            // AutoUpdater.NET's WebClient doesn't set Encoding, causing emoji garbling
            // on some systems (known issues #735 and #632 on their repo).
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    using (var client = new System.Net.WebClient())
                    {
                        client.Encoding = Encoding.UTF8;
                        client.Headers[System.Net.HttpRequestHeader.UserAgent] = "TruckSimGPS-Server";
                        string json = client.DownloadString(
                            "https://api.github.com/repos/TruckSim-GPS/trucksim-gps-server/releases/latest");

                        var release = JObject.Parse(json);
                        string tagName = release.Value<string>("tag_name") ?? "";
                        string version = tagName.TrimStart('v');
                        string changelogUrl = release.Value<string>("html_url");
                        string downloadUrl = changelogUrl;
                        string releaseBody = release.Value<string>("body") ?? "";

                        var assets = release["assets"] as JArray;
                        if (assets != null)
                        {
                            foreach (var asset in assets)
                            {
                                string name = asset.Value<string>("name") ?? "";
                                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                {
                                    downloadUrl = asset.Value<string>("browser_download_url");
                                    break;
                                }
                            }
                        }

                        // Only offer the update if an installer .exe asset is attached to the release
                        bool hasInstaller = downloadUrl != changelogUrl;
                        var installedVersion = Assembly.GetEntryAssembly().GetName().Version;

                        var args = new UpdateInfoEventArgs
                        {
                            CurrentVersion = version,
                            ChangelogURL = changelogUrl,
                            DownloadURL = downloadUrl,
                            InstallerArgs = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS",
                            InstalledVersion = installedVersion,
                            IsUpdateAvailable = hasInstaller && new Version(version) > installedVersion
                        };

                        BeginInvoke(new Action(() => ShowUpdateResult(args, releaseBody, reportErrors)));
                    }
                }
                catch (Exception)
                {
                    if (reportErrors)
                        BeginInvoke(new Action(() =>
                            MessageBox.Show(this, "Could not reach the update server. Please try again later.",
                                "Update Check Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning)));
                }
            });
        }

        void ShowUpdateResult(UpdateInfoEventArgs args, string releaseBody, bool reportErrors)
        {
            if (!args.IsUpdateAvailable)
            {
                if (reportErrors)
                    MessageBox.Show(this, $"You are running the latest version (v{args.InstalledVersion}).",
                        "No Update Available", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var form = new UpdateForm(
                args.CurrentVersion, args.InstalledVersion.ToString(),
                releaseBody, args.ChangelogURL))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    if (DownloadVerifyAndLaunchUpdate(args))
                        Application.Exit();
                }
            }
        }

        /// <summary>
        /// Downloads the installer, verifies its Authenticode signature against the pinned
        /// signer identity, and launches it. Returns true if the installer was launched
        /// and the caller should exit; false if anything failed (download error, signature
        /// mismatch, downgrade attempt) — in that case the user continues running the current
        /// version.
        ///
        /// TOCTOU defense: we hold an exclusive-write file lock (FileShare.Read denies write
        /// and delete to other processes) from after download through Process.Start. Once
        /// Process.Start returns, Windows pins the file via the spawned process's section
        /// mapping, so the handle can be released safely.
        /// </summary>
        bool DownloadVerifyAndLaunchUpdate(UpdateInfoEventArgs args)
        {
            string tempPath = Path.Combine(Path.GetTempPath(),
                $"TruckSimGPS_Update_{Guid.NewGuid():N}.exe");

            // Step 1: Download with a progress dialog. The download is kicked off from the
            // dialog's Shown event so DownloadFileCompleted cannot fire before ShowDialog
            // has a running message pump (otherwise dialog.Close() would no-op and the
            // dialog would hang).
            Exception downloadError = null;
            using (var dialog = new UpdateDownloadDialog())
            using (var client = new WebClient())
            {
                client.Headers[HttpRequestHeader.UserAgent] = "TruckSimGPS-Server";
                client.DownloadProgressChanged += (s, e) => dialog.SetProgress(e.ProgressPercentage);
                client.DownloadFileCompleted += (s, e) =>
                {
                    downloadError = e.Error;
                    dialog.MarkDone();
                };
                dialog.Shown += (s, e) =>
                    client.DownloadFileAsync(new Uri(args.DownloadURL), tempPath);
                dialog.ShowDialog(this);
            }

            if (downloadError != null)
            {
                Log.Error("Update download failed", downloadError);
                SafeDelete(tempPath);
                MessageBox.Show(this,
                    "Could not download the update. Please check your internet connection and try again.",
                    "Update Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            // Step 2-4 must hold the lock contiguously: verify, downgrade check, launch.
            FileStream lockHandle;
            try
            {
                // FileShare.Read denies write and delete from other processes; permits our
                // own verifier and the OS process loader to read.
                lockHandle = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            catch (Exception ex)
            {
                Log.Error("Could not open downloaded update for verification", ex);
                SafeDelete(tempPath);
                MessageBox.Show(this,
                    "The downloaded update could not be opened. Please try again.",
                    "Update Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            using (lockHandle)
            {
                // Step 2: Verify Authenticode signature against pinned signer + root.
                var verification = SignatureVerifier.Verify(tempPath);
                if (!verification.IsValid)
                {
                    Log.ErrorFormat(
                        "Update rejected — signature verification failed. Reason: {0}. Signer: {1}",
                        verification.FailureReason,
                        verification.SignerSubject ?? "<unknown>");
                    // Release the lock so SafeDelete can remove the file.
                    lockHandle.Dispose();
                    SafeDelete(tempPath);
                    MessageBox.Show(this,
                        "The downloaded update could not be verified and was rejected.\n\n" +
                        "Please report this to support — your current version will keep working.",
                        "Update Verification Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }

                // Step 3: Downgrade-attack guard — refuse to launch an installer whose
                // ProductVersion is older than what is currently running. An attacker who
                // can substitute a release asset on GitHub could otherwise point users at an
                // older legitimately-signed installer with a known vulnerability.
                if (!CheckInstallerIsNotADowngrade(tempPath, args.InstalledVersion, out string downgradeReason))
                {
                    Log.ErrorFormat("Update rejected — downgrade guard failed: {0}", downgradeReason);
                    lockHandle.Dispose();
                    SafeDelete(tempPath);
                    MessageBox.Show(this,
                        "The downloaded update could not be verified and was rejected.\n\n" +
                        "Please report this to support — your current version will keep working.",
                        "Update Verification Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }

                Log.InfoFormat("Update signature verified. Signer: {0}", verification.SignerSubject);

                // Step 4: Launch the verified installer. ShellExecute triggers UAC because
                // the installer manifest requires admin. Once Process.Start returns,
                // Windows has mapped the executable section and the lock can be safely
                // released — closing the using block.
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = tempPath,
                        Arguments = args.InstallerArgs ?? string.Empty,
                        UseShellExecute = true
                    });
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Error("Failed to launch verified update installer", ex);
                    lockHandle.Dispose();
                    SafeDelete(tempPath);
                    MessageBox.Show(this,
                        $"The update was verified but could not be launched:\n\n{ex.Message}",
                        "Update Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }
            }
        }

        /// <summary>
        /// Reads the downloaded installer's ProductVersion and confirms it is at least the
        /// currently running version. Returns false (with a reason) if the downloaded version
        /// is older or unreadable — both are treated as rejections.
        /// </summary>
        static bool CheckInstallerIsNotADowngrade(string installerPath, Version currentVersion, out string reason)
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(installerPath);
                if (string.IsNullOrEmpty(info.ProductVersion))
                {
                    reason = "Installer is missing ProductVersion metadata";
                    return false;
                }

                if (!Version.TryParse(info.ProductVersion, out Version downloadedVersion))
                {
                    reason = $"Could not parse installer ProductVersion '{info.ProductVersion}'";
                    return false;
                }

                if (downloadedVersion < currentVersion)
                {
                    reason = $"Installer version {downloadedVersion} is older than installed version {currentVersion}";
                    return false;
                }

                reason = null;
                return true;
            }
            catch (Exception ex)
            {
                reason = $"Could not read installer version: {ex.Message}";
                return false;
            }
        }

        static void SafeDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                Log.WarnFormat("Could not delete temp file '{0}': {1}", path, ex.Message);
            }
        }

        /// <summary>
        /// Modal "Downloading update..." dialog with a progress bar. No buttons, no close
        /// box — runs to completion driven by the WebClient events on the UI thread.
        /// </summary>
        sealed class UpdateDownloadDialog : Form
        {
            readonly ProgressBar _bar;

            public UpdateDownloadDialog()
            {
                Text = "Downloading update";
                FormBorderStyle = FormBorderStyle.FixedDialog;
                StartPosition = FormStartPosition.CenterParent;
                ControlBox = false;
                MinimizeBox = false;
                MaximizeBox = false;
                ShowInTaskbar = false;
                ClientSize = new Size(380, 90);

                var label = new Label
                {
                    Text = "Downloading update... This may take a moment.",
                    AutoSize = false,
                    Size = new Size(340, 20),
                    Location = new Point(20, 18),
                    Font = new Font("Segoe UI", 9.5f)
                };

                _bar = new ProgressBar
                {
                    Location = new Point(20, 48),
                    Size = new Size(340, 20),
                    Minimum = 0,
                    Maximum = 100,
                    Style = ProgressBarStyle.Continuous
                };

                Controls.Add(label);
                Controls.Add(_bar);
            }

            public void SetProgress(int percentage)
            {
                if (percentage < 0) percentage = 0;
                if (percentage > 100) percentage = 100;
                _bar.Value = percentage;
            }

            public void MarkDone()
            {
                _bar.Value = 100;
                Close();
            }
        }

        void UpdateGameInfo()
        {
            // Always show both ETS2 and ATS info
            UpdateEts2Info();
            UpdateAtsInfo();
        }
        
        void UpdateEts2Info()
        {
            string ets2Path = Settings.Instance.Ets2GamePath ?? "Not configured";
            string displayPath = ets2Path == "N/A" ? "Installation skipped" : ets2Path;
            ets2PathLabel.Text = displayPath;
            
            string statusMessage;
            var result = CheckGamePluginStatus("ETS2", out statusMessage);
            string pluginText = GetSimplePluginText(result, statusMessage);
            ets2PluginStatusLabel.Text = pluginText;
            ets2PluginStatusLabel.ForeColor = GetStatusColor(result, statusMessage);
            
            // Show "Copy missing plugins" button only for PluginMissing state
            ets2CopyPluginButton.Visible = (result == PluginValidationResult.PluginMissing);
        }
        
        void UpdateAtsInfo()
        {
            string atsPath = Settings.Instance.AtsGamePath ?? "Not configured";
            string displayPath = atsPath == "N/A" ? "Installation skipped" : atsPath;
            atsPathLabel.Text = displayPath;
            
            string statusMessage;
            var result = CheckGamePluginStatus("ATS", out statusMessage);
            string pluginText = GetSimplePluginText(result, statusMessage);
            atsPluginStatusLabel.Text = pluginText;
            atsPluginStatusLabel.ForeColor = GetStatusColor(result, statusMessage);
            
            // Show "Copy missing plugins" button only for PluginMissing state
            atsCopyPluginButton.Visible = (result == PluginValidationResult.PluginMissing);
        }
        
        string GetSimplePluginText(PluginValidationResult result, string baseMessage)
        {
            switch (result)
            {
                case PluginValidationResult.Valid:
                    return "✓ Plugin OK";

                case PluginValidationResult.PluginMissing:
                    return "⚠ Plugin missing";

                case PluginValidationResult.InvalidPath:
                    if (baseMessage == "Installation skipped")
                        return "○ Not configured";
                    else if (baseMessage == "Installation corrupted")
                        return "⚠ Install corrupted (please reinstall)";
                    else
                        return "✗ Invalid path (Server > Re-run Setup)";

                default:
                    return "Unknown";
            }
        }
        
        Color GetStatusColor(PluginValidationResult result, string statusMessage = "")
        {
            switch (result)
            {
                case PluginValidationResult.Valid:
                    return Color.DarkGreen;
                case PluginValidationResult.PluginMissing:
                    return Color.FromArgb(180, 100, 0); // Darker orange for better contrast
                case PluginValidationResult.InvalidPath:
                default:
                    if (statusMessage == "Installation skipped")
                        return Color.FromArgb(128, 128, 128); // Gray for not configured
                    else
                        return Color.FromArgb(200, 45, 25); // Darker red for actual errors
            }
        }
        
        void TryAutoCorrectGamePath()
        {
            try
            {
#if DEBUG
                Console.WriteLine($"AUTO-CORRECT DEBUG: UseTestTelemetryData={UseTestTelemetryData}, IsEts2Running={Ets2ProcessHelper.IsEts2Running}, IsConnected={ScsTelemetryDataReader.Instance.IsConnected}");
#endif

                // Only try auto-correction if game is running (connected or not - we'll try both cases)
                if (UseTestTelemetryData || !Ets2ProcessHelper.IsEts2Running)
                {
#if DEBUG
                    Console.WriteLine("AUTO-CORRECT: Skipped - test mode enabled or game not running");
#endif
                    return;
                }

                string runningGame = Ets2ProcessHelper.LastRunningGameName;
                string detectedPath = Ets2ProcessHelper.LastRunningGamePath;

#if DEBUG
                Console.WriteLine($"AUTO-CORRECT DEBUG: RunningGame='{runningGame}', DetectedPath='{detectedPath}'");
#endif

                // Need both game name and detected path
                if (string.IsNullOrEmpty(runningGame) || string.IsNullOrEmpty(detectedPath))
                {
#if DEBUG
                    Console.WriteLine("AUTO-CORRECT: Skipped - missing game name or detected path");
#endif
                    return;
                }

                // Get current stored path for the running game
                string currentStoredPath = runningGame == "ETS2" ? Settings.Instance.Ets2GamePath : Settings.Instance.AtsGamePath;

#if DEBUG
                Console.WriteLine($"AUTO-CORRECT DEBUG: CurrentStoredPath='{currentStoredPath}', IsInvalid={IsGamePathInvalid(currentStoredPath)}");
#endif

                // Skip if detected path is the same as current stored path
                if (string.Equals(currentStoredPath, detectedPath, StringComparison.OrdinalIgnoreCase))
                {
#if DEBUG
                    Console.WriteLine("AUTO-CORRECT: Skipped - detected path is same as stored path");
#endif
                    return;
                }

                // Always prioritize the running executable path - validate that detected path is actually a valid game installation
                if (!IsValidGamePath(detectedPath, runningGame))
                {
#if DEBUG
                    Console.WriteLine($"AUTO-CORRECT: Skipped - detected path '{detectedPath}' is not a valid game installation");
#endif
                    return;
                }

                // Update the stored path
#if DEBUG
                Console.WriteLine($"AUTO-CORRECT: Prioritizing running executable - updating {runningGame} path from '{currentStoredPath}' to '{detectedPath}'");
#endif
                if (runningGame == "ETS2")
                {
                    Settings.Instance.Ets2GamePath = detectedPath;
                }
                else
                {
                    Settings.Instance.AtsGamePath = detectedPath;
                }
                Settings.Instance.Save();

                // Force refresh after a short delay to ensure settings are saved
                System.Threading.Thread.Sleep(100);

                // Refresh the display multiple times to ensure it updates
                UpdateGameInfo();
#if DEBUG
                Console.WriteLine($"AUTO-CORRECT: Path update completed, display refreshed");
#endif
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                // Don't let auto-correction errors break the main status update
            }
        }
        
        bool IsGamePathInvalid(string gamePath)
        {
            return string.IsNullOrEmpty(gamePath) || gamePath == "N/A" || !IsValidGamePath(gamePath);
        }
        
        bool IsValidGamePath(string gamePath, string gameName = null)
        {
            if (string.IsNullOrEmpty(gamePath))
                return false;
                
            try
            {
                // Check for base.scs file (game data archive)
                var baseScsPath = System.IO.Path.Combine(gamePath, "base.scs");
                if (!System.IO.File.Exists(baseScsPath))
                    return false;

                // Check for bin directory
                var binPath = System.IO.Path.Combine(gamePath, "bin");
                if (!System.IO.Directory.Exists(binPath))
                    return false;

                // If game name is provided, check for the actual game executable (enhanced validation)
                if (!string.IsNullOrEmpty(gameName))
                {
                    string gameExeName = gameName == "ETS2" ? "eurotrucks2.exe" : "amtrucks.exe";
                    var gameExePath = System.IO.Path.Combine(gamePath, "bin", "win_x64", gameExeName);
                    return System.IO.File.Exists(gameExePath);
                }
                
                // Fallback to basic validation if no game name provided
                return true;
            }
            catch
            {
                return false;
            }
        }

        enum PluginValidationResult
        {
            Valid,
            InvalidPath,
            PluginMissing
        }

        PluginValidationResult CheckGamePluginStatus(string gameName, out string statusMessage)
        {
            try
            {
                string gamePath = gameName == "ETS2" ? Settings.Instance.Ets2GamePath : Settings.Instance.AtsGamePath;

                if (string.IsNullOrEmpty(gamePath))
                {
                    statusMessage = "Not configured";
                    return PluginValidationResult.InvalidPath;
                }

                if (gamePath == "N/A")
                {
                    statusMessage = "Installation skipped";
                    return PluginValidationResult.InvalidPath;
                }

                if (!IsValidGamePath(gamePath, gameName))
                {
                    statusMessage = "Invalid directory";
                    return PluginValidationResult.InvalidPath;
                }

                var state = PluginSetup.GetPluginState(gamePath);
                switch (state)
                {
                    case PluginSetup.PluginState.Valid:
                        statusMessage = "Plugin installed";
                        return PluginValidationResult.Valid;

                    case PluginSetup.PluginState.NotInstalled:
                    case PluginSetup.PluginState.Outdated:
                        statusMessage = "Plugin missing or outdated";
                        return PluginValidationResult.PluginMissing;

                    case PluginSetup.PluginState.LocalDllMissing:
                        // InvalidPath (not PluginMissing): we don't want to show the
                        // "Copy plugin" button, because clicking it would re-try File.Copy
                        // from the same missing shipped DLL and fail with a confusing error.
                        statusMessage = "Installation corrupted";
                        return PluginValidationResult.InvalidPath;

                    default:
                        statusMessage = "Unknown state";
                        return PluginValidationResult.InvalidPath;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                statusMessage = "Validation error";
                return PluginValidationResult.InvalidPath;
            }
        }
        
        void ets2CopyPluginButton_Click(object sender, EventArgs e)
        {
            CopyPluginsForGame("ETS2");
        }
        
        void atsCopyPluginButton_Click(object sender, EventArgs e)
        {
            CopyPluginsForGame("ATS");
        }
        
        void CopyPluginsForGame(string gameName)
        {
            try
            {
                string gamePath = gameName == "ETS2" ? Settings.Instance.Ets2GamePath : Settings.Instance.AtsGamePath;
                
                if (string.IsNullOrEmpty(gamePath) || gamePath == "N/A")
                {
                    MessageBox.Show(this, $"Cannot copy plugins: {gameName} path is not configured.", 
                        "Plugin Copy Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                
                if (!IsValidGamePath(gamePath, gameName))
                {
                    MessageBox.Show(this, $"Cannot copy plugins: {gameName} path is invalid.", 
                        "Plugin Copy Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                
                // Source plugin paths come from PluginSetup so we stay in sync with the
                // single source of truth for where the shipped DLLs live.
                string sourceX86Path = PluginSetup.LocalX86PluginPath;
                string sourceX64Path = PluginSetup.LocalX64PluginPath;

                // Define destination plugin paths
                string destX86Path = System.IO.Path.Combine(gamePath, @"bin\win_x86\plugins", PluginSetup.TelemetryDllName);
                string destX64Path = System.IO.Path.Combine(gamePath, @"bin\win_x64\plugins", PluginSetup.TelemetryDllName);
                
                // Ensure destination directories exist
                string destX86Dir = System.IO.Path.GetDirectoryName(destX86Path);
                string destX64Dir = System.IO.Path.GetDirectoryName(destX64Path);
                if (!System.IO.Directory.Exists(destX86Dir))
                    System.IO.Directory.CreateDirectory(destX86Dir);
                if (!System.IO.Directory.Exists(destX64Dir))
                    System.IO.Directory.CreateDirectory(destX64Dir);
                
                // Copy plugin files
                Log.InfoFormat("Copying {1} x86 plugin DLL file to: {0}", destX86Path, gameName);
                System.IO.File.Copy(sourceX86Path, destX86Path, true);
                
                Log.InfoFormat("Copying {1} x64 plugin DLL file to: {0}", destX64Path, gameName);
                System.IO.File.Copy(sourceX64Path, destX64Path, true);
                
                // Show success message and refresh status
                MessageBox.Show(this, $"✓ Successfully copied {gameName} plugins!\n\nPlugins installed to:\n• {destX86Path}\n• {destX64Path}", 
                    "Plugins Copied Successfully", MessageBoxButtons.OK, MessageBoxIcon.Information);
                
                // Refresh the plugin status display
                UpdateGameInfo();
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                MessageBox.Show(this, $"Failed to copy {gameName} plugins:\n\n{ex.Message}", 
                    "Plugin Copy Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

    }
}
