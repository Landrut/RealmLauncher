using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace RealmLauncher.Services
{
    public static class GameConfigService
    {
        private const string SavedServersSection = "SavedServers";
        private const string SavedCoopSection = "SavedCoopData";
        private const string LastConnectedKey = "LastConnected";
        private const string ListenSessionKey = "StartedListenServerSession";

        private static readonly string[] ConfigDirectoryNames = { "Windows", "WindowsNoEditor" };

        public static string ResolveSandboxDirectory(string conanExePath)
        {
            if (string.IsNullOrWhiteSpace(conanExePath))
            {
                throw new InvalidOperationException("Не указан путь к ConanSandbox.exe.");
            }

            var exeDirectoryPath = Path.GetDirectoryName(conanExePath) ?? string.Empty;
            var exeDirectory = new DirectoryInfo(exeDirectoryPath);

            if (string.Equals(exeDirectory.Name, "ConanSandbox", StringComparison.OrdinalIgnoreCase))
            {
                return exeDirectory.FullName;
            }

            var nested = Path.Combine(exeDirectory.FullName, "ConanSandbox");
            if (Directory.Exists(nested))
            {
                return nested;
            }

            var current = exeDirectory.Parent;
            while (current != null)
            {
                if (string.Equals(current.Name, "ConanSandbox", StringComparison.OrdinalIgnoreCase))
                {
                    return current.FullName;
                }
                current = current.Parent;
            }

            throw new InvalidOperationException("Не удалось найти папку ConanSandbox рядом с указанным файлом игры.");
        }

        public static string GetGameRoot(string conanExePath)
        {
            var sandbox = ResolveSandboxDirectory(conanExePath);
            var parent = Directory.GetParent(sandbox);
            return parent != null ? parent.FullName : Path.GetDirectoryName(conanExePath);
        }

        public static string GetLogsDirectory(string conanExePath)
        {
            return Path.Combine(ResolveSandboxDirectory(conanExePath), "Saved", "Logs");
        }

        private static List<string> GetExistingConfigDirectories(string sandboxDirectory)
        {
            var savedConfig = Path.Combine(sandboxDirectory, "Saved", "Config");
            return ConfigDirectoryNames
                .Select(name => Path.Combine(savedConfig, name))
                .Where(Directory.Exists)
                .ToList();
        }

        public static void SetLastConnectedServer(string conanExePath, string serverIp, Action<string> log)
        {
            var sandboxDirectory = ResolveSandboxDirectory(conanExePath);
            var targets = GetExistingConfigDirectories(sandboxDirectory);

            if (targets.Count == 0)
            {
                var fallback = Path.Combine(sandboxDirectory, "Saved", "Config", ConfigDirectoryNames[0]);
                Directory.CreateDirectory(fallback);
                targets.Add(fallback);
            }

            var written = 0;
            foreach (var directory in targets)
            {
                var gameIniPath = Path.Combine(directory, "Game.ini");
                try
                {
                    var lines = File.Exists(gameIniPath)
                        ? File.ReadAllLines(gameIniPath).ToList()
                        : new List<string>();

                    var changed = false;
                    changed |= UpsertKeyInSection(lines, SavedServersSection, LastConnectedKey, serverIp);
                    changed |= UpsertKeyInSection(lines, SavedCoopSection, ListenSessionKey, "False");

                    if (changed)
                    {
                        File.WriteAllLines(gameIniPath, lines);
                    }

                    written++;
                }
                catch (Exception ex)
                {
                    log?.Invoke("Не удалось записать адрес сервера в " + gameIniPath + ": " + ex.Message);
                }
            }

            if (written > 0)
            {
                log?.Invoke(string.Format("Адрес сервера прописан в Game.ini ({0}): {1}", written, serverIp));
            }
        }

        private static bool UpsertKeyInSection(List<string> lines, string section, string key, string value)
        {
            var target = key + "=" + value;
            var sectionHeader = "[" + section + "]";
            var sectionRegex = new Regex(@"^\s*\[" + Regex.Escape(section) + @"\]\s*$", RegexOptions.IgnoreCase);
            var anySectionRegex = new Regex(@"^\s*\[.+\]\s*$");
            var keyRegex = new Regex(@"^\s*" + Regex.Escape(key) + @"\s*=", RegexOptions.IgnoreCase);

            var sectionStart = -1;
            for (var i = 0; i < lines.Count; i++)
            {
                if (sectionRegex.IsMatch(lines[i]))
                {
                    sectionStart = i;
                    break;
                }
            }

            if (sectionStart < 0)
            {
                if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[lines.Count - 1]))
                {
                    lines.Add(string.Empty);
                }
                lines.Add(sectionHeader);
                lines.Add(target);
                return true;
            }

            var sectionEnd = lines.Count;
            for (var i = sectionStart + 1; i < lines.Count; i++)
            {
                if (anySectionRegex.IsMatch(lines[i]))
                {
                    sectionEnd = i;
                    break;
                }
            }

            for (var i = sectionStart + 1; i < sectionEnd; i++)
            {
                if (!keyRegex.IsMatch(lines[i]))
                {
                    continue;
                }

                if (string.Equals(lines[i].Trim(), target, StringComparison.Ordinal))
                {
                    return false;
                }

                lines[i] = target;
                return true;
            }

            lines.Insert(sectionStart + 1, target);
            return true;
        }

        public static string ResolveLaunchExe(string conanExePath, bool useBattlEye)
        {
            var sandboxDirectory = ResolveSandboxDirectory(conanExePath);
            var binaries = Path.Combine(sandboxDirectory, "Binaries", "Win64");

            var battlEye = Path.Combine(binaries, "ConanSandbox_BE.exe");
            var shipping = Path.Combine(binaries, "ConanSandbox-Win64-Shipping.exe");
            var legacyDirect = Path.Combine(binaries, "ConanSandbox.exe");

            var order = useBattlEye
                ? new[] { battlEye, shipping, legacyDirect, conanExePath }
                : new[] { shipping, legacyDirect, battlEye, conanExePath };

            return order.FirstOrDefault(File.Exists) ?? conanExePath;
        }
    }
}
