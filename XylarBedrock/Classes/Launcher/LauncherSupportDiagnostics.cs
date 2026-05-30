using System;
using System.Collections.Generic;

namespace XylarBedrock.Classes.Launcher
{
    public sealed class BundledDllPackDiagnostics
    {
        public string ExecutableDirectoryPath { get; set; } = string.Empty;
        public string DllDirectoryPath { get; set; } = string.Empty;
        public string ModDllName { get; set; } = string.Empty;
        public string ModDllPath { get; set; } = string.Empty;
        public string RuntimeDllPath { get; set; } = string.Empty;

        public bool DllDirectoryExists { get; set; }
        public bool ModDllExists { get; set; }
        public bool RuntimeDllExists { get; set; }
        public bool ModDllReadable { get; set; }
        public bool RuntimeDllReadable { get; set; }

        public bool IsComplete =>
            DllDirectoryExists &&
            ModDllExists &&
            RuntimeDllExists;

        public bool IsReady =>
            IsComplete &&
            ModDllReadable &&
            RuntimeDllReadable;

        public string StatusText { get; set; } = string.Empty;
        public string DetailsText { get; set; } = string.Empty;
    }

    public sealed class LauncherSupportDiagnostics
    {
        public bool OfficialStoreReleaseDetected { get; set; }
        public IReadOnlyList<string> OfficialStoreReleaseDirectories { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> PreviewOrLocalDirectories { get; set; } = Array.Empty<string>();
        public BundledDllPackDiagnostics BundledDllPack { get; set; } = new BundledDllPackDiagnostics();
        public string LastLaunchMethodAttempted { get; set; } = "No launch attempted yet.";
    }
}
