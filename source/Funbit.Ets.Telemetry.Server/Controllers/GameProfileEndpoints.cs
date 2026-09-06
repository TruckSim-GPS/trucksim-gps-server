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

        public static string GetStateJson(string game)
        {
            var state = ProfileWatcher.GetState(game);
            bool gameRunning = Ets2ProcessHelper.IsEts2Running &&
                string.Equals(Ets2ProcessHelper.LastRunningGameName, game, StringComparison.OrdinalIgnoreCase);

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

            return JsonConvert.SerializeObject(new
            {
                gameRunning,
                game,
                revision = state.Revision,
                activeProfile,
            }, JsonHelper.RestSettings);
        }

        public static string GetProfilesJson(string game)
        {
            var profiles = GameProfileScanner.GetProfiles(game);
            return JsonConvert.SerializeObject(new { game, profiles }, JsonHelper.RestSettings);
        }

        public static string GetProfileModsJson(string game, string id, string type)
        {
            var mods = GameProfileScanner.GetProfileMods(game, id, type);
            return JsonConvert.SerializeObject(mods, JsonHelper.RestSettings);
        }
    }
}
