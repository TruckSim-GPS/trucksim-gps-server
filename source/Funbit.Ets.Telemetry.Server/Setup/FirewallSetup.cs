using System;
using System.Configuration;
using System.Reflection;
using System.Windows.Forms;
using Funbit.Ets.Telemetry.Server.Helpers;

namespace Funbit.Ets.Telemetry.Server.Setup
{
    public class FirewallSetup : ISetup
    {
        static readonly log4net.ILog Log = log4net.LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        const string FirewallRuleName = "TruckSim GPS Telemetry Server";
        const string FirewallTcpRuleName = "TruckSim GPS Telemetry Server (TCP Port)";
        const string FirewallUdpRuleName = "TruckSim GPS Telemetry Server (UDP Port)";
        const string LegacyFirewallRuleName = "TRUCKSIM GPS TELEMETRY SERVER (PORT 31377)";

        SetupStatus _status;

        public FirewallSetup()
        {
            // Always run the netsh check. The legacy HadErrors short-circuit caused stuck
            // states: a stale Settings.json flag would make the constructor report
            // "Installed" forever, suppressing the dialog and blocking self-healing.
            try
            {
                const string arguments = "advfirewall firewall show rule dir=in name=all";
                Log.Info("Checking Firewall rule...");
                string output = ProcessHelper.RunNetShell(arguments, "Failed to check Firewall rule status");
                _status = output.Contains(FirewallRuleName) ? SetupStatus.Installed : SetupStatus.Uninstalled;
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                _status = SetupStatus.Failed;
            }
        }

        public SetupStatus Status => _status;

        public SetupStatus Install(IWin32Window owner)
        {
            // No early-return on Installed — clicking "Rerun Setup" must actually re-apply
            // the rules even when the cached status says they're already there. The
            // delete-before-add cleanup below makes this idempotent.
            try
            {
                string exePath = Application.ExecutablePath;
                string port = ConfigurationManager.AppSettings["Port"];

                // Cleanup: delete legacy and any previous incarnations of our rules. "Rule not
                // found" is expected on first install and is swallowed by SafeDeleteRule.
                SafeDeleteRule($"name=\"{LegacyFirewallRuleName}\"");
                SafeDeleteRule($"name=\"{FirewallRuleName}\"");
                SafeDeleteRule($"name=\"{FirewallTcpRuleName}\"");
                SafeDeleteRule($"name=\"{FirewallUdpRuleName}\"");
                SafeDeleteRule($"name=all program=\"{exePath}\"");

                // Primary: program-based rule. Covers TCP REST and future UDP discovery because
                // it has no protocol/port restriction — any inbound traffic to this exe is allowed.
                Log.Info("Adding program-based Firewall rule...");
                ProcessHelper.RunNetShell(
                    $"advfirewall firewall add rule name=\"{FirewallRuleName}\" dir=in action=allow " +
                    $"program=\"{exePath}\" profile=any enable=yes " +
                    $"description=\"Allow inbound traffic to TruckSim GPS Telemetry Server\"",
                    "Failed to add program-based Firewall rule");

                // TCP port fallback — if program-path matching fails.
                Log.Info("Adding TCP port Firewall rule...");
                ProcessHelper.RunNetShell(
                    $"advfirewall firewall add rule name=\"{FirewallTcpRuleName}\" dir=in action=allow " +
                    $"protocol=TCP localport={port} profile=any enable=yes " +
                    $"description=\"TCP fallback for TruckSim GPS Telemetry Server\"",
                    "Failed to add TCP port Firewall rule");

                // UDP port fallback — for future auto-discovery traffic, if program-path matching fails.
                Log.Info("Adding UDP port Firewall rule...");
                ProcessHelper.RunNetShell(
                    $"advfirewall firewall add rule name=\"{FirewallUdpRuleName}\" dir=in action=allow " +
                    $"protocol=UDP localport={port} profile=any enable=yes " +
                    $"description=\"UDP fallback for TruckSim GPS Telemetry Server (auto-discovery)\"",
                    "Failed to add UDP port Firewall rule");

                _status = SetupStatus.Installed;
            }
            catch (Exception ex)
            {
                _status = SetupStatus.Failed;
                Log.Error(ex);
                Settings.Instance.FirewallSetupHadErrors = true;
                Settings.Instance.Save();
                throw new Exception("Cannot configure Windows Firewall." + Environment.NewLine +
                                    "If you are using some 3rd-party firewall please open " +
                                    ConfigurationManager.AppSettings["Port"] + " TCP port manually!", ex);
            }

            return _status;
        }

        public SetupStatus Uninstall(IWin32Window owner)
        {
            if (_status == SetupStatus.Uninstalled)
                return _status;

            try
            {
                string exePath = Application.ExecutablePath;

                SafeDeleteRule($"name=\"{LegacyFirewallRuleName}\"");
                SafeDeleteRule($"name=\"{FirewallRuleName}\"");
                SafeDeleteRule($"name=\"{FirewallTcpRuleName}\"");
                SafeDeleteRule($"name=\"{FirewallUdpRuleName}\"");
                SafeDeleteRule($"name=all program=\"{exePath}\"");

                return SetupStatus.Uninstalled;
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                _status = SetupStatus.Failed;
                throw new Exception("Cannot configure Windows Firewall." + Environment.NewLine +
                                    "If you are using some 3rd-party firewall please close " +
                                    ConfigurationManager.AppSettings["Port"] + " TCP port manually!", ex);
            }
        }

        // "Rule not found" makes netsh return a non-zero exit code, which RunNetShell
        // throws on. That's expected on fresh installs — log and continue.
        static void SafeDeleteRule(string criteria)
        {
            try
            {
                Log.InfoFormat("Deleting Firewall rule: {0}", criteria);
                ProcessHelper.RunNetShell(
                    $"advfirewall firewall delete rule {criteria}",
                    "Delete Firewall rule");
            }
            catch (Exception ex)
            {
                Log.InfoFormat("Delete rule returned non-zero (rule likely not present): {0}", ex.Message);
            }
        }
    }
}
