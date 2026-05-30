using XylarBedrock.Downloaders;
using XylarBedrock.Handlers;
using XylarBedrock.Localization.Language;
using XylarBedrock.ViewModels;
using JemExtensions;
using Microsoft.Win32;
using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;

namespace XylarBedrock
{
    public static class Program
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        private const string MinimumVCRuntimeVersion = "14.14.26405.0";
        private const string VCRedistDownloadUrlX64 = "https://aka.ms/vc14/vc_redist.x64.exe";
        private const string VCRedistDownloadUrlX86 = "https://aka.ms/vc14/vc_redist.x86.exe";
        private static int deferredStartupStarted;

        [STAThread]
        public static void Main()
        {
            string[] startupArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();
            StartupArgsHandler.SetStartupArgs(startupArgs);
            if (StartupArgsHandler.TryHandleProcessWideArgs(startupArgs))
            {
                return;
            }

            Directory.SetCurrentDirectory(AppContext.BaseDirectory);
            RuntimeHandler.ConfigureRenderingCompatibility();

            RuntimeHandler.StartLogging();
            RuntimeHandler.LogStartupInformation();
            RuntimeHandler.ValidateOSArchitecture();
            Trace.WriteLine("Application Starting...");
            var application = new App();
            application.Startup += OnApplicationInitalizing;
            application.InitializeComponent();
            application.Run();
        }

        public static void OnApplicationInitalizing(object sender, StartupEventArgs e)
        {
            Trace.WriteLine("Application Initalization Started!");
            StartupArgsHandler.SetStartupArgs(e.Args);
            StartupArgsHandler.RunPreStartupArgs();
            Trace.WriteLine("Application Initalization Finished!");
        }
        public static async Task OnApplicationLoaded()
        {
            await MainViewModel.Default.ShowWaitingDialog(async () =>
            {
                Trace.WriteLine("Preparing Application...");
                SafeRun("App registration", AppRegistrationHandler.EnsureRegistered);
                SafeRun("Language init", LanguageManager.Init);
                SafeRun("Load config", MainDataModel.Default.LoadConfig);
                SafeRun("Play button refresh", () =>
                {
                    MainDataModel.Default.ProgressBarState.PlayButtonLanguageChanged =
                        !MainDataModel.Default.ProgressBarState.PlayButtonLanguageChanged;
                });
                await Task.CompletedTask;
                Trace.WriteLine("Preparing Application: DONE");
            });
        }

        public static async Task OnApplicationRefresh()
        {

            await MainViewModel.Default.ShowWaitingDialog(async () =>
            {
                Trace.WriteLine("Refreshing Application...");
                SafeRun("Refresh config", MainDataModel.Default.LoadConfig);
                await SafeRunAsync("Refresh versions", () => MainDataModel.Default.LoadVersions());
                Trace.WriteLine("Refreshing Application: DONE");
            });
        }

        public static void StartDeferredStartupWork()
        {
            if (Interlocked.Exchange(ref deferredStartupStarted, 1) == 1)
            {
                return;
            }

            _ = Task.Run(RunDeferredStartupWorkAsync);
        }

        private static async Task RunDeferredStartupWorkAsync()
        {
            await Task.Delay(1200);

            Trace.WriteLine("Starting deferred startup work...");
            await SafeRunAsync("VC runtime", EnsureVCRuntimeAsync);
            await SafeRunAsync("Load versions", () => MainDataModel.Default.LoadVersions(true));
            await SafeRunAsync("Bundled DLL refresh", () => MainDataModel.Default.PackageManager.AutoRefreshBundledModAsync());
            SafeRun("Play button refresh", () =>
            {
                MainDataModel.Default.ProgressBarState.PlayButtonLanguageChanged =
                    !MainDataModel.Default.ProgressBarState.PlayButtonLanguageChanged;
            });
            await SafeRunAsync("Bugrock of the week", RuntimeHandler.InitalizeBugRockOfTheWeek);
            await SafeRunAsync("Update check", async () =>
            {
                if (StartupArgsHandler.IsUpdatePromptTestRequested)
                {
                    await MainDataModel.Updater.ShowTestUpdatePromptAsync();
                    return;
                }

                if (await MainDataModel.Updater.CheckForUpdatesAsync(true))
                {
                    MainViewModel.Default.UpdateButton.ShowUpdateButton();
                    await MainDataModel.Updater.ShowUpdatePromptAsync();
                }
            });
            Trace.WriteLine("Deferred startup work finished.");
        }

