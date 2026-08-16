using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Funbit.Ets.Telemetry.Server.Helpers;

namespace Funbit.Ets.Telemetry.Server.Data
{
    public class GameLogState
    {
        public int Revision { get; set; }
        public string ActiveProfileName { get; set; }
        public string ActiveProfileType { get; set; }
    }

    /// <summary>
    /// Tails the running game's game.log.txt to track the active profile. The game writes the
    /// log continuously and re-emits "New profile selected" both at startup and whenever the
    /// profile or its mod configuration changes, so the log is the single change signal.
    /// Each change bumps a revision counter that clients poll for; the first catch-up reports
    /// one change, not the whole session.
    /// </summary>
    public static class GameLogWatcher
    {
        static readonly log4net.ILog Log = log4net.LogManager.GetLogger(
            MethodBase.GetCurrentMethod().DeclaringType);

        const int PollIntervalMs = 250;

        // Modded games can produce very large logs, so catch up in bounded chunks
        // instead of reading everything the log grew by in one allocation.
        const int MaxReadChunkBytes = 1024 * 1024;

        class State
        {
            public long LastLength;
            public string Remainder = "";
            public int Revision;
            public string ProfileName;
            public string ProfileType;
            public bool Primed;
        }

        class Condition
        {
            public string Signature;
            public int Count;
        }

        static readonly object Lock = new object();
        static readonly Dictionary<string, State> States = new Dictionary<string, State>
        {
            { "ets2", new State() },
            { "ats", new State() },
        };
        // The poll loop runs 4x/sec, so a persistent failure is reported once and then counted.
        static readonly Dictionary<string, Condition> Conditions = new Dictionary<string, Condition>();
        static Thread _thread;

        static bool IsNewCondition(string key, string signature)
        {
            Condition condition;
            if (Conditions.TryGetValue(key, out condition) && condition.Signature == signature)
            {
                condition.Count++;
                return false;
            }
            Conditions[key] = new Condition { Signature = signature };
            return true;
        }

        static int ClearCondition(string key)
        {
            Condition condition;
            if (!Conditions.TryGetValue(key, out condition))
                return -1;
            Conditions.Remove(key);
            return condition.Count;
        }

        public static GameLogState GetState(string game)
        {
            EnsureStarted();
            lock (Lock)
            {
                var state = States[game];
                return new GameLogState
                {
                    Revision = state.Revision,
                    ActiveProfileName = state.ProfileName,
                    ActiveProfileType = state.ProfileType,
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
                _thread = new Thread(PollLoop) { IsBackground = true, Name = "GameLogWatcher" };
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
                        watched = game;
                        if (game != null)
                        {
                            Reset(States[game]);
                            ClearCondition(game + ".read");
                            ClearCondition(game + ".missing");
                            Log.InfoFormat("[{0}] Game detected (exe v{1}); documents root: {2}", game,
                                Ets2ProcessHelper.LastRunningGameProductVersion ?? "unknown",
                                GameProfileScanner.GetDocumentsRoot(game));
                        }
                    }
                    if (watched != null)
                        Poll(watched);
                }
                catch (Exception ex)
                {
                    // Transient share violations are expected while the game replaces the log;
                    // nothing here may ever take down the server process.
                    if (watched != null &&
                        IsNewCondition(watched + ".read", ex.GetType().Name + ex.Message))
                    {
                        Log.WarnFormat("[{0}] Cannot read game.log.txt: {1}: {2}; retrying silently",
                            watched, ex.GetType().Name, ex.Message);
                    }
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

        static void Reset(State state)
        {
            lock (Lock)
            {
                state.LastLength = 0;
                state.Remainder = "";
                state.ProfileName = null;
                state.ProfileType = null;
                state.Primed = false;
            }
        }

        static void Poll(string game)
        {
            string logPath = Path.Combine(GameProfileScanner.GetDocumentsRoot(game), "game.log.txt");
            if (!File.Exists(logPath))
            {
                if (IsNewCondition(game + ".missing", logPath))
                    Log.WarnFormat("[{0}] game.log.txt not found at {1}; profile detection idle",
                        game, logPath);
                return;
            }
            if (ClearCondition(game + ".missing") >= 0)
                Log.InfoFormat("[{0}] game.log.txt found at {1}", game, logPath);

            State state;
            lock (Lock)
                state = States[game];

            using (var stream = File.Open(logPath, FileMode.Open, FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete))
            {
                int suppressed = ClearCondition(game + ".read");
                if (suppressed >= 0)
                    Log.InfoFormat("[{0}] game.log.txt readable again after {1} failed attempts",
                        game, suppressed);

                long length = stream.Length;
                if (length < state.LastLength)
                {
                    // Truncated: the game started a new session and rewrote the log.
                    Log.InfoFormat("[{0}] game.log.txt truncated (new game session); re-reading", game);
                    Reset(state);
                }
                if (length <= state.LastLength)
                    return;

                stream.Seek(state.LastLength, SeekOrigin.Begin);
                var buffer = new byte[Math.Min(length - state.LastLength, MaxReadChunkBytes)];
                int read = stream.Read(buffer, 0, buffer.Length);
                string chunk = state.Remainder + Encoding.UTF8.GetString(buffer, 0, read);

                var lines = chunk.Split('\n');
                var changes = new List<string>();
                bool primedWithoutProfile = false;
                lock (Lock)
                {
                    bool priming = !state.Primed;
                    for (int i = 0; i < lines.Length - 1; i++)
                        ParseLine(state, lines[i].TrimEnd('\r'), priming, changes);
                    state.Remainder = lines[lines.Length - 1];
                    state.LastLength += read;
                    if (priming && state.LastLength >= length)
                    {
                        state.Primed = true;
                        state.Revision++;
                        primedWithoutProfile = state.ProfileName == null;
                        if (!primedWithoutProfile)
                            changes.Add(string.Format(
                                "Session log parsed ({0} bytes): profile '{1}' ({2}), revision {3}",
                                length, state.ProfileName, state.ProfileType, state.Revision));
                    }
                }
                foreach (var change in changes)
                    Log.InfoFormat("[{0}] {1}", game, change);
                if (primedWithoutProfile)
                    Log.WarnFormat("[{0}] Session log parsed ({1} bytes) but no profile markers " +
                                   "found; log format may have changed", game, length);
            }
        }

        static void ParseLine(State state, string line, bool priming, List<string> changes)
        {
            string name = ExtractQuoted(line, "Set profile finished: '");
            if (name != null)
            {
                state.ProfileName = name;
                return;
            }

            int typeIndex = line.IndexOf("Profile type: ", StringComparison.Ordinal);
            if (typeIndex >= 0)
            {
                string rawType = line.Substring(typeIndex + "Profile type: ".Length).Trim();
                state.ProfileType = rawType == "PC_steam_cloud" ? "steam" : "local";
                return;
            }

            name = ExtractQuoted(line, "New profile selected: '");
            if (name != null)
            {
                state.ProfileName = name;
                if (!priming)
                {
                    state.Revision++;
                    changes.Add(string.Format("Profile changed to '{0}' ({1}), revision {2}",
                        name, state.ProfileType, state.Revision));
                }
                return;
            }

            if (!priming && line.EndsWith("Current profile saved.", StringComparison.Ordinal))
            {
                state.Revision++;
                changes.Add(string.Format("Profile saved in game, revision {0}", state.Revision));
            }
        }

        static string ExtractQuoted(string line, string marker)
        {
            int start = line.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
                return null;
            start += marker.Length;
            int end = line.LastIndexOf('\'');
            return end > start ? line.Substring(start, end - start) : null;
        }
    }
}
