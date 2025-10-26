using System;
using System.Configuration;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using Funbit.Ets.Telemetry.Server.Controllers;
using Funbit.Ets.Telemetry.Server.Data;
using Funbit.Ets.Telemetry.Server.Helpers;
using Funbit.Ets.Telemetry.Server.Setup;

namespace Funbit.Ets.Telemetry.Server
{
    public partial class MainForm : Form
    {
        MinimalHttpServer _server;
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
            Log.InfoFormat("Running application on {0} ({1}) {2}", Environment.OSVersion,
                Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit",
                Program.UninstallMode ? "[UNINSTALL MODE]" : "");
            Text += @" " + AssemblyHelper.Version;

            // install or uninstall server if needed
            Setup();

            // start WebApi server
            Start();
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
    
        void closeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }

        void trayIcon_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            WindowState = FormWindowState.Normal;
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
            appUrlLabel.Text = IpToEndpointUrl(selectedInterface.Ip) + Ets2AppController.TelemetryAppUriPath;
            ipAddressLabel.Text = selectedInterface.Ip;
            Settings.Instance.DefaultNetworkInterfaceId = selectedInterface.Id;
            Settings.Instance.Save();
        }

        async void broadcastTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                broadcastTimer.Enabled = false;
                await _broadcastHttpClient.PostAsJsonAsync(BroadcastUrl, ScsTelemetryDataReader.Instance.Read());
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
            broadcastTimer.Enabled = true;
        }
        
        void uninstallToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // Make sure that game is not running during uninstall
            if (Ets2ProcessHelper.IsEts2Running)
            {
                MessageBox.Show(this,
                    @"In order to proceed the ETS2/ATS game must not be running." + Environment.NewLine +
                    @"Please exit the game and try again.", @"Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string exeFileName = Process.GetCurrentProcess().MainModule.FileName;
            var startInfo = new ProcessStartInfo
            {
                Arguments = $"/C ping 127.0.0.1 -n 2 && \"{exeFileName}\" -uninstall",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
                FileName = "cmd.exe"
            };
            Process.Start(startInfo);
            Application.Exit();
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

        void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // TODO: implement later
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
                
                // Use the same enhanced validation logic as PluginSetup
                if (!IsValidGamePath(gamePath, gameName))
                {
                    statusMessage = "Invalid directory";
                    return PluginValidationResult.InvalidPath;
                }
                
                // Use the same MD5 validation as PluginSetup.GameState.IsPluginValid()
                const string TelemetryX64DllMd5 = "90bfd9519f9251afdf4ff131839efbd9";
                const string TelemetryX86DllMd5 = "1f94471a3698a372064f73e6168d6711";
                
                string x64DllPath = System.IO.Path.Combine(gamePath, @"bin\win_x64\plugins\trucksim-gps-telemetry.dll");
                string x86DllPath = System.IO.Path.Combine(gamePath, @"bin\win_x86\plugins\trucksim-gps-telemetry.dll");
                
                string x64Md5 = ComputeMd5(x64DllPath);
                string x86Md5 = ComputeMd5(x86DllPath);
                
#if DEBUG
                Console.WriteLine($"PLUGIN DEBUG: {gameName} x64 MD5: expected='{TelemetryX64DllMd5}', actual='{x64Md5}'");
                Console.WriteLine($"PLUGIN DEBUG: {gameName} x86 MD5: expected='{TelemetryX86DllMd5}', actual='{x86Md5}'");
#endif
                
                if (x64Md5 != TelemetryX64DllMd5 || x86Md5 != TelemetryX86DllMd5)
                {
                    statusMessage = "Plugin missing or outdated";
                    return PluginValidationResult.PluginMissing;
                }
                
                statusMessage = "Plugin installed";
                return PluginValidationResult.Valid;
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                statusMessage = "Validation error";
                return PluginValidationResult.InvalidPath;
            }
        }
        
        string ComputeMd5(string fileName)
        {
            if (!System.IO.File.Exists(fileName))
                return null;
                
            try
            {
                using (var provider = new MD5CryptoServiceProvider())
                {
                    var bytes = System.IO.File.ReadAllBytes(fileName);
                    var hash = provider.ComputeHash(bytes);
                    var result = string.Concat(hash.Select(b => $"{b:x02}"));
                    return result;
                }
            }
            catch
            {
                return null;
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
                
                // Define source plugin paths (from telemetry server installation)
                const string TelemetryDllName = "trucksim-gps-telemetry.dll";
                string sourceX86Path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"TruckSimGPSPlugins\win_x86\plugins", TelemetryDllName);
                string sourceX64Path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"TruckSimGPSPlugins\win_x64\plugins", TelemetryDllName);
                
                // Define destination plugin paths
                string destX86Path = System.IO.Path.Combine(gamePath, @"bin\win_x86\plugins", TelemetryDllName);
                string destX64Path = System.IO.Path.Combine(gamePath, @"bin\win_x64\plugins", TelemetryDllName);
                
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
