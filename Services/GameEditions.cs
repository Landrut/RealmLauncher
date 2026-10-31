using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace RealmLauncher.Services
{
    public enum GameEdition
    {
        Enhanced,
        Legacy
    }

    public sealed class EditionInstall
    {
        public GameEdition Edition { get; set; }
        public string InstallDirectory { get; set; }
        public string ExePath { get; set; }

        /// <summary>Steam keeps this copy up to date: it is the folder appmanifest_440900.acf points at.</summary>
        public bool IsSteamManaged { get; set; }

        /// <summary>The player pointed the launcher at this folder by hand.</summary>
        public bool IsCustomPath { get; set; }
    }

    public sealed class GameEditionState
    {
        public EditionInstall Enhanced { get; set; }
        public EditionInstall Legacy { get; set; }

        public string SteamManagedDirectory { get; set; }
        public string SteamBranchKey { get; set; }
        public GameEdition SteamBranchEdition { get; set; }

        /// <summary>Both builds sit on disk, so switching is a matter of launching the other folder.</summary>
        public bool DualInstallReady
        {
            get { return Enhanced != null && Legacy != null; }
        }

        public EditionInstall Get(GameEdition edition)
        {
            return edition == GameEdition.Enhanced ? Enhanced : Legacy;
        }

        public void Set(GameEdition edition, EditionInstall install)
        {
            if (edition == GameEdition.Enhanced)
            {
                Enhanced = install;
            }
            else
            {
                Legacy = install;
            }
        }
    }

    public sealed class DualInstallPrepareResult
    {
        public bool Renamed { get; set; }
        public string Error { get; set; }
        public string Warning { get; set; }
    }

    /// <summary>
    /// Conan Exiles ships as two different builds on one Steam app: Enhanced on the
    /// default branch and Legacy on "conan-exiles-legacy". Steam only keeps the branch
    /// it is set to, so flipping between them means a full re-download every time.
    /// Keeping the other build in a renamed sibling folder sidesteps that: the launcher
    /// then just starts whichever folder it needs.
    /// </summary>
    public static class GameEditions
    {
        public const string LegacyBranchKey = "conan-exiles-legacy";

        public const string EnhancedExeName = "ConanSandbox-Win64-Shipping.exe";
        public const string LegacyExeName = "ConanSandbox.exe";
        public const string BattlEyeExeName = "ConanSandbox_BE.exe";

        private const string RootExeName = "ConanSandbox.exe";
        private const string SandboxFolderName = "ConanSandbox";
        private const string DefaultFolderName = "Conan Exiles";

        // StateFlags bits Steam sets while it is fetching or validating files.
        private const int DownloadingStateMask = 256 | 1024 | 1048576 | 2097152 | 4194304;

        private static readonly string[] GameProcessNames =
        {
            "ConanSandbox",
            "ConanSandbox-Win64-Shipping",
            "ConanSandbox_BE"
        };

        public static string DisplayName(GameEdition edition)
        {
            return edition == GameEdition.Enhanced ? "Enhanced" : "Legacy";
        }

        public static GameEdition Other(GameEdition edition)
        {
            return edition == GameEdition.Enhanced ? GameEdition.Legacy : GameEdition.Enhanced;
        }

        public static GameEdition FromBranchKey(string branchKey)
        {
            // No beta selected means the default branch, which is Enhanced.
            return string.IsNullOrWhiteSpace(branchKey) ||
                   string.Equals(branchKey, "public", StringComparison.OrdinalIgnoreCase)
                ? GameEdition.Enhanced
                : GameEdition.Legacy;
        }

        /// <summary>Name the launcher renames an install to so the other build can take its place.</summary>
        public static string FolderNameFor(GameEdition edition)
        {
            return DefaultFolderName + " " + DisplayName(edition);
        }

        public static GameEditionState Detect(string configuredExePath, string customLegacyDirectory)
        {
            var state = new GameEditionState();
            string common = null;

            var steamInstall = SteamLocator.FindConanInstall();
            if (steamInstall != null)
            {
                state.SteamManagedDirectory = steamInstall.InstallDirectory;
                state.SteamBranchKey = steamInstall.BranchKey;
                common = Path.GetDirectoryName(steamInstall.InstallDirectory);
            }

            state.SteamBranchEdition = FromBranchKey(state.SteamBranchKey);

            // Between the rename and the re-download Steam has no manifest for the game,
            // so the configured path is the only way back to the library folder.
            var configuredDirectory = ResolveInstallDirectory(configuredExePath);
            if (string.IsNullOrEmpty(common) && !string.IsNullOrEmpty(configuredDirectory))
            {
                common = Path.GetDirectoryName(configuredDirectory);
            }

            // A Legacy copy the player keeps on another drive wins over anything found nearby.
            Take(state, customLegacyDirectory, GameEdition.Legacy, false, true);

            if (!string.IsNullOrEmpty(common))
            {
                Take(state, Path.Combine(common, FolderNameFor(GameEdition.Enhanced)), null, false, false);
                Take(state, Path.Combine(common, FolderNameFor(GameEdition.Legacy)), null, false, false);
            }

            // Steam knows which branch its own folder holds.
            Take(state, state.SteamManagedDirectory, state.SteamBranchEdition, true, false);

            Take(state, configuredDirectory, null, false, false);

            return state;
        }

        private static void Take(
            GameEditionState state,
            string directory,
            GameEdition? known,
            bool steamManaged,
            bool custom)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            string full;
            try
            {
                full = Path.GetFullPath(directory.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
            catch
            {
                return;
            }

            if (!Directory.Exists(full) || !LooksLikeInstall(full))
            {
                return;
            }

            var probed = ProbeEdition(full);

            // A hand-picked Legacy folder that turns out to hold Enhanced is simply wrong,
            // and taking it anyway would hide the real install behind it.
            if (custom && probed != null && probed != known)
            {
                return;
            }

            // Steam's branch key says what its own folder holds; everywhere else the files
            // on disk decide, because a folder name or a stored path can be out of date.
            var edition = steamManaged
                ? known ?? EditionFromFolderName(full) ?? probed
                : probed ?? EditionFromFolderName(full) ?? known;

            if (edition == null)
            {
                return;
            }

            if (state.Get(edition.Value) != null)
            {
                return;
            }

            // One folder cannot be both builds at once.
            if (SamePath(full, state.Enhanced) || SamePath(full, state.Legacy))
            {
                return;
            }

            state.Set(edition.Value, new EditionInstall
            {
                Edition = edition.Value,
                InstallDirectory = full,
                ExePath = ResolveExePath(full),
                IsSteamManaged = steamManaged,
                IsCustomPath = custom
            });
        }

        private static bool SamePath(string directory, EditionInstall install)
        {
            return install != null &&
                   string.Equals(install.InstallDirectory, directory, StringComparison.OrdinalIgnoreCase);
        }

        public static bool LooksLikeInstall(string installDirectory)
        {
            if (string.IsNullOrWhiteSpace(installDirectory))
            {
                return false;
            }

            try
            {
                return File.Exists(Path.Combine(installDirectory, RootExeName)) ||
                       ProbeEdition(installDirectory) != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Tells the builds apart by their shipping executable: Enhanced is a newer engine.</summary>
        public static GameEdition? ProbeEdition(string installDirectory)
        {
            if (string.IsNullOrWhiteSpace(installDirectory))
            {
                return null;
            }

            try
            {
                var binaries = Path.Combine(installDirectory, SandboxFolderName, "Binaries", "Win64");

                if (File.Exists(Path.Combine(binaries, EnhancedExeName)))
                {
                    return GameEdition.Enhanced;
                }

                if (File.Exists(Path.Combine(binaries, LegacyExeName)))
                {
                    return GameEdition.Legacy;
                }
            }
            catch
            {
            }

            return null;
        }

        private static GameEdition? EditionFromFolderName(string installDirectory)
        {
            string name;
            try
            {
                name = new DirectoryInfo(installDirectory).Name;
            }
            catch
            {
                return null;
            }

            if (name.EndsWith(" " + DisplayName(GameEdition.Enhanced), StringComparison.OrdinalIgnoreCase))
            {
                return GameEdition.Enhanced;
            }

            if (name.EndsWith(" " + DisplayName(GameEdition.Legacy), StringComparison.OrdinalIgnoreCase))
            {
                return GameEdition.Legacy;
            }

            return null;
        }

        /// <summary>Walks up from any game executable to the folder that holds ConanSandbox.</summary>
        public static string ResolveInstallDirectory(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath))
            {
                return null;
            }

            string directory;
            try
            {
                directory = Path.GetDirectoryName(Path.GetFullPath(exePath.Trim()));
            }
            catch
            {
                return null;
            }

            var current = directory;
            while (!string.IsNullOrEmpty(current))
            {
                if (Directory.Exists(Path.Combine(current, SandboxFolderName, "Binaries")) ||
                    File.Exists(Path.Combine(current, RootExeName)))
                {
                    return current;
                }

                var parent = Directory.GetParent(current);
                current = parent != null ? parent.FullName : null;
            }

            return directory;
        }

        public static string ResolveExePath(string installDirectory)
        {
            if (string.IsNullOrWhiteSpace(installDirectory))
            {
                return null;
            }

            // The launcher stores the shim in the install root, same as Steam starts it.
            var root = Path.Combine(installDirectory, RootExeName);
            if (File.Exists(root))
            {
                return root;
            }

            var binaries = Path.Combine(installDirectory, SandboxFolderName, "Binaries", "Win64");
            foreach (var name in new[] { EnhancedExeName, LegacyExeName, BattlEyeExeName })
            {
                var candidate = Path.Combine(binaries, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return root;
        }

        public static bool IsGameRunning()
        {
            foreach (var name in GameProcessNames)
            {
                Process[] running;
                try
                {
                    running = Process.GetProcessesByName(name);
                }
                catch
                {
                    continue;
                }

                try
                {
                    if (running.Length > 0)
                    {
                        return true;
                    }
                }
                finally
                {
                    foreach (var process in running)
                    {
                        process.Dispose();
                    }
                }
            }

            return false;
        }

        /// <summary>True while Steam is still writing into the folder we are about to rename.</summary>
        public static bool IsGameDownloading(string installDirectory)
        {
            var manifest = FindAppManifest(installDirectory);
            if (manifest == null)
            {
                return false;
            }

            try
            {
                var match = Regex.Match(File.ReadAllText(manifest), "\"StateFlags\"\\s*\"(\\d+)\"");
                int flags;
                if (!match.Success || !int.TryParse(match.Groups[1].Value, out flags))
                {
                    return false;
                }

                return (flags & DownloadingStateMask) != 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>A launcher living inside the game folder cannot survive the rename.</summary>
        public static bool IsLauncherInside(string installDirectory)
        {
            if (string.IsNullOrWhiteSpace(installDirectory))
            {
                return false;
            }

            try
            {
                var launcher = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var game = Path.GetFullPath(installDirectory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                return launcher.Equals(game, StringComparison.OrdinalIgnoreCase) ||
                       launcher.StartsWith(game + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string FindSteamappsDirectory(string installDirectory)
        {
            if (string.IsNullOrWhiteSpace(installDirectory))
            {
                return null;
            }

            try
            {
                var current = new DirectoryInfo(Path.GetFullPath(installDirectory));
                while (current != null)
                {
                    if (string.Equals(current.Name, "steamapps", StringComparison.OrdinalIgnoreCase))
                    {
                        return current.FullName;
                    }

                    current = current.Parent;
                }
            }
            catch
            {
            }

            return null;
        }

        private static string AppManifestPath(string steamappsDirectory)
        {
            return Path.Combine(steamappsDirectory, "appmanifest_" + SteamLocator.ConanAppId + ".acf");
        }

        /// <summary>The library folder Steam asks for when it offers to install the game again.</summary>
        public static string FindLibraryRoot(string installDirectory)
        {
            var steamapps = FindSteamappsDirectory(installDirectory);
            if (steamapps == null)
            {
                return null;
            }

            var parent = Directory.GetParent(steamapps);
            return parent != null ? parent.FullName : null;
        }

        public static string FindAppManifest(string installDirectory)
        {
            var steamapps = FindSteamappsDirectory(installDirectory);
            if (steamapps == null)
            {
                return null;
            }

            var manifest = AppManifestPath(steamapps);
            return File.Exists(manifest) ? manifest : null;
        }

        /// <summary>True once the setup has moved the manifest aside, i.e. the rename already happened.</summary>
        public static bool HasBackedUpAppManifest(string installDirectory)
        {
            var steamapps = FindSteamappsDirectory(installDirectory);
            if (steamapps == null)
            {
                return false;
            }

            try
            {
                return File.Exists(AppManifestPath(steamapps) + ".bak");
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Moves the current install aside so Steam can download the other build into
        /// the default folder, and takes the manifest with it: left in place, Steam would
        /// patch an install that is no longer there.
        /// </summary>
        public static DualInstallPrepareResult PrepareDualInstall(
            string installDirectory,
            GameEdition edition,
            Action<string> log)
        {
            var result = new DualInstallPrepareResult();

            if (string.IsNullOrWhiteSpace(installDirectory) || !Directory.Exists(installDirectory))
            {
                result.Error = "Папка игры не найдена:\n" + (installDirectory ?? string.Empty);
                return result;
            }

            var destination = DestinationFor(installDirectory, edition);
            if (string.IsNullOrEmpty(destination))
            {
                result.Error = "Не удалось определить, куда переименовать папку игры.";
                return result;
            }

            if (Directory.Exists(destination))
            {
                // Already moved on an earlier run.
                result.Renamed = true;
                return result;
            }

            var manifest = FindAppManifest(installDirectory);

            try
            {
                Directory.Move(installDirectory, destination);
            }
            catch (Exception ex)
            {
                result.Error =
                    "Не удалось переименовать папку: " + ex.Message +
                    "\n\nФайл внутри папки может быть занят Steam или другой программой. " +
                    "Закройте игру и Steam, либо переименуйте папку вручную.";
                return result;
            }

            result.Renamed = true;
            if (log != null)
            {
                log("Папка игры переименована: " + destination);
            }

            if (manifest != null)
            {
                var backup = manifest + ".bak";
                try
                {
                    if (File.Exists(backup))
                    {
                        File.Delete(backup);
                    }

                    File.Move(manifest, backup);

                    if (log != null)
                    {
                        log("Манифест Steam убран в " + Path.GetFileName(backup) + ".");
                    }
                }
                catch (Exception ex)
                {
                    result.Warning =
                        "Не удалось убрать файл " + Path.GetFileName(manifest) + ": " + ex.Message +
                        "\nПродолжить можно, но Steam, возможно, придётся перезапустить.";
                }
            }

            // The cache stores absolute paths to mods and is wrong after the move.
            var modControllerCache = Path.Combine(destination, SandboxFolderName, "Saved", "ModControllerCache.json");
            if (File.Exists(modControllerCache))
            {
                try
                {
                    File.Delete(modControllerCache);
                }
                catch (Exception ex)
                {
                    var warning =
                        "Не удалось удалить ModControllerCache.json: " + ex.Message +
                        "\nЕсли моды будут вести себя странно, удалите его вручную из ConanSandbox\\Saved.";

                    result.Warning = string.IsNullOrEmpty(result.Warning)
                        ? warning
                        : result.Warning + "\n\n" + warning;
                }
            }

            return result;
        }

        public static string DestinationFor(string installDirectory, GameEdition edition)
        {
            if (string.IsNullOrWhiteSpace(installDirectory))
            {
                return null;
            }

            var parent = Path.GetDirectoryName(installDirectory.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            return string.IsNullOrEmpty(parent)
                ? null
                : Path.Combine(parent, FolderNameFor(edition));
        }

        public static IEnumerable<EditionInstall> All(GameEditionState state)
        {
            if (state == null)
            {
                return Enumerable.Empty<EditionInstall>();
            }

            return new[] { state.Enhanced, state.Legacy }.Where(install => install != null);
        }
    }
}
