using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using SIIDecryptSharp;

namespace Funbit.Ets.Telemetry.Server.Data
{
    public class GameProfileInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
    }

    public class GameProfileMod
    {
        public string Package { get; set; }
        public string DisplayName { get; set; }
        public string Source { get; set; }
        public long? WorkshopId { get; set; }
        public bool FileFound { get; set; }
        public long? FileSize { get; set; }
        public string Fingerprint { get; set; }
    }

    public class GameProfileMods
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public string MapPath { get; set; }
        public List<GameProfileMod> Mods { get; set; }
        public string Error { get; set; }
    }

    /// <summary>
    /// Reads ETS2/ATS profiles from disk: local profiles under the game's Documents folder
    /// and Steam Cloud profiles under Steam userdata. Profile folder names are the
    /// hex-encoded profile name; profile.sii holds the active mod list.
    /// </summary>
    public static class GameProfileScanner
    {
        static readonly log4net.ILog Log = log4net.LogManager.GetLogger(
            MethodBase.GetCurrentMethod().DeclaringType);

        static readonly ConcurrentDictionary<string, Tuple<long, DateTime, string>> FingerprintCache =
            new ConcurrentDictionary<string, Tuple<long, DateTime, string>>();

        static readonly Regex ActiveModRegex = new Regex(
            "active_mods\\[\\d+\\]:\\s*\"([^|\"]*)\\|?([^\"]*)\"", RegexOptions.Compiled);
        static readonly Regex MapPathRegex = new Regex(
            "map_path:\\s*\"([^\"]*)\"", RegexOptions.Compiled);
        static readonly Regex WorkshopPackageRegex = new Regex(
            "^mod_workshop_package\\.([0-9A-Fa-f]+)$", RegexOptions.Compiled);

        public static string GetDocumentsRoot(string game)
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string folder = game == "ats" ? "American Truck Simulator" : "Euro Truck Simulator 2";
            return Path.Combine(documents, folder);
        }

        static string GetSteamAppId(string game)
        {
            return game == "ats" ? "270880" : "227300";
        }

        static string GetSteamRoot()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
            {
                var path = key?.GetValue("SteamPath") as string;
                return string.IsNullOrEmpty(path) ? null : path.Replace('/', '\\');
            }
        }

        static IEnumerable<string> GetSteamProfileRoots(string game)
        {
            string steamRoot = GetSteamRoot();
            if (steamRoot == null)
                yield break;
            string userData = Path.Combine(steamRoot, "userdata");
            if (!Directory.Exists(userData))
                yield break;
            foreach (var account in Directory.GetDirectories(userData))
            {
                string profiles = Path.Combine(account, GetSteamAppId(game), "remote", "profiles");
                if (Directory.Exists(profiles))
                    yield return profiles;
            }
        }

        public static List<GameProfileInfo> GetProfiles(string game)
        {
            var result = new List<GameProfileInfo>();

            string localRoot = Path.Combine(GetDocumentsRoot(game), "profiles");
            if (Directory.Exists(localRoot))
                AddProfiles(result, localRoot, "local");
            int localCount = result.Count;

            foreach (var steamRoot in GetSteamProfileRoots(game))
                AddProfiles(result, steamRoot, "steam");

            Log.InfoFormat("[{0}] Profiles found: {1} local, {2} steam (documents: {3}; steam: {4})",
                game, localCount, result.Count - localCount, GetDocumentsRoot(game),
                GetSteamRoot() ?? "not found");
            return result;
        }

        static void AddProfiles(List<GameProfileInfo> result, string root, string type)
        {
            foreach (var dir in Directory.GetDirectories(root))
            {
                if (!File.Exists(Path.Combine(dir, "profile.sii")))
                    continue;
                string id = Path.GetFileName(dir);
                string name = TryDecodeHexName(id);
                if (name == null || result.Any(p => p.Id == id && p.Type == type))
                    continue;
                result.Add(new GameProfileInfo { Id = id, Name = name, Type = type });
            }
        }

        public static GameProfileMods GetProfileMods(string game, string id, string type)
        {
            var result = new GameProfileMods
            {
                Id = id,
                Name = TryDecodeHexName(id) ?? id,
                Type = type,
                Mods = new List<GameProfileMod>()
            };

            string profilePath = ResolveProfileSiiPath(game, id, type);
            if (profilePath == null)
            {
                Log.WarnFormat("[{0}] profile.sii not found for {1} profile '{2}' (id {3})",
                    game, type, result.Name, id);
                result.Error = "profile_sii_missing";
                return result;
            }

            string text;
            try
            {
                text = Encoding.UTF8.GetString(Decryptor.Decrypt(profilePath));
            }
            catch (Exception ex)
            {
                Log.Warn($"[{game}] Could not decrypt '{profilePath}'", ex);
                result.Error = "decrypt_failed";
                return result;
            }

            var modMatches = ActiveModRegex.Matches(text);
            if (modMatches.Count == 0 && !text.Contains("active_mods"))
            {
                Log.WarnFormat("[{0}] No active_mods in '{1}' ({2} chars decoded); profile format " +
                               "may have changed", game, profilePath, text.Length);
                result.Error = "parse_failed";
                return result;
            }

            var mapPathMatch = MapPathRegex.Match(text);
            if (mapPathMatch.Success)
                result.MapPath = mapPathMatch.Groups[1].Value;

            // profile.sii stores active_mods[] bottom-up: index 0 is the BOTTOM of the
            // in-game Mod Manager, the last entry its top = highest priority. Serve
            // top-first so the wire order is the priority order consumers act on.
            for (int i = modMatches.Count - 1; i >= 0; i--)
            {
                string package = modMatches[i].Groups[1].Value;
                string displayName = modMatches[i].Groups[2].Value;
                result.Mods.Add(DescribeMod(game, package, displayName));
            }

            LogModInventory(game, result);
            return result;
        }

        static void LogModInventory(string game, GameProfileMods result)
        {
            Log.InfoFormat("[{0}] Profile '{1}' ({2}): {3} mods, {4} missing, {5} fingerprinted, map_path '{6}'",
                game, result.Name, result.Type, result.Mods.Count,
                result.Mods.Count(m => !m.FileFound), result.Mods.Count(m => m.Fingerprint != null),
                result.MapPath ?? "");

            for (int i = 0; i < result.Mods.Count; i++)
            {
                var mod = result.Mods[i];
                Log.InfoFormat("  {0,2}. {1,-40} {2,-8} {3,-7} {4,10} {5}", i + 1, mod.Package,
                    mod.Source, mod.FileFound ? "found" : "MISSING",
                    mod.FileSize.HasValue ? $"{mod.FileSize.Value / 1048576.0:F1} MB" : "-",
                    mod.Fingerprint == null ? "-" : mod.Fingerprint.Substring(0, 8));
            }
        }

        static string ResolveProfileSiiPath(string game, string id, string type)
        {
            if (type == "local")
            {
                string path = Path.Combine(GetDocumentsRoot(game), "profiles", id, "profile.sii");
                return File.Exists(path) ? path : null;
            }
            foreach (var steamRoot in GetSteamProfileRoots(game))
            {
                string path = Path.Combine(steamRoot, id, "profile.sii");
                if (File.Exists(path))
                    return path;
            }
            return null;
        }

        static GameProfileMod DescribeMod(string game, string package, string displayName)
        {
            var mod = new GameProfileMod
            {
                Package = package,
                // Verbatim from profile.sii; empty when the profile stores no display
                // name. The app must know the difference: fabricated names would feed
                // its name-based mod matching.
                DisplayName = displayName
            };

            var workshopMatch = WorkshopPackageRegex.Match(package);
            if (workshopMatch.Success)
            {
                mod.Source = "workshop";
                mod.WorkshopId = Convert.ToInt64(workshopMatch.Groups[1].Value, 16);
                mod.FileFound = FindWorkshopContentDir(game, mod.WorkshopId.Value) != null;
                return mod;
            }

            mod.Source = "local";
            // The package name comes from the profile and ends up in a path, so it may
            // only ever be a plain file name.
            if (package.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                package == "." || package == "..")
            {
                return mod;
            }

            string modRoot = Path.Combine(GetDocumentsRoot(game), "mod");
            string filePath = Path.Combine(modRoot, package + ".scs");
            if (File.Exists(filePath))
            {
                mod.FileFound = true;
                try
                {
                    long fileSize;
                    mod.Fingerprint = GetFingerprint(filePath, out fileSize);
                    mod.FileSize = fileSize;
                }
                catch (Exception ex)
                {
                    // A file the game is holding must not fail the whole mod list.
                    Log.Warn($"[{game}] Could not fingerprint '{filePath}'", ex);
                }
            }
            else if (Directory.Exists(Path.Combine(modRoot, package)))
            {
                // Unpacked mods are folders; there is no single file to fingerprint.
                mod.FileFound = true;
            }
            return mod;
        }

        static string FindWorkshopContentDir(string game, long workshopId)
        {
            string steamRoot = GetSteamRoot();
            if (steamRoot == null)
                return null;
            string appId = GetSteamAppId(game);
            foreach (var library in GetSteamLibraryPaths(steamRoot))
            {
                string dir = Path.Combine(library, "steamapps", "workshop", "content", appId,
                    workshopId.ToString());
                if (Directory.Exists(dir))
                    return dir;
            }
            return null;
        }

        static IEnumerable<string> GetSteamLibraryPaths(string steamRoot)
        {
            yield return steamRoot;
            string vdfPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdfPath))
                yield break;
            string vdf;
            try
            {
                vdf = File.ReadAllText(vdfPath);
            }
            catch (IOException ex)
            {
                Log.WarnFormat("Could not read Steam library list '{0}': {1}", vdfPath, ex.Message);
                yield break;
            }
            foreach (Match match in Regex.Matches(vdf, "\"path\"\\s+\"([^\"]+)\""))
                yield return match.Groups[1].Value.Replace(@"\\", @"\");
        }

        /// <summary>
        /// Content-derived mod file identity that survives file renames: a SHA-256 over the
        /// first and last mebibyte plus the file length. Cheap enough to compute on demand.
        /// The size is taken from the opened stream so symlinked mod files report the
        /// target's size instead of the link's.
        /// </summary>
        static string GetFingerprint(string filePath, out long fileSize)
        {
            DateTime lastWrite = File.GetLastWriteTimeUtc(filePath);
            Tuple<long, DateTime, string> cached;
            if (FingerprintCache.TryGetValue(filePath, out cached) && cached.Item2 == lastWrite)
            {
                fileSize = cached.Item1;
                return cached.Item3;
            }

            const int sampleSize = 1024 * 1024;
            using (var sha = SHA256.Create())
            using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                fileSize = stream.Length;

                int headSize = (int)Math.Min(sampleSize, fileSize);
                var buffer = new byte[headSize];
                ReadFully(stream, buffer);
                sha.TransformBlock(buffer, 0, headSize, null, 0);

                int tailSize = (int)Math.Min(sampleSize, fileSize);
                stream.Seek(-tailSize, SeekOrigin.End);
                buffer = new byte[tailSize];
                ReadFully(stream, buffer);
                sha.TransformBlock(buffer, 0, tailSize, null, 0);

                var lengthBytes = BitConverter.GetBytes(fileSize);
                sha.TransformFinalBlock(lengthBytes, 0, lengthBytes.Length);

                string fingerprint = BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
                FingerprintCache[filePath] = Tuple.Create(fileSize, lastWrite, fingerprint);
                return fingerprint;
            }
        }

        static void ReadFully(Stream stream, byte[] buffer)
        {
            int total = 0;
            while (total < buffer.Length)
            {
                int read = stream.Read(buffer, total, buffer.Length - total);
                if (read <= 0)
                    throw new EndOfStreamException();
                total += read;
            }
        }

        /// <summary>
        /// Decodes a profile folder name, or returns null when it is not strictly a pair of
        /// hex digits per byte. Callers rely on this to reject ids that could act as a path.
        /// </summary>
        public static string TryDecodeHexName(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex.Length % 2 != 0)
                return null;
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                int high = HexDigit(hex[i * 2]);
                int low = HexDigit(hex[i * 2 + 1]);
                if (high < 0 || low < 0)
                    return null;
                bytes[i] = (byte)((high << 4) | low);
            }
            return Encoding.UTF8.GetString(bytes);
        }

        static int HexDigit(char c)
        {
            if (c >= '0' && c <= '9')
                return c - '0';
            if (c >= 'a' && c <= 'f')
                return c - 'a' + 10;
            if (c >= 'A' && c <= 'F')
                return c - 'A' + 10;
            return -1;
        }

        public static string EncodeHexName(string name)
        {
            return BitConverter.ToString(Encoding.UTF8.GetBytes(name)).Replace("-", "");
        }
    }
}
