using System;
using System.Configuration;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Funbit.Ets.Telemetry.Server.Helpers;

namespace Funbit.Ets.Telemetry.Server
{
    static class Program
    {
        [DllImport("kernel32.dll", EntryPoint = "CreateMutexA")]
        private static extern int CreateMutex(int lpMutexAttributes, int bInitialOwner, string lpName);
        [DllImport("kernel32.dll")]
        private static extern int GetLastError();
        private const int ErrorAlreadyExists = 183;

        public static bool UninstallMode;
        public static bool ForceSetupMode;

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            // check if another instance is running
            CreateMutex(0, -1,
                Uac.IsProcessElevated()
                    ? "TruckSimGPS_B2F7A93E1D4C58E092F3B6A8CD459127_UAC"
                    : "TruckSimGPS_B2F7A93E1D4C58E092F3B6A8CD459127");
            bool bAnotherInstanceRunning = GetLastError() == ErrorAlreadyExists;
            if (bAnotherInstanceRunning)
            {
                MessageBox.Show(@"Another TruckSim GPS Telemetry Server instance is already running!", @"Warning",
                    MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }

            log4net.Config.XmlConfigurator.Configure();
            
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            UninstallMode = args.Length >= 1 && args.Any(a => a.Trim() == "-uninstall");
            ForceSetupMode = args.Length >= 1 && args.Any(a => a.Trim() == "-rerunsetup");

            Application.Run(new MainForm());
        }
    }
}
