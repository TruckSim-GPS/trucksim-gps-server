using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Forms;
using Funbit.Ets.Telemetry.Server.Helpers;
using Microsoft.Win32;

namespace Funbit.Ets.Telemetry.Server.Setup
{
    public class PluginSetup : ISetup
    {
        static readonly log4net.ILog Log = log4net.LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        internal const string TelemetryX64DllMd5 = "9f58d5699e3e6b268d9a731996f07312";
        internal const string TelemetryX86DllMd5 = "f640a4aa4a484aa29264d716c44ee589";

        internal static string ComputeMd5(string fileName)
        {
            if (!File.Exists(fileName))
                return null;
            using (var provider = new MD5CryptoServiceProvider())
            {
                var bytes = File.ReadAllBytes(fileName);
                var hash = provider.ComputeHash(bytes);
                return string.Concat(hash.Select(b => $"{b:x02}"));
            }
        }

        const string Ets2 = "ETS2";
        const string Ats = "ATS";
        SetupStatus _status;
        
        public PluginSetup()
        {
            try
            {
                Log.Info("Checking plugin DLL files...");
                
                var ets2State = new GameState(Ets2, Settings.Instance.Ets2GamePath);
                var atsState = new GameState(Ats, Settings.Instance.AtsGamePath);

                if (Program.ForceSetupMode)
                {
                    // Force setup dialogs when manually triggered
                    _status = SetupStatus.Uninstalled;
                }
                else if (ets2State.IsPluginValid() && atsState.IsPluginValid())
                {
                    _status = SetupStatus.Installed;
                }
                else
                {
                    _status = SetupStatus.Uninstalled;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                _status = SetupStatus.Failed;
            }
        }

        public SetupStatus Status => _status;

        public SetupStatus Install(IWin32Window owner)
        {
            try
            {
                var ets2State = new GameState(Ets2, Settings.Instance.Ets2GamePath);
                var atsState = new GameState(Ats, Settings.Instance.AtsGamePath);

                // Always process ETS2 - show confirmation dialog if valid, or detection dialog if invalid
                ets2State.ConfigureGamePath(owner);
                if (ets2State.IsPathValid() && ets2State.GamePath != "N/A")
                    ets2State.InstallPlugin();

                // Always process ATS - show confirmation dialog if valid, or detection dialog if invalid
                atsState.ConfigureGamePath(owner);
                if (atsState.IsPathValid() && atsState.GamePath != "N/A")
                    atsState.InstallPlugin();
                
                // Save the final paths (might have changed during configuration)
                Settings.Instance.Ets2GamePath = ets2State.GamePath;
                Settings.Instance.AtsGamePath = atsState.GamePath;
                Settings.Instance.Save();
                
                // Set final status based on whether both games are properly configured
                if (ets2State.IsPluginValid() && atsState.IsPluginValid())
                    _status = SetupStatus.Installed;
                else
                    _status = SetupStatus.Uninstalled;
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                _status = SetupStatus.Failed;
                throw;
            }
            
            return _status;
        }

        public SetupStatus Uninstall(IWin32Window owner)
        {
            if (_status == SetupStatus.Uninstalled)
                return _status;

            SetupStatus status;
            try
            {
                var ets2State = new GameState(Ets2, Settings.Instance.Ets2GamePath);
                var atsState = new GameState(Ats, Settings.Instance.AtsGamePath);
                ets2State.UninstallPlugin();
                atsState.UninstallPlugin();
                status = SetupStatus.Uninstalled;
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                status = SetupStatus.Failed;
            }
            return status;
        }
        
        class GameState
        {
            const string InstallationSkippedPath = "N/A";
            const string TelemetryDllName = "trucksim-gps-telemetry.dll";

            readonly string _gameName;

            public GameState(string gameName, string gamePath)
            {
                _gameName = gameName;
                GamePath = gamePath;
            }

            string GameDirectoryName
            {
                get
                {
                    string fullName = "Euro Truck Simulator 2";
                    if (_gameName == Ats)
                        fullName = "American Truck Simulator";
                    return fullName;
                }
            }

            public string GamePath { get; private set; }

            public bool IsPathValid()
            {
                if (GamePath == InstallationSkippedPath)
                    return true;

                return IsValidGameInstallation(GamePath);
            }

            public bool IsPluginValid()
            {
                if (GamePath == InstallationSkippedPath)
                    return true;

                if (!IsPathValid())
                    return false;

                return Md5(GetTelemetryPluginDllFileName(GamePath, x64: true)) == TelemetryX64DllMd5 &&
                    Md5(GetTelemetryPluginDllFileName(GamePath, x64: false)) == TelemetryX86DllMd5;
            }
            
            bool IsValidGameInstallation(string gamePath)
            {
                if (string.IsNullOrEmpty(gamePath))
                    return false;

                // Check for base.scs file (game data archive)
                var baseScsPath = Path.Combine(gamePath, "base.scs");
                if (!File.Exists(baseScsPath))
                    return false;

                // Check for bin directory
                var binPath = Path.Combine(gamePath, "bin");
                if (!Directory.Exists(binPath))
                    return false;

                // Check for the actual game executable - this is the key validation that prevents orphan folder detection
                string gameExeName = _gameName == "ETS2" ? "eurotrucks2.exe" : "amtrucks.exe";
                var gameExePath = Path.Combine(gamePath, "bin", "win_x64", gameExeName);
                
                bool isValid = File.Exists(gameExePath);
                Log.InfoFormat("Validating {2} installation: '{0}' ... {1}", gamePath, isValid ? "OK" : "Fail", _gameName);
                
                return isValid;
            }

            public void InstallPlugin()
            {
                if (GamePath == InstallationSkippedPath)
                    return;

                string x64DllFileName = GetTelemetryPluginDllFileName(GamePath, x64: true);
                string x86DllFileName = GetTelemetryPluginDllFileName(GamePath, x64: false);

                Log.InfoFormat("Copying {1} x86 plugin DLL file to: {0}", x86DllFileName, _gameName);
                File.Copy(LocalEts2X86TelemetryPluginDllFileName, x86DllFileName, true);

                Log.InfoFormat("Copying {1} x64 plugin DLL file to: {0}", x64DllFileName, _gameName);
                File.Copy(LocalEts2X64TelemetryPluginDllFileName, x64DllFileName, true);
            }

            public void UninstallPlugin()
            {
                if (GamePath == InstallationSkippedPath)
                    return;

                Log.InfoFormat("Backing up plugin DLL files for {0}...", _gameName);
                string x64DllFileName = GetTelemetryPluginDllFileName(GamePath, x64: true);
                string x86DllFileName = GetTelemetryPluginDllFileName(GamePath, x64: false);
                string x86BakFileName = Path.ChangeExtension(x86DllFileName, ".bak");
                string x64BakFileName = Path.ChangeExtension(x64DllFileName, ".bak");
                if (File.Exists(x86BakFileName))
                    File.Delete(x86BakFileName);
                if (File.Exists(x64BakFileName))
                    File.Delete(x64BakFileName);
                File.Move(x86DllFileName, x86BakFileName);
                File.Move(x64DllFileName, x64BakFileName);
            }

            static List<string> GetSteamLibraryPaths()
            {
                var libraryPaths = new List<string>();
                
                try
                {
                    // Get Steam installation path from registry
                    var steamPath = GetDefaultSteamPath();
                    if (string.IsNullOrEmpty(steamPath))
                        return libraryPaths;
                    
                    steamPath = steamPath.Replace('/', '\\');
                    
                    // Try both VDF file locations (new location first, then old)
                    string[] vdfPaths = {
                        Path.Combine(steamPath, "config", "libraryfolders.vdf"),
                        Path.Combine(steamPath, "steamapps", "libraryfolders.vdf")
                    };
                    
                    foreach (var vdfPath in vdfPaths)
                    {
                        if (File.Exists(vdfPath))
                        {
                            var parsedPaths = ParseLibraryFoldersVdf(vdfPath);
                            libraryPaths.AddRange(parsedPaths);
                            break; // Use first valid VDF file found
                        }
                    }
                    
                    // Add default Steam path as fallback (always include main Steam library)
                    if (!string.IsNullOrEmpty(steamPath))
                    {
                        libraryPaths.Add(steamPath);
                    }
                    
                    // Remove duplicates and return
                    return libraryPaths.Distinct().ToList();
                }
                catch (Exception ex)
                {
                    // Log error but don't crash - fallback to default detection
                    Log.WarnFormat("Failed to parse Steam library folders: {0}", ex.Message);
                    
                    // Return at least the default Steam path if available
                    var defaultPath = GetDefaultSteamPath();
                    if (!string.IsNullOrEmpty(defaultPath))
                        libraryPaths.Add(defaultPath.Replace('/', '\\'));
                    
                    return libraryPaths;
                }
            }
            
            static List<string> ParseLibraryFoldersVdf(string vdfFilePath)
            {
                var paths = new List<string>();
                
                try
                {
                    var vdfContent = File.ReadAllText(vdfFilePath);
                    
                    // Simple regex to extract paths - matches: "path" "C:\\Program Files (x86)\\Steam"
                    // This handles escaped backslashes and various whitespace patterns
                    var pathRegex = new System.Text.RegularExpressions.Regex(
                        @"""path""\s*""([^""]+)""", 
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    
                    var matches = pathRegex.Matches(vdfContent);
                    
                    foreach (System.Text.RegularExpressions.Match match in matches)
                    {
                        if (match.Success && match.Groups.Count > 1)
                        {
                            var path = match.Groups[1].Value;
                            // Unescape backslashes (VDF uses \\ for \)
                            path = path.Replace(@"\\", @"\");
                            
                            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                            {
                                paths.Add(path);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.WarnFormat("Failed to parse VDF file '{0}': {1}", vdfFilePath, ex.Message);
                }
                
                return paths;
            }

            static string GetDefaultSteamPath()
            {
                var steamKey = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                return steamKey?.GetValue("SteamPath") as string;
            }

            static string LocalEts2X86TelemetryPluginDllFileName => Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, @"TruckSimGPSPlugins\win_x86\plugins", TelemetryDllName);

            static string LocalEts2X64TelemetryPluginDllFileName => Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, @"TruckSimGPSPlugins\win_x64\plugins", TelemetryDllName);

            static string FindLocalTelemetryPluginDll(bool x64)
            {
                var arch = x64 ? "win_x64" : "win_x86";
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TruckSimGPSPlugins", arch, "plugins", TelemetryDllName);
            }
            
            static string GetPluginPath(string gamePath, bool x64)
            {
                return Path.Combine(gamePath, x64 ? @"bin\win_x64\plugins" : @"bin\win_x86\plugins");
            }

            static string GetTelemetryPluginDllFileName(string gamePath, bool x64)
            {
                string path = GetPluginPath(gamePath, x64);
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
                return Path.Combine(path, TelemetryDllName);
            }
            
            static string Md5(string fileName) => ComputeMd5(fileName);

            public void DetectPath()
            {
                // Try to find the game in all Steam library locations
                var steamLibraryPaths = GetSteamLibraryPaths();
                
                foreach (var libraryPath in steamLibraryPaths)
                {
                    var potentialGamePath = Path.Combine(libraryPath, "steamapps", "common", GameDirectoryName);
                    if (Directory.Exists(potentialGamePath))
                    {
                        // Check if this looks like a valid installation
                        if (IsValidGameInstallation(potentialGamePath))
                        {
                            GamePath = potentialGamePath;
                            return;
                        }
                    }
                }
            }

            public void ConfigureGamePath(IWin32Window owner)
            {
                // If path is already valid and not skipped, ask user if they want to keep or change it
                if (IsPathValid() && GamePath != InstallationSkippedPath && !string.IsNullOrEmpty(GamePath))
                {
                    var gameFullName = _gameName == "ETS2" ? "Euro Truck Simulator 2" : "American Truck Simulator";
                    
                    var confirmResult = MessageBox.Show(owner,
                        $">>> {gameFullName} <<<" + Environment.NewLine +
                        "Installation is currently configured at:" + Environment.NewLine + Environment.NewLine +
                        GamePath + Environment.NewLine + Environment.NewLine +
                        @"Do you want to keep using this installation path?" + Environment.NewLine + Environment.NewLine +
                        @"[YES]    = Keep this path and continue" + Environment.NewLine +
                        @"[NO]     = Browse for a different location",
                        $"{gameFullName} Setup", MessageBoxButtons.YesNo, MessageBoxIcon.Information,
                        MessageBoxDefaultButton.Button1);
                    
                    if (confirmResult == DialogResult.Yes)
                    {
                        // User wants to keep the current path
                        return;
                    }
                    // User wants to change the path - clear current path first, then browse
                    GamePath = null; // Clear current path so browse logic works
                    BrowseForPath(owner);
                    return;
                }
                
                // Path is invalid, null, or user wants to change it - try auto-detection first
                DetectPath();
                if (!IsPathValid())
                    ShowDetectionFailedAndBrowse(owner);
            }
            
            void ShowDetectionFailedAndBrowse(IWin32Window owner)
            {
                var gameFullName = _gameName == "ETS2" ? "Euro Truck Simulator 2" : "American Truck Simulator";
                
                var detectionResult = MessageBox.Show(owner,
                    $">>> {gameFullName} <<<" + Environment.NewLine +
                    "Installation could not be automatically detected." + Environment.NewLine + Environment.NewLine +
                    $"Do you want to manually locate your {gameFullName} installation?" + Environment.NewLine + Environment.NewLine +
                    @"[YES]    = Browse to find installation folder" + Environment.NewLine +
                    @"[NO]     = Skip this game (you can configure it later)",
                    $"{gameFullName} Setup", MessageBoxButtons.YesNo, MessageBoxIcon.Information,
                    MessageBoxDefaultButton.Button1);
                    
                if (detectionResult == DialogResult.No)
                {
                    GamePath = InstallationSkippedPath;
                    return;
                }
                
                BrowseForPath(owner);
            }
            
            void BrowseForPath(IWin32Window owner)
            {
                var gameFullName = _gameName == "ETS2" ? "Euro Truck Simulator 2" : "American Truck Simulator";
                
                while (!IsPathValid())
                {
                    var browser = new FolderBrowserDialog();
                    browser.Description = $"Select {gameFullName} installation folder";
                    browser.ShowNewFolderButton = false;
                    var result = browser.ShowDialog(owner);
                    
                    if (result == DialogResult.Cancel)
                    {
                        GamePath = InstallationSkippedPath;
                        return;
                    }
                    
                    GamePath = browser.SelectedPath;
                    
                    if (!IsPathValid())
                    {
                        string executableName = _gameName == "ETS2" ? "eurotrucks2.exe" : "amtrucks.exe";
                        MessageBox.Show(owner,
                            $">>> {gameFullName} <<<" + Environment.NewLine +
                            "The selected folder does not appear to be a valid installation." + Environment.NewLine + Environment.NewLine +
                            $"Please select the main {gameFullName} folder that contains 'base.scs' and 'bin' folder.",
                            "Invalid Path", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                    }
                }
            }
            
        }
    }
}