        private static void SafeRun(string stepName, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Startup step failed: {stepName}: {ex}");
            }
        }

        private static async Task SafeRunAsync(string stepName, Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Startup step failed: {stepName}: {ex}");
            }
        }

        private static Task EnsureVCRuntimeAsync()
        {
            return Task.Run(() => CheckForVCRuntime(false));
        }

        public static bool CheckForVCRuntime(bool isStartupBlocking = true)
        {
            Trace.WriteLine("Checking VC Runtime version");

            string[] requiredArchitectures = GetRequiredVCRuntimeArchitectures().ToArray();
            List<string> missingArchitectures = new List<string>();

            foreach (string architecture in requiredArchitectures)
            {
                if (TryGetInstalledVCRuntimeVersion(architecture, out Version installedVersion) &&
                    installedVersion.CompareTo(new Version(MinimumVCRuntimeVersion)) >= 0)
                {
                    Trace.WriteLine($"VC++ Runtime OK for {architecture}");
                    continue;
                }

                string downloadUrl = GetVCRedistDownloadUrl(architecture);
                Trace.WriteLine($"VC++ Runtime missing or outdated for {architecture}. Starting official Microsoft installer.");
                bool installResult = TryInstallVCRuntime(architecture, downloadUrl);
                if (installResult)
                {
                    Trace.WriteLine($"VC++ Runtime OK for {architecture}");
                    continue;
                }

                missingArchitectures.Add(architecture);
            }

            if (missingArchitectures.Count == 0)
            {
                return true;
            }

            string missingArchitecturesText = string.Join(" and ", missingArchitectures);
            string manualLinks = string.Join(
                Environment.NewLine,
                missingArchitectures.Select(architecture => $"{architecture}: {GetVCRedistDownloadUrl(architecture)}"));

            string errorMessage =
                $"XylarBedrock could not confirm the Microsoft Visual C++ Runtime for {missingArchitecturesText} yet.\n\n" +
                "The launcher tried to install the official Microsoft package automatically, but it did not complete.\n\n" +
                $"If Minecraft still does not open, install it manually from:\n{manualLinks}";
            Trace.WriteLine(errorMessage);

            if (isStartupBlocking)
            {
                System.Windows.Forms.MessageBox.Show(errorMessage, "Runtime required", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return false;
        }
        public static bool CheckForWindowsVersion()
        {
            Trace.WriteLine("Checking Windows Version");
            bool result = false;
            string minimumVersionS = "10.0.19041.0";

            try
            {
                Version currentVersion = Environment.OSVersion.Version;
                Version minimumVersion = new Version(minimumVersionS);
                if (currentVersion.CompareTo(minimumVersion) >= 0) result = true;
            }
            catch (Exception) { }

            if (!result)
            {
                Trace.WriteLine("This application only works on Windows version " + minimumVersionS + " or above!");
                System.Windows.Forms.MessageBox.Show("This application only works on Windows version " + minimumVersionS + " or above!", "Error");
            }
            else
            {
                Trace.WriteLine("Windows Version OK");
            }
                return result;
        }

        private static IEnumerable<string> GetRequiredVCRuntimeArchitectures()
        {
            HashSet<string> architectures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!Environment.Is64BitProcess)
            {
                architectures.Add("x86");
            }

            switch (RuntimeInformation.OSArchitecture)
            {
                case Architecture.X64:
                case Architecture.Arm64:
                    architectures.Add("x64");
                    break;
                case Architecture.X86:
                    architectures.Add("x86");
                    break;
            }

            if (architectures.Count == 0)
            {
                architectures.Add("x64");
            }

            return architectures;
        }

        private static string GetVCRedistDownloadUrl(string architecture)
        {
            return architecture.Equals("x86", StringComparison.OrdinalIgnoreCase)
                ? VCRedistDownloadUrlX86
                : VCRedistDownloadUrlX64;
        }

        private static bool TryInstallVCRuntime(string architecture, string downloadUrl)
        {
            try
            {
                string workingDirectory = Path.Combine(Path.GetTempPath(), "XylarBedrock");
                Directory.CreateDirectory(workingDirectory);

                string installerPath = Path.Combine(workingDirectory, $"vc_redist.{architecture}.exe");
                string logPath = Path.Combine(workingDirectory, "vc_redist_install.log");

                using (HttpClient client = new HttpClient())
                using (HttpResponseMessage response = client.GetAsync(downloadUrl).GetAwaiter().GetResult())
                {
                    response.EnsureSuccessStatusCode();
                    using Stream downloadStream = response.Content.ReadAsStream();
                    using FileStream fileStream = File.Create(installerPath);
                    downloadStream.CopyTo(fileStream);
                }

                ProcessStartInfo installerInfo = new ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments = $"/install /passive /norestart /log \"{logPath}\"",
                    UseShellExecute = true,
                    Verb = "runas"
                };

                using Process installer = Process.Start(installerInfo);
                installer?.WaitForExit();

                if (installer == null) return false;
                if (installer.ExitCode != 0 && installer.ExitCode != 3010)
                {
                    Trace.WriteLine($"VC++ Runtime installer exited with code {installer.ExitCode}");
                    return false;
                }

                return TryGetInstalledVCRuntimeVersion(architecture, out Version installedVersion)
                    && installedVersion.CompareTo(new Version(MinimumVCRuntimeVersion)) >= 0;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"VC++ Runtime install failed: {ex}");
                return false;
            }
        }

        private static bool TryGetInstalledVCRuntimeVersion(string architecture, out Version version)
        {
            version = null;

            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                    using RegistryKey runtimeKey = baseKey.OpenSubKey($@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\{architecture}");
                    string value = runtimeKey?.GetValue("Version") as string;
                    if (!string.IsNullOrWhiteSpace(value) && Version.TryParse(value.Replace("v", string.Empty), out Version parsedVersion))
                    {
                        version = parsedVersion;
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Unable to read VC++ Runtime registry key in {view}: {ex.Message}");
                }
            }

            return false;
        }
    }
}

