using System;
using System.Collections.Generic;
using System.IO;
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

        static readonly object Lock = new object();
        static readonly Dictionary<string, State> States = new Dictionary<string, State>
        {
            { "ets2", new State() },
            { "ats", new State() },
        };
        static Thread _thread;

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
                        watched = game;
                        if (game != null)
                            Reset(States[game]);
                    }
                    if (watched != null)
                        Poll(watched);
                }
                catch (Exception)
                {
                    // Transient share violations are expected while the game replaces the log;
                    // nothing here may ever take down the server process.
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
                return;

            State state;
            lock (Lock)
                state = States[game];

            using (var stream = File.Open(logPath, FileMode.Open, FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete))
            {
                long length = stream.Length;
                if (length < state.LastLength)
                {
                    // Truncated: the game started a new session and rewrote the log.
                    Reset(state);
                }
                if (length <= state.LastLength)
                    return;

                stream.Seek(state.LastLength, SeekOrigin.Begin);
                var buffer = new byte[Math.Min(length - state.LastLength, MaxReadChunkBytes)];
                int read = stream.Read(buffer, 0, buffer.Length);
                string chunk = state.Remainder + Encoding.UTF8.GetString(buffer, 0, read);

                var lines = chunk.Split('\n');
                lock (Lock)
                {
                    bool priming = !state.Primed;
                    for (int i = 0; i < lines.Length - 1; i++)
                        ParseLine(state, lines[i].TrimEnd('\r'), priming);
                    state.Remainder = lines[lines.Length - 1];
                    state.LastLength += read;
                    if (priming && state.LastLength >= length)
                    {
                        state.Primed = true;
                        state.Revision++;
                    }
                }
            }
        }

        static void ParseLine(State state, string line, bool priming)
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
                    state.Revision++;
                return;
            }

            if (!priming && line.EndsWith("Current profile saved.", StringComparison.Ordinal))
                state.Revision++;
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
