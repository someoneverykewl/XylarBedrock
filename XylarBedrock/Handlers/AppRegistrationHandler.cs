using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace XylarBedrock.Handlers
{
    public static class AppRegistrationHandler
    {
        private const string UninstallRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\XylarBedrock";
        private const string DesktopShortcutName = "XylarBedrock.lnk";
        private const string StartMenuShortcutName = "XylarBedrock.lnk";

        public static void EnsureRegistered()
        {
            string exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                return;
            }

            string installDirectory = GetInstallDirectory();
            using RegistryKey uninstallKey = Registry.CurrentUser.CreateSubKey(UninstallRegistryKey, true);
            if (uninstallKey == null)
            {
                return;
            }

            uninstallKey.SetValue("DisplayName", App.DisplayName);
            uninstallKey.SetValue("DisplayVersion", App.Version);
            uninstallKey.SetValue("Publisher", "Xylar Inc. and Mrmariix");
            uninstallKey.SetValue("InstallLocation", installDirectory);
            uninstallKey.SetValue("DisplayIcon", exePath);
            uninstallKey.SetValue("UninstallString", $"\"{exePath}\" --uninstall");
            uninstallKey.SetValue("QuietUninstallString", $"\"{exePath}\" --uninstall");
            uninstallKey.SetValue("NoModify", 1, RegistryValueKind.DWord);
            uninstallKey.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            uninstallKey.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));

            int estimatedSizeKb = GetEstimatedSizeInKilobytes(installDirectory);
            if (estimatedSizeKb > 0)
            {
                uninstallKey.SetValue("EstimatedSize", estimatedSizeKb, RegistryValueKind.DWord);
            }
        }

        public static void RunInteractiveUninstall()
        {
            string installDirectory = GetInstallDirectory();

            DialogResult result = MessageBox.Show(
                "Do you want to remove XylarBedrock from Windows Apps and delete its shortcuts?\n\n" +
                "The launcher files will stay in their current folder, and that folder will open when uninstall finishes.",
                App.DisplayName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
            {
                return;
            }

            try
            {
                Unregister();
                DeleteShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), DesktopShortcutName));
                DeleteShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), StartMenuShortcutName));

                MessageBox.Show(
                    "XylarBedrock was removed from Windows Apps.\n\n" +
                    "If you do not need it anymore, you can delete this folder manually:\n" +
                    installDirectory,
                    App.DisplayName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                TryOpenFolder(installDirectory);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "XylarBedrock could not finish uninstall cleanly.\n\n" + ex.Message,
                    App.DisplayName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static void Unregister()
        {
            Registry.CurrentUser.DeleteSubKeyTree(UninstallRegistryKey, false);
        }

        private static void DeleteShortcut(string shortcutPath)
        {
            if (File.Exists(shortcutPath))
            {
                File.Delete(shortcutPath);
            }
        }

        private static string GetInstallDirectory()
        {
            return AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static int GetEstimatedSizeInKilobytes(string installDirectory)
        {
            try
            {
                long size = Directory
                    .EnumerateFiles(installDirectory, "*", SearchOption.AllDirectories)
                    .Select(file => new FileInfo(file).Length)
                    .Sum();

                return (int)Math.Min(int.MaxValue, Math.Max(1, size / 1024));
            }
            catch
            {
                return 0;
            }
        }

        private static void TryOpenFolder(string installDirectory)
        {
            if (!Directory.Exists(installDirectory))
            {
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{installDirectory}\"",
                UseShellExecute = true
            });
        }
    }
}
