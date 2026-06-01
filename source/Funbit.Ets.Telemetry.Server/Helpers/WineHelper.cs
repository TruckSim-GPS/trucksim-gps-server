using System;
using System.Runtime.InteropServices;

namespace Funbit.Ets.Telemetry.Server.Helpers
{
    /// <summary>
    /// Helper class for Wine-related functionality.
    /// </summary>
    public class WineHelper
    {
        /// <summary>
        /// Checks for running via Wine or Proton.
        /// </summary>
        public static bool IsInWine()
        {
            IntPtr ntdll = GetModuleHandle("ntdll.dll");
            if (ntdll == IntPtr.Zero)
            {
                return false;
            }

            bool isWine = GetProcAddress(ntdll, "wine_get_version") != IntPtr.Zero;
            return isWine;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string dllToLoad);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);
    }
}