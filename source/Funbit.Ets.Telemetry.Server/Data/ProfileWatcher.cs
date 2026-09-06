using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Funbit.Ets.Telemetry.Server.Helpers;
using SIIDecryptSharp;

namespace Funbit.Ets.Telemetry.Server.Data
{
    public class ProfileState
    {
        public int Revision { get; set; }
        public string ActiveProfileId { get; set; }
        public string ActiveProfileType { get; set; }
    }

    /// <summary>
    /// Watches the running game's profile folders to track the active profile and its mod
    /// configuration. The game rewrites the selected profile's config_local.cfg whenever a
    /// session starts or ends, and profile.sii whenever the mod list is saved; both are plain
    /// file writes for local and Steam Cloud profiles alike. Each change bumps a revision
    /// counter that clients poll for.
    /// </summary>
    public static class ProfileWatcher
    {
        static readonly log4net.ILog Log = log4net.LogManager.GetLogger(
            MethodBase.GetCurrentMethod().DeclaringType);

        const int PollIntervalMs = 1000;

        class Profile
        {
            public string Id;
            public string Type;
            public string Name;
            public DateTime ConfigWriteTime;
            public string SiiPath;
            public DateTime SiiWriteTime;
            public string Mods;
        }

        class State
        {
            public int Revision;
            public string ActiveId;
            public string ActiveType;
            public Dictionary<string, Profile> Profiles = new Dictionary<string, Profile>();
        }

        static readonly object Lock = new object();
        static readonly Dictionary<string, State> States = new Dictionary<string, State>
        {
            { "ets2", new State() },
            { "ats", new State() },
        };
        static List<string> _steamRoots = new List<string>();
        static string _lastError;
        static Thread _thread;

        public static ProfileState GetState(string game)
        {
            EnsureStarted();
            lock (Lock)
            {
                var state = States[game];
                return new ProfileState
                {
                    Revision = state.Revision,
                    ActiveProfileId = state.ActiveId,
                    ActiveProfileType = state.ActiveType,
                };
            }
        }

        static void EnsureStarted()
        {
            lock (Lock)
            {
                if (_thread != null)
                    return;
                Log.Info("Profile watcher started (first /api/game/state request)");
                _thread = new Thread(PollLoop) { IsBackground = true, Name = "ProfileWatcher" };
                _thread.Start();
            }
        }

        static void PollLoop()
        {
            string watched = null;
            while (true)
            {
                try
                {
                    string game = RunningGame();
                    if (game != watched)
                    {
                        if (watched != null)
                            Log.InfoFormat("[{0}] Game closed; stopped watching", watched);
                        if (game != null)
                        {
                            _steamRoots = GameProfileScanner.GetSteamProfileRoots(game).ToList();
                            Log.InfoFormat("[{0}] Game detected (exe v{1}); documents root: {2}", game,
                                Ets2ProcessHelper.LastRunningGameProductVersion ?? "unknown",
                                GameProfileScanner.GetDocumentsRoot(game));
                        }
                        watched = game;
                    }
                    if (watched != null)
                        Poll(watched);
                    _lastError = null;
                }
                catch (Exception ex)
                {
                    // Transient sharing violations are expected while the game writes;
                    // nothing here may ever take down the server process.
                    string error = ex.GetType().Name + ": " + ex.Message;
                    if (error != _lastError)
                        Log.WarnFormat("[{0}] Profile scan failed: {1}; retrying silently", watched, error);
                    _lastError = error;
                }
                Thread.Sleep(PollIntervalMs);
            }
        }

        static string RunningGame()
        {
            if (!Ets2ProcessHelper.IsEts2Running)
                return null;
            return string.Equals(Ets2ProcessHelper.LastRunningGameName, "ATS",
                StringComparison.OrdinalIgnoreCase) ? "ats" : "ets2";
        }

