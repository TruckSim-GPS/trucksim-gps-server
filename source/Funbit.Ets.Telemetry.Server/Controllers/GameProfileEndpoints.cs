using System;
using Funbit.Ets.Telemetry.Server.Data;
using Funbit.Ets.Telemetry.Server.Helpers;
using Newtonsoft.Json;

namespace Funbit.Ets.Telemetry.Server.Controllers
{
    /// <summary>
    /// Endpoint helpers for the game profile API consumed by the mobile app's
    /// mods configuration screen. The state endpoint is cheap enough to poll:
    /// it only reads in-memory state maintained by <see cref="ProfileWatcher"/>.
    /// </summary>
    public static class GameProfileEndpoints
    {
        public static bool IsValidGame(string game)
        {
            return game == "ets2" || game == "ats";
        }

        static bool IsRunning(string game)
        {
            return Ets2ProcessHelper.IsEts2Running &&
                string.Equals(Ets2ProcessHelper.LastRunningGameName, game, StringComparison.OrdinalIgnoreCase);
        }

        public static string GetStateJson(string game)
        {
            var state = ProfileWatcher.GetState(game);
            bool gameRunning = IsRunning(game);

            object activeProfile = null;
            if (gameRunning && state.ActiveProfileId != null)
            {
                activeProfile = new
                {
                    id = state.ActiveProfileId,
                    name = GameProfileScanner.TryDecodeHexName(state.ActiveProfileId),
                    type = state.ActiveProfileType,
                };
            }

            var mp = TruckersMpWatcher.GetState(game);
            bool mpActive = gameRunning && mp.Active;
            return JsonConvert.SerializeObject(new
            {
                gameRunning,
                game,
                revision = state.Revision,
                activeProfile,
                truckersMp = new { active = mpActive, clientVersion = mpActive ? mp.ClientVersion : null },
            }, JsonHelper.RestSettings);
        }

        public static string GetProfilesJson(string game)
        {
            var profiles = GameProfileScanner.GetProfiles(game);
            return JsonConvert.SerializeObject(new { game, profiles }, JsonHelper.RestSettings);
        }

        public static string GetProfileModsJson(string game, string id, string type)
        {
            // TruckersMP ignores the profile's mod list and mounts its own selection.
            var state = ProfileWatcher.GetState(game);
            var mp = TruckersMpWatcher.GetState(game);
            var mods = mp.Active && IsRunning(game) && id == state.ActiveProfileId && type == state.ActiveProfileType
                ? GameProfileScanner.GetMountedMods(game, id, type, mp.MountedFiles)
                : GameProfileScanner.GetProfileMods(game, id, type);
            return JsonConvert.SerializeObject(mods, JsonHelper.RestSettings);
        }
    }
}
