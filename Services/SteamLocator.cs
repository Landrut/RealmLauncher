using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace RealmLauncher.Services
{
    public sealed class ConanInstallInfo
    {
        public string InstallDirectory { get; set; }
        public string LauncherExePath { get; set; }
        public string SteamLibraryRoot { get; set; }
        public string BranchKey { get; set; }
        public string DisplayName { get; set; }
        public long SizeOnDiskBytes { get; set; }

        public bool IsLegacyBranch
        {
            get
            {
                return !string.IsNullOrWhiteSpace(BranchKey) &&
                       !string.Equals(BranchKey, "public", StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    public static class SteamLocator
    {
        public const int ConanAppId = 440900;

        public static string FindSteamRoot()
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                var path = ReadSteamPath(RegistryHive.CurrentUser, view, @"Software\Valve\Steam", "SteamPath")
                           ?? ReadSteamPath(RegistryHive.LocalMachine, view, @"SOFTWARE\Valve\Steam", "InstallPath");
                if (path != null)
                {
                    return path;
                }
            }

            return null;
        }

        private static string ReadSteamPath(RegistryHive hive, RegistryView view, string subKey, string valueName)
        {
            try
            {
                using (var baseKey = RegistryKey.OpenBaseKey(hive, view))
                using (var key = baseKey.OpenSubKey(subKey))
                {
                    var value = key?.GetValue(valueName) as string;
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        return null;
                    }

                    var normalized = value.Replace('/', '\\').TrimEnd('\\');
                    return Directory.Exists(normalized) ? normalized : null;
                }
            }
            catch
            {
                return null;
            }
        }

        public static List<string> GetLibraryRoots()
        {
            var roots = new List<string>();
            var steamRoot = FindSteamRoot();
            if (steamRoot == null)
            {
                return roots;
            }

            roots.Add(steamRoot);

            var vdfPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdfPath))
            {
                return roots;
            }

            try
            {
                var text = File.ReadAllText(vdfPath);
                foreach (Match match in Regex.Matches(text, "\"path\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase))
                {
                    var raw = match.Groups[1].Value.Replace("\\\\", "\\").TrimEnd('\\');
                    if (Directory.Exists(raw) && !roots.Any(r => string.Equals(r, raw, StringComparison.OrdinalIgnoreCase)))
                    {
                        roots.Add(raw);
                    }
                }
            }
            catch
            {
            }

            return roots;
        }

        public static ConanInstallInfo FindConanInstall()
        {
            foreach (var libraryRoot in GetLibraryRoots())
            {
                var manifestPath = Path.Combine(libraryRoot, "steamapps", "appmanifest_" + ConanAppId + ".acf");
                if (!File.Exists(manifestPath))
                {
                    continue;
                }

                string manifest;
                try
                {
                    manifest = File.ReadAllText(manifestPath);
                }
                catch
                {
                    continue;
                }

                var installDirName = ReadAcfValue(manifest, "installdir");
                if (string.IsNullOrWhiteSpace(installDirName))
                {
                    continue;
                }

                var installDirectory = Path.Combine(libraryRoot, "steamapps", "common", installDirName);
                if (!Directory.Exists(installDirectory))
                {
                    continue;
                }

                var launcherExe = Path.Combine(installDirectory, "ConanSandbox.exe");
                if (!File.Exists(launcherExe))
                {
                    continue;
                }

                long sizeOnDisk;
                long.TryParse(ReadAcfValue(manifest, "SizeOnDisk"), out sizeOnDisk);

                return new ConanInstallInfo
                {
                    InstallDirectory = installDirectory,
                    LauncherExePath = launcherExe,
                    SteamLibraryRoot = libraryRoot,
                    BranchKey = ReadAcfValue(manifest, "BetaKey"),
                    DisplayName = ReadAcfValue(manifest, "name"),
                    SizeOnDiskBytes = sizeOnDisk
                };
            }

            return null;
        }

        public static ConanInstallInfo DescribeInstallFromExePath(string conanExePath)
        {
            if (string.IsNullOrWhiteSpace(conanExePath) || !File.Exists(conanExePath))
            {
                return null;
            }

            var installDirectory = Path.GetDirectoryName(conanExePath);
            if (string.IsNullOrWhiteSpace(installDirectory))
            {
                return null;
            }

            var info = new ConanInstallInfo
            {
                InstallDirectory = installDirectory,
                LauncherExePath = conanExePath
            };

            var common = Directory.GetParent(installDirectory);
            var steamapps = common != null ? common.Parent : null;
            var libraryRoot = steamapps != null ? steamapps.Parent : null;
            if (libraryRoot == null)
            {
                return info;
            }

            info.SteamLibraryRoot = libraryRoot.FullName;

            var manifestPath = Path.Combine(steamapps.FullName, "appmanifest_" + ConanAppId + ".acf");
            if (!File.Exists(manifestPath))
            {
                return info;
            }

            try
            {
                var manifest = File.ReadAllText(manifestPath);
                info.BranchKey = ReadAcfValue(manifest, "BetaKey");
                info.DisplayName = ReadAcfValue(manifest, "name");
            }
            catch
            {
            }

            return info;
        }

        private static string ReadAcfValue(string manifest, string key)
        {
            if (string.IsNullOrEmpty(manifest))
            {
                return null;
            }

            var match = Regex.Match(
                manifest,
                "\"" + Regex.Escape(key) + "\"\\s*\"([^\"]*)\"",
                RegexOptions.IgnoreCase);

            return match.Success ? match.Groups[1].Value.Trim() : null;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetDiskFreeSpaceEx(
            string directoryName,
            out ulong freeBytesAvailable,
            out ulong totalNumberOfBytes,
            out ulong totalNumberOfFreeBytes);

        public static long GetFreeSpaceBytes(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return -1;
            }

            try
            {
                var directory = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
                if (string.IsNullOrWhiteSpace(directory))
                {
                    return -1;
                }

                ulong free, total, totalFree;
                if (GetDiskFreeSpaceEx(directory, out free, out total, out totalFree))
                {
                    return (long)free;
                }
            }
            catch
            {
            }

            try
            {
                var root = Path.GetPathRoot(path);
                if (!string.IsNullOrWhiteSpace(root))
                {
                    return new DriveInfo(root).AvailableFreeSpace;
                }
            }
            catch
            {
            }

            return -1;
        }
    }
}
