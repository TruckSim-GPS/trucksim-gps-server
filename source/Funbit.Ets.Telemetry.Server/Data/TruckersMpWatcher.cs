using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Funbit.Ets.Telemetry.Server.Data
{
    public class TruckersMpState
    {
        public bool Active { get; set; }
        public string ClientVersion { get; set; }
        public List<string> MountedFiles { get; set; }
    }

    /// <summary>
    /// Detects a TruckersMP session from the client DLL injected into the game process and
    /// tracks the mod files TruckersMP mounts from the game's mod folder (its own mod manager
    /// bypasses the profile's mod list). Mounts are read from game.log.txt, which stays small in
    /// a TruckersMP session because profile mods are never loaded there.
    /// </summary>
    public static class TruckersMpWatcher
    {
        static readonly log4net.ILog Log = log4net.LogManager.GetLogger(
            MethodBase.GetCurrentMethod().DeclaringType);

        const int ModuleProbeSeconds = 60;
        static readonly Regex MountRegex = new Regex(
            @"\[fs\] device (.+?) mounted to mod pool\.$", RegexOptions.Compiled);

        static readonly object Lock = new object();
        static string _game;
        static int _pid;
        static DateTime _probeUntil;
        static bool _active;
        static string _version;
        static readonly List<string> _mounted = new List<string>();
        static long _logOffset;
        static byte[] _remainder = new byte[0];

        public static TruckersMpState GetState(string game)
        {
            lock (Lock)
            {
                bool active = _active && _game == game;
                return new TruckersMpState
                {
                    Active = active,
                    ClientVersion = active ? _version : null,
                    MountedFiles = active ? _mounted.ToList() : null,
                };
            }
        }

        /// <summary>Called once per watcher sweep; true when the reported state changed.</summary>
        internal static bool Poll(string game)
        {
            try
            {
                var processes = Process.GetProcessesByName(game == "ats" ? "amtrucks" : "eurotrucks2");
                if (processes.Length == 0)
                    return Reset(null, 0);
                bool changed;
                using (var process = processes[0])
                {
                    changed = process.Id != _pid && Reset(game, process.Id);
                    if (!_active)
                    {
                        if (DateTime.Now > _probeUntil)
                            return changed;
                        var module = FindClientModule(process);
                        if (module == null)
                            return changed;
                        lock (Lock)
                        {
                            _active = true;
                            _version = module.FileVersionInfo.FileVersion;
                        }
                        Log.InfoFormat("[{0}] TruckersMP client {1} detected", game, _version);
                        changed = true;
                    }
                }
                return ReadMounts(game) || changed;
            }
            catch (Exception ex)
            {
                // An elevated game hides its process details; the profile sweep must go on.
                Log.Debug("TruckersMP probe failed", ex);
                return false;
            }
        }

        // The client DLL is injected at launch, so probing stops a minute after first sight.
        static bool Reset(string game, int pid)
        {
            lock (Lock)
            {
                bool changed = _active;
                _game = game;
                _pid = pid;
                _probeUntil = DateTime.Now.AddSeconds(ModuleProbeSeconds);
                _active = false;
                _version = null;
                _mounted.Clear();
                _logOffset = 0;
                _remainder = new byte[0];
                return changed;
            }
        }

        static ProcessModule FindClientModule(Process process)
        {
            try
            {
                return process.Modules.Cast<ProcessModule>().FirstOrDefault(m =>
                    m.ModuleName.Equals("core_ets2mp.dll", StringComparison.OrdinalIgnoreCase) ||
                    m.ModuleName.Equals("core_atsmp.dll", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception)
            {
                // Module list is unavailable while the process is still starting.
                return null;
            }
        }

        static bool ReadMounts(string game)
        {
            string logPath = Path.Combine(GameProfileScanner.GetDocumentsRoot(game), "game.log.txt");
            string modRoot = Path.Combine(GameProfileScanner.GetDocumentsRoot(game), "mod") + Path.DirectorySeparatorChar;
            if (!File.Exists(logPath))
                return false;

            byte[] buffer;
            using (var stream = File.Open(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (stream.Length < _logOffset)
                    _logOffset = 0;
                if (stream.Length == _logOffset)
                    return false;
                stream.Seek(_logOffset, SeekOrigin.Begin);
                buffer = new byte[_remainder.Length + stream.Length - _logOffset];
                Array.Copy(_remainder, buffer, _remainder.Length);
                int read = stream.Read(buffer, _remainder.Length, buffer.Length - _remainder.Length);
                _logOffset += read;
                Array.Resize(ref buffer, _remainder.Length + read);
            }

            // Decode complete lines only; a partial line stays as bytes for the next read.
            int end = Array.LastIndexOf(buffer, (byte)'\n') + 1;
            _remainder = buffer.Skip(end).ToArray();
            var lines = Encoding.UTF8.GetString(buffer, 0, end).Split('\n');
            bool changed = false;
            lock (Lock)
            {
                foreach (var line in lines)
                {
                    var match = MountRegex.Match(line.TrimEnd('\r'));
                    if (!match.Success)
                        continue;
                    string path = match.Groups[1].Value.Replace('/', '\\');
                    if (!path.StartsWith(modRoot, StringComparison.OrdinalIgnoreCase))
                        continue;
                    string file = Path.GetFileName(path);
                    if (_mounted.Contains(file))
                        continue;
                    _mounted.Add(file);
                    changed = true;
                }
            }
            if (changed)
                Log.InfoFormat("[{0}] TruckersMP mounted {1} mod files from the mod folder", game, _mounted.Count);
            return changed;
        }
    }
}
