using System;
using System.Configuration;
using System.Diagnostics;
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
        [DllImport("user32.dll")]
        private static extern int RegisterWindowMessage(string message);
        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam);
        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int dwProcessId);
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private const int ErrorAlreadyExists = 183;
        private const int HWND_BROADCAST = 0xffff;
        private const int ASFW_ANY = -1;
        private const int SW_RESTORE = 9;

        public const string ShowExistingInstanceMessage = "WM_TRUCKSIMGPS_SHOW_INSTANCE";
        public static int WM_SHOWEXISTINGINSTANCE;

        public static bool UninstallMode;
        public static bool ForceSetupMode;

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            // Register custom window message for bringing existing instance to foreground
            WM_SHOWEXISTINGINSTANCE = RegisterWindowMessage(ShowExistingInstanceMessage);

            // check if another instance is running
            CreateMutex(0, -1,
                Uac.IsProcessElevated()
                    ? "TruckSimGPS_B2F7A93E1D4C58E092F3B6A8CD459127_UAC"
                    : "TruckSimGPS_B2F7A93E1D4C58E092F3B6A8CD459127");
            bool bAnotherInstanceRunning = GetLastError() == ErrorAlreadyExists;
            if (bAnotherInstanceRunning)
            {
                BringExistingInstanceToForeground();
                return;
            }

            log4net.Config.XmlConfigurator.Configure();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            UninstallMode = args.Length >= 1 && args.Any(a => a.Trim() == "-uninstall");
            ForceSetupMode = args.Length >= 1 && args.Any(a => a.Trim() == "-rerunsetup");

            Application.Run(new MainForm());
        }

        static void BringExistingInstanceToForeground()
        {
            // Allow any process to set foreground window (required for Windows 11)
            AllowSetForegroundWindow(ASFW_ANY);

            // Find the existing process
            var currentProcess = Process.GetCurrentProcess();
            var existingProcesses = Process.GetProcessesByName(currentProcess.ProcessName)
                .Where(p => p.Id != currentProcess.Id)
                .ToArray();

            foreach (var process in existingProcesses)
            {
                var hwnd = process.MainWindowHandle;
                if (hwnd != IntPtr.Zero)
                {
                    // Window is visible - restore and bring to front
                    ShowWindow(hwnd, SW_RESTORE);
                    SetForegroundWindow(hwnd);
                    return;
                }

                // MainWindowHandle is zero (window might be hidden in system tray)
                // Send message directly to all windows belonging to this process
                uint processId = (uint)process.Id;
                EnumWindows((hWnd, lParam) =>
                {
                    GetWindowThreadProcessId(hWnd, out uint windowProcessId);
                    if (windowProcessId == processId)
                    {
                        PostMessage(hWnd, WM_SHOWEXISTINGINSTANCE, IntPtr.Zero, IntPtr.Zero);
                    }
                    return true; // Continue enumeration
                }, IntPtr.Zero);
                return;
            }

            // Fallback: broadcast message
            PostMessage((IntPtr)HWND_BROADCAST, WM_SHOWEXISTINGINSTANCE, IntPtr.Zero, IntPtr.Zero);
        }
    }
}
