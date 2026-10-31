using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace RealmLauncher.Services
{
    public sealed class PresetFolder
    {
        public string Path { get; set; }
        public bool Exists { get; set; }
        public int FileCount { get; set; }
        public long TotalBytes { get; set; }
        public DateTime NewestWriteUtc { get; set; }

        public bool HasPresets
        {
            get { return Exists && FileCount > 0; }
        }
    }

    public sealed class PresetSyncPlan
    {
        public PresetSyncPlan()
        {
            Added = new List<string>();
            Replaced = new List<string>();
            TargetOnly = new List<string>();
        }

        public GameEdition SourceEdition { get; set; }
        public GameEdition TargetEdition { get; set; }
        public PresetFolder Source { get; set; }
        public PresetFolder Target { get; set; }

        /// <summary>Files the target does not have at all.</summary>
        public List<string> Added { get; private set; }

        /// <summary>Files both sides have with different content.</summary>
        public List<string> Replaced { get; private set; }

        /// <summary>Files only the target has; they are never touched.</summary>
        public List<string> TargetOnly { get; private set; }

        public bool HasWork
        {
            get { return Added.Count > 0 || Replaced.Count > 0; }
        }

        /// <summary>Nothing can be lost when only new files are added, so that runs unattended.</summary>
        public bool NeedsConfirmation
        {
            get { return Replaced.Count > 0; }
        }
    }

    public sealed class PresetSyncResult
    {
        public int Copied { get; set; }
        public string BackupPath { get; set; }
        public string Error { get; set; }

        public bool Ok
        {
            get { return string.IsNullOrEmpty(Error); }
        }
    }

    /// <summary>
    /// Moves TotCustom appearance presets between two game installs. Presets are what
    /// players care about most, so nothing here deletes or moves a preset file: the
    /// target is copied into a backup before a single byte of it is overwritten.
    /// </summary>
    public static class TotCustomPresets
    {
        public const string FolderName = "TotCustom";

        private static readonly string[] SaveGamesParts = { "ConanSandbox", "Saved", "SaveGames" };

        // The mod keeps its own rolling backups in a Backups subfolder. They are the bulk
        // of the folder and each build writes its own, so only the top level ever travels.
        private const int KeptBackups = 10;

        public static string PathFor(string installDirectory)
        {
            if (string.IsNullOrWhiteSpace(installDirectory))
            {
                return null;
            }

            var path = installDirectory;
            foreach (var part in SaveGamesParts)
            {
                path = Path.Combine(path, part);
            }

            return Path.Combine(path, FolderName);
        }

        public static PresetFolder Inspect(string installDirectory)
        {
            var folder = new PresetFolder { Path = PathFor(installDirectory) };
            if (string.IsNullOrEmpty(folder.Path) || !Directory.Exists(folder.Path))
            {
                return folder;
            }

            folder.Exists = true;
            foreach (var file in RootFiles(folder.Path))
            {
                folder.FileCount++;
                folder.TotalBytes += file.Length;
                if (file.LastWriteTimeUtc > folder.NewestWriteUtc)
                {
                    folder.NewestWriteUtc = file.LastWriteTimeUtc;
                }
            }

            return folder;
        }

        public static PresetSyncPlan Plan(EditionInstall source, EditionInstall target)
        {
            var plan = new PresetSyncPlan();
            if (source == null || target == null)
            {
                return plan;
            }

            plan.SourceEdition = source.Edition;
            plan.TargetEdition = target.Edition;
            plan.Source = Inspect(source.InstallDirectory);
            plan.Target = Inspect(target.InstallDirectory);

            if (!plan.Source.HasPresets)
            {
                return plan;
            }

            var targetFiles = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
            if (plan.Target.Exists)
            {
                foreach (var file in RootFiles(plan.Target.Path))
                {
                    targetFiles[file.Name] = file;
                }
            }

            var sourceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in RootFiles(plan.Source.Path))
            {
                sourceNames.Add(file.Name);

                FileInfo existing;
                if (!targetFiles.TryGetValue(file.Name, out existing))
                {
                    plan.Added.Add(file.Name);
                    continue;
                }

                if (!SameContent(file, existing))
                {
                    plan.Replaced.Add(file.Name);
                }
            }

            foreach (var name in targetFiles.Keys)
            {
                if (!sourceNames.Contains(name))
                {
                    plan.TargetOnly.Add(name);
                }
            }

            plan.Added.Sort(StringComparer.OrdinalIgnoreCase);
            plan.Replaced.Sort(StringComparer.OrdinalIgnoreCase);
            plan.TargetOnly.Sort(StringComparer.OrdinalIgnoreCase);
            return plan;
        }

        public static PresetSyncResult Apply(PresetSyncPlan plan, Action<string> log)
        {
            var result = new PresetSyncResult();
            if (plan == null || !plan.HasWork)
            {
                return result;
            }

            try
            {
                // Back up what is already there before anything is written over it.
                if (plan.Target.HasPresets)
                {
                    result.BackupPath = Backup(plan.TargetEdition, plan.Target.Path, log);
                }

                Directory.CreateDirectory(plan.Target.Path);

                foreach (var name in plan.Added.Concat(plan.Replaced))
                {
                    File.Copy(
                        Path.Combine(plan.Source.Path, name),
                        Path.Combine(plan.Target.Path, name),
                        true);
                    result.Copied++;
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Copies the live presets of an install into the launcher's own folder, which
        /// survives a game reinstall or a Steam file verification.
        /// </summary>
        public static string Backup(GameEdition edition, string presetPath, Action<string> log)
        {
            if (string.IsNullOrEmpty(presetPath) || !Directory.Exists(presetPath))
            {
                return null;
            }

            var root = Path.Combine(BackupRoot, GameEditions.DisplayName(edition));
            var stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
            var destination = Path.Combine(root, stamp);
            Directory.CreateDirectory(destination);

            var copied = 0;
            foreach (var file in RootFiles(presetPath))
            {
                file.CopyTo(Path.Combine(destination, file.Name), true);
                copied++;
            }

            if (log != null)
            {
                log(string.Format(
                    "Внешности {0} сохранены в резервную копию ({1} файл(ов)): {2}",
                    GameEditions.DisplayName(edition), copied, destination));
            }

            PruneBackups(root);
            return destination;
        }

        public static string BackupRoot
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RealmLauncher",
                    "PresetBackups");
            }
        }

        private static void PruneBackups(string editionRoot)
        {
            try
            {
                var folders = new DirectoryInfo(editionRoot)
                    .GetDirectories()
                    .OrderByDescending(d => d.Name, StringComparer.Ordinal)
                    .Skip(KeptBackups)
                    .ToList();

                foreach (var folder in folders)
                {
                    folder.Delete(true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static FileInfo[] RootFiles(string path)
        {
            // Top level only, which is what leaves the mod's own Backups subfolder where it is.
            try
            {
                return new DirectoryInfo(path).GetFiles("*.sav", SearchOption.TopDirectoryOnly);
            }
            catch (IOException)
            {
                return new FileInfo[0];
            }
            catch (UnauthorizedAccessException)
            {
                return new FileInfo[0];
            }
        }

        private static bool SameContent(FileInfo left, FileInfo right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            try
            {
                return Sha256(left.FullName).SequenceEqual(Sha256(right.FullName));
            }
            catch (IOException)
            {
                // Unreadable means "treat as different" would overwrite on a guess, so keep it.
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
        }

        private static byte[] Sha256(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
            {
                return sha.ComputeHash(stream);
            }
        }

        public static string FormatSize(long bytes)
        {
            if (bytes <= 0)
            {
                return "0 B";
            }

            var units = new[] { "B", "KB", "MB", "GB" };
            var size = (double)bytes;
            var unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }

            return string.Format(CultureInfo.CurrentCulture, "{0:0.##} {1}", size, units[unit]);
        }

        public static string Describe(PresetFolder folder)
        {
            if (folder == null || !folder.HasPresets)
            {
                return "внешностей нет";
            }

            return string.Format(
                CultureInfo.CurrentCulture,
                "{0} файл(ов), {1}, последнее изменение {2}",
                folder.FileCount,
                FormatSize(folder.TotalBytes),
                folder.NewestWriteUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture));
        }
    }
}
