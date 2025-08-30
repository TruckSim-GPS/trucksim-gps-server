using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Funbit.Ets.Telemetry.Server.Helpers
{
    public static class Ets2ProcessHelper
    {
        static long _lastCheckTime;
        static bool _cachedRunningFlag;

        /// <summary>
        /// Returns last running game name: "ETS2", "ATS" or null if undefined.
        /// </summary>
        public static string LastRunningGameName { get; set; }
        
        /// <summary>
        /// Returns the installation path of the last detected running game, or null if not available.
        /// </summary>
        public static string LastRunningGamePath { get; set; }

        /// <summary>
        /// Checks whether ETS2 game process is running right now. The maximum check frequency is restricted to 1 second.
        /// </summary>
        /// <returns>True if ETS2 process is run, false otherwise.</returns>
        public static bool IsEts2Running
        {
            get
            {
                if (DateTime.Now - new DateTime(Interlocked.Read(ref _lastCheckTime)) > TimeSpan.FromSeconds(1))
                {
                    Interlocked.Exchange(ref _lastCheckTime, DateTime.Now.Ticks);
                    var processes = Process.GetProcesses();
                    foreach (Process process in processes)
                    {
                        try
                        {
                            bool running = process.MainWindowTitle.StartsWith("Euro Truck Simulator 2") &&
                                           process.ProcessName == "eurotrucks2"
                                           || (process.MainWindowTitle.StartsWith("American Truck Simulator") &&
                                           process.ProcessName == "amtrucks");
                            if (running)
                            {
                                _cachedRunningFlag = true;
                                LastRunningGameName = process.ProcessName == "eurotrucks2" ? "ETS2" : "ATS";
                                
                                // Try to get the game installation path
                                try
                                {
                                    string exePath = process.MainModule.FileName;
                                    string exeDir = Path.GetDirectoryName(exePath);
                                    
                                    // The exe is typically in bin\win_x64 or bin\win_x86, so we need to go up to the game root
                                    // Example: F:\SteamLibrary\steamapps\common\American Truck Simulator\bin\win_x64\amtrucks.exe
                                    // We want: F:\SteamLibrary\steamapps\common\American Truck Simulator
                                    
                                    string gameRoot = null;
                                    DirectoryInfo currentDir = new DirectoryInfo(exeDir);
                                    
                                    // Go up directories until we find one with base.scs and bin folder
                                    while (currentDir != null && currentDir.Parent != null)
                                    {
                                        string testPath = currentDir.FullName;
                                        string baseScsPath = Path.Combine(testPath, "base.scs");
                                        string binPath = Path.Combine(testPath, "bin");
                                        
                                        if (File.Exists(baseScsPath) && Directory.Exists(binPath))
                                        {
                                            gameRoot = testPath;
                                            break;
                                        }
                                        
                                        currentDir = currentDir.Parent;
                                    }
                                    
                                    LastRunningGamePath = gameRoot;
#if DEBUG
                                    Console.WriteLine($"PROCESS DEBUG: Exe path: '{exePath}'");
                                    Console.WriteLine($"PROCESS DEBUG: Game root: '{LastRunningGamePath}'");
#endif
                                }
                                catch (Exception ex)
                                {
#if DEBUG
                                    Console.WriteLine($"PROCESS DEBUG: Failed to get process path: {ex.Message}");
#endif
                                    LastRunningGamePath = null;
                                }
                                
                                return _cachedRunningFlag;
                            }
                        }
                        // ReSharper disable once EmptyGeneralCatchClause
                        catch
                        {
                        }
                    }
                    _cachedRunningFlag = false;
                }
                return _cachedRunningFlag;
            }
        }
    }
}