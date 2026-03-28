using System;

namespace RealmLauncher.Models
{
    public sealed class LauncherUpdateCheckResult
    {
        public bool IsUpdateAvailable { get; set; }
        public Version CurrentVersion { get; set; }
        public Version LatestVersion { get; set; }
        public LauncherUpdateManifest Manifest { get; set; }
    }
}