        static void Poll(string game)
        {
            var found = Scan(game);
            Dictionary<string, Profile> known;
            lock (Lock)
                known = new Dictionary<string, Profile>(States[game].Profiles);

            // A root that is briefly unreadable must not read as every profile deleted.
            if (found.Count == 0 && known.Count > 0)
                return;

            var changes = new List<string>();
            foreach (var pair in found)
            {
                Profile old;
                if (!known.TryGetValue(pair.Key, out old))
                {
                    pair.Value.Mods = ReadMods(pair.Value.SiiPath);
                    if (pair.Value.Mods == null)
                        pair.Value.SiiWriteTime = DateTime.MinValue;
                    if (known.Count > 0)
                        changes.Add(string.Format("Profile '{0}' added", pair.Value.Name));
                    continue;
                }
                pair.Value.Mods = old.Mods;
                if (pair.Value.SiiWriteTime == old.SiiWriteTime)
                    continue;
                string mods = ReadMods(pair.Value.SiiPath);
                if (mods == null)
                {
                    pair.Value.SiiWriteTime = old.SiiWriteTime;
                    continue;
                }
                pair.Value.Mods = mods;
                if (mods != old.Mods)
                    changes.Add(string.Format("Profile '{0}' mods changed", pair.Value.Name));
            }
            foreach (var pair in known.Where(k => !found.ContainsKey(k.Key)))
                changes.Add(string.Format("Profile '{0}' removed", pair.Value.Name));
            if (known.Count == 0 && found.Count > 0)
                changes.Add(string.Format("Profiles found: {0}", found.Count));

            var active = found.Values.OrderByDescending(p => p.ConfigWriteTime).FirstOrDefault();
            int revision;
            lock (Lock)
            {
                var state = States[game];
                if (active?.Id != state.ActiveId)
                {
                    state.ActiveId = active?.Id;
                    state.ActiveType = active?.Type;
                    changes.Add(string.Format("Active profile '{0}' ({1})", active?.Name, active?.Type));
                }
                state.Profiles = found;
                if (changes.Count > 0)
                    state.Revision++;
                revision = state.Revision;
            }
            foreach (var change in changes)
                Log.InfoFormat("[{0}] {1}, revision {2}", game, change, revision);
        }

        // config_local.cfg lives under Documents for both profile types; profile.sii under
        // Documents for local profiles and under Steam userdata for Steam Cloud ones.
        static Dictionary<string, Profile> Scan(string game)
        {
            var result = new Dictionary<string, Profile>();
            string documents = GameProfileScanner.GetDocumentsRoot(game);
            AddProfiles(result, Path.Combine(documents, "profiles"), "local",
                id => Path.Combine(documents, "profiles", id, "profile.sii"));
            AddProfiles(result, Path.Combine(documents, "steam_profiles"), "steam",
                id => _steamRoots.Select(r => Path.Combine(r, id, "profile.sii")).FirstOrDefault(File.Exists));
            return result;
        }

        static void AddProfiles(Dictionary<string, Profile> result, string root, string type,
            Func<string, string> siiPath)
        {
            if (!Directory.Exists(root))
                return;
            foreach (var dir in Directory.GetDirectories(root))
            {
                string id = Path.GetFileName(dir);
                string name = GameProfileScanner.TryDecodeHexName(id);
                string config = Path.Combine(dir, "config_local.cfg");
                if (name == null || !File.Exists(config))
                    continue;
                string sii = siiPath(id);
                if (sii != null && !File.Exists(sii))
                    sii = null;
                result[type + "/" + id] = new Profile
                {
                    Id = id,
                    Type = type,
                    Name = name,
                    ConfigWriteTime = File.GetLastWriteTimeUtc(config),
                    SiiPath = sii,
                    SiiWriteTime = sii == null ? DateTime.MinValue : File.GetLastWriteTimeUtc(sii),
                };
            }
        }

        static string ReadMods(string siiPath)
        {
            if (siiPath == null)
                return "";
            try
            {
                string text = Encoding.UTF8.GetString(Decryptor.Decrypt(siiPath));
                return string.Join("\n", GameProfileScanner.ActiveModRegex.Matches(text)
                    .Cast<Match>().Select(m => m.Value));
            }
            catch (Exception ex)
            {
                Log.Warn($"Could not read '{siiPath}'", ex);
                return null;
            }
        }
    }
}
