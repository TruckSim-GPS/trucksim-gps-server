using Funbit.Ets.Telemetry.Server.Helpers;

namespace Funbit.Ets.Telemetry.Server.Setup
{
    public static class SetupManager
    {
        public static ISetup[] Steps;

        static SetupManager()
        {
            if (WineHelper.IsInWine())
            {
                Steps = new ISetup[]
                {
                    new VCRedistSetup(),
                    new PluginSetup()
                };
            }
            else
            {
                Steps = new ISetup[]
                {
                    new VCRedistSetup(),
                    new PluginSetup(),
                    new FirewallSetup(),
                    new UrlReservationSetup()
                };
            }
        }
    }
}