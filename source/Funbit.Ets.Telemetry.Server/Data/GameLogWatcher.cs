using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace Funbit.Ets.Telemetry.Server.Data
{
    public class GameLogState
    {
        public int Revision { get; set; }
        public string ActiveProfileName { get; set; }
        public string ActiveProfileType { get; set; }
    }

    /// <summary>
    /// Tails the game's game.log.txt to track the active profile. The game writes the log
    /// continuously and re-emits "New profile selected" both at startup and whenever the
    /// profile or its mod configuration changes, so the log is the single change signal.
    /// Each change bumps a revision counter that clients poll for.
    /// </summary>
    public static class GameLogWatcher
    {
        const int PollIntervalMs = 250;

        class State
        {
            public long LastLength;
            public string Remainder = "";
            public int Revision;
            public string ProfileName;
            public string ProfileType;
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
            while (true)
            {
                foreach (var game in States.Keys)
                {
                    try
                    {
                        Poll(game);
                    }
                    catch (Exception)
                    {
                        // Transient share violations are expected while the game replaces the log;
                        // nothing here may ever take down the server process.
                    }
                }
                Thread.Sleep(PollIntervalMs);
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
                    lock (Lock)
                    {
                        state.LastLength = 0;
                        state.Remainder = "";
                        state.ProfileName = null;
                        state.ProfileType = null;
                        state.Revision++;
                    }
                }
                if (length == state.LastLength)
                    return;

                stream.Seek(state.LastLength, SeekOrigin.Begin);
                var buffer = new byte[length - state.LastLength];
                int read = stream.Read(buffer, 0, buffer.Length);
                string chunk = state.Remainder + Encoding.UTF8.GetString(buffer, 0, read);

                var lines = chunk.Split('\n');
                lock (Lock)
                {
                    for (int i = 0; i < lines.Length - 1; i++)
                        ParseLine(state, lines[i].TrimEnd('\r'));
                    state.Remainder = lines[lines.Length - 1];
                    state.LastLength += read;
                }
            }
        }

        static void ParseLine(State state, string line)
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
                state.Revision++;
                return;
            }

            if (line.EndsWith("Current profile saved.", StringComparison.Ordinal))
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
