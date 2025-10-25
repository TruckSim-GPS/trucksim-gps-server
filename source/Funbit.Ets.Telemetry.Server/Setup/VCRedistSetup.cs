using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Funbit.Ets.Telemetry.Server.Setup
{
    public class VCRedistSetup : ISetup
    {
        static readonly log4net.ILog Log = log4net.LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        const string RegistryKeyPath = @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64";
        const string VCRedistDownloadUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe";
        const string VCRedistInfoUrl = "https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist";

        SetupStatus _status;

        public VCRedistSetup()
        {
            _status = IsVC2022Installed() ? SetupStatus.Installed : SetupStatus.Uninstalled;
        }

        public SetupStatus Status => _status;

        public SetupStatus Install(IWin32Window owner)
        {
            if (IsVC2022Installed())
            {
                _status = SetupStatus.Installed;
                return _status;
            }

            ShowInstallPrompt(owner);

            // Re-check after user action
            _status = IsVC2022Installed() ? SetupStatus.Installed : SetupStatus.Uninstalled;
            return _status;
        }

        public SetupStatus Uninstall(IWin32Window owner)
        {
            // VC++ Redistributable uninstall is not handled by this app
            // It's a system-wide component that should be managed by Windows
            return SetupStatus.Installed;
        }

        static bool IsVC2022Installed()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(RegistryKeyPath))
                {
                    if (key == null)
                        return false;

                    object bldValue = key.GetValue("Bld");
                    object installedValue = key.GetValue("Installed");

                    if (bldValue != null && installedValue != null)
                    {
                        int buildNumber = (int)bldValue;
                        int installed = (int)installedValue;
                        
                        // VC++ 2019+ is compatible with 2022
                        return installed == 1 && buildNumber >= 27000;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to check VC++ Redistributable: {ex.Message}");
            }

            return false;
        }

        static void ShowInstallPrompt(IWin32Window owner)
        {
            var dialog = new Form
            {
                Text = "Missing Dependency",
                Size = new Size(500, 260),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
                TopMost = true
            };

            var iconBox = new PictureBox
            {
                Location = new Point(20, 20),
                Size = new Size(48, 48),
                Image = SystemIcons.Warning.ToBitmap()
            };

            var messageLabel = new Label
            {
                Location = new Point(80, 20),
                Size = new Size(390, 90),
                Text = "Microsoft Visual C++ 2015-2022 Redistributable is required but not installed.\n\n" +
                       "This is needed for the telemetry plugin to communicate with ETS2/ATS.\n\n" +
                       "Without it, the mobile app won't receive truck data from the simulator."
            };

            var downloadButton = new Button
            {
                Location = new Point(150, 130),
                Size = new Size(200, 40),
                Text = "Download",
                UseVisualStyleBackColor = true,
                Font = new Font("Segoe UI", 9.75F, FontStyle.Regular)
            };

            var infoLink = new LinkLabel
            {
                Location = new Point(150, 178),
                Size = new Size(200, 20),
                Text = "More information at Microsoft",
                TextAlign = ContentAlignment.MiddleCenter,
                LinkBehavior = LinkBehavior.HoverUnderline,
                Font = new Font("Segoe UI", 8.25F)
            };

            var cancelButton = new Button
            {
                Location = new Point(360, 178),
                Size = new Size(110, 28),
                Text = "Skip for Now",
                DialogResult = DialogResult.Cancel,
                UseVisualStyleBackColor = true,
                Font = new Font("Segoe UI", 8.25F)
            };

            // Download button - opens direct download link
            downloadButton.Click += (s, e) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = VCRedistDownloadUrl,
                        UseShellExecute = true
                    });

                    MessageBox.Show(dialog,
                        "Download started in your browser.\n\n" +
                        "After installation completes, please restart this application.",
                        "Download Started",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);

                    dialog.DialogResult = DialogResult.OK;
                    dialog.Close();

                    // Always exit the application so user must restart after installing
                    Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    Log.Error($"Failed to open download link: {ex.Message}");
                    MessageBox.Show(dialog,
                        $"Failed to open download link.\n\n" +
                        $"Please manually visit:\n{VCRedistDownloadUrl}",
                        "Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            };

            // Info link - opens Microsoft documentation
            infoLink.LinkClicked += (s, e) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = VCRedistInfoUrl,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Log.Error($"Failed to open info link: {ex.Message}");
                    MessageBox.Show(dialog,
                        $"Failed to open link.\n\n" +
                        $"Please manually visit:\n{VCRedistInfoUrl}",
                        "Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            };

            dialog.Controls.Add(iconBox);
            dialog.Controls.Add(messageLabel);
            dialog.Controls.Add(downloadButton);
            dialog.Controls.Add(infoLink);
            dialog.Controls.Add(cancelButton);

            dialog.ShowDialog(owner);
        }

        public static bool CheckAndPromptOnStartup(IWin32Window owner)
        {
            if (!IsVC2022Installed())
            {
                ShowInstallPrompt(owner);
                // If we're still here, user clicked "Skip for Now"
                return true; // Continue running
            }
            return true; // VC++ is installed, continue running
        }
    }
}
