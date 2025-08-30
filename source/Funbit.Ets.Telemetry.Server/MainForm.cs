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
using Microsoft.Owin.Hosting;

namespace Funbit.Ets.Telemetry.Server
{
    public partial class MainForm : Form
    {
        IDisposable _server;
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
                // load list of available network interfaces
                var networkInterfaces = NetworkHelper.GetAllActiveNetworkInterfaces();
                interfacesDropDown.Items.Clear();
                foreach (var networkInterface in networkInterfaces)
                    interfacesDropDown.Items.Add(networkInterface);
                // select remembered interface or default
                var rememberedInterface = networkInterfaces.FirstOrDefault(
                    i => i.Id == Settings.Instance.DefaultNetworkInterfaceId);
                if (rememberedInterface != null)
                    interfacesDropDown.SelectedItem = rememberedInterface;
                else
                    interfacesDropDown.SelectedIndex = 0; // select default interface

                // bind to all available interfaces
                _server = WebApp.Start<Startup>(IpToEndpointUrl("+"));

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
            _server?.Dispose();
            trayIcon.Visible = false;
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
            // Update menu item availability based on game state
            rerunSetupToolStripMenuItem.Enabled = !Ets2ProcessHelper.IsEts2Running;
            
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
        }
        
        string GetSimplePluginText(PluginValidationResult result, string baseMessage)
        {
            switch (result)
            {
                case PluginValidationResult.Valid:
                    return "✓ Plugin OK";
                    
                case PluginValidationResult.PluginMissing:
                    return "⚠ Plugin missing (Server > Re-run Setup)";
                    
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
                Console.WriteLine($"AUTO-CORRECT DEBUG: UseTestTelemetryData={UseTestTelemetryData}, IsEts2Running={Ets2ProcessHelper.IsEts2Running}, IsConnected={ScsTelemetryDataReader.Instance.IsConnected}");
                
                // Only try auto-correction if game is running (connected or not - we'll try both cases)
                if (UseTestTelemetryData || !Ets2ProcessHelper.IsEts2Running)
                {
                    Console.WriteLine("AUTO-CORRECT: Skipped - test mode enabled or game not running");
                    return;
                }
                    
                string runningGame = Ets2ProcessHelper.LastRunningGameName;
                string detectedPath = Ets2ProcessHelper.LastRunningGamePath;
                
                Console.WriteLine($"AUTO-CORRECT DEBUG: RunningGame='{runningGame}', DetectedPath='{detectedPath}'");
                
                // Need both game name and detected path
                if (string.IsNullOrEmpty(runningGame) || string.IsNullOrEmpty(detectedPath))
                {
                    Console.WriteLine("AUTO-CORRECT: Skipped - missing game name or detected path");
                    return;
                }
                    
                // Get current stored path for the running game
                string currentStoredPath = runningGame == "ETS2" ? Settings.Instance.Ets2GamePath : Settings.Instance.AtsGamePath;
                
                Console.WriteLine($"AUTO-CORRECT DEBUG: CurrentStoredPath='{currentStoredPath}', IsInvalid={IsGamePathInvalid(currentStoredPath)}");
                
                // Only auto-correct if current stored path is invalid and detected path is different
                if (!IsGamePathInvalid(currentStoredPath) || string.Equals(currentStoredPath, detectedPath, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("AUTO-CORRECT: Skipped - current path is valid or same as detected");
                    return;
                }
                    
                // Validate that detected path is actually a valid game installation
                if (!IsValidGamePath(detectedPath))
                {
                    Console.WriteLine($"AUTO-CORRECT: Skipped - detected path '{detectedPath}' is not a valid game installation");
                    return;
                }
                    
                // Update the stored path
                Console.WriteLine($"AUTO-CORRECT: Updating {runningGame} path from '{currentStoredPath}' to '{detectedPath}'");
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
                Console.WriteLine($"AUTO-CORRECT: Path update completed, display refreshed");
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
        
        bool IsValidGamePath(string gamePath)
        {
            if (string.IsNullOrEmpty(gamePath))
                return false;
                
            try
            {
                var baseScsPath = System.IO.Path.Combine(gamePath, "base.scs");
                var binPath = System.IO.Path.Combine(gamePath, "bin");
                return System.IO.File.Exists(baseScsPath) && System.IO.Directory.Exists(binPath);
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
                
                // Use the same validation logic as PluginSetup
                var baseScsPath = System.IO.Path.Combine(gamePath, "base.scs");
                var binPath = System.IO.Path.Combine(gamePath, "bin");
                bool pathValid = System.IO.File.Exists(baseScsPath) && System.IO.Directory.Exists(binPath);
                
                if (!pathValid)
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
                
                Console.WriteLine($"PLUGIN DEBUG: {gameName} x64 MD5: expected='{TelemetryX64DllMd5}', actual='{x64Md5}'");
                Console.WriteLine($"PLUGIN DEBUG: {gameName} x86 MD5: expected='{TelemetryX86DllMd5}', actual='{x86Md5}'");
                
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

    }
}
