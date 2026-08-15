using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace RealmLauncher.Services
{
    public static class GameConfigService
    {
        private const string SavedServersSection = "SavedServers";
        private const string SavedCoopSection = "SavedCoopData";
        private const string LastConnectedKey = "LastConnected";
        private const string LastPasswordKey = "LastPassword";
        private const string ListenSessionKey = "StartedListenServerSession";
        private const string ServerModListPrefix = "ServerModList=";
        private const string ModMismatchSection = "Settings.ModMismatch";
        private const string AutoRestartKey = "bAutoRestart";
        private const string AutoConnectKey = "AutoConnect";
        private const string FavoriteServersSection = "FavoriteServers";
        private const string FavoriteServersKey = "ServersList";

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

        public static void SetLastConnectedServer(
            string conanExePath, string serverIp, string serverPassword, Action<string> log)
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
                    changed |= UpsertKeyInSection(
                        lines, SavedServersSection, LastPasswordKey, serverPassword ?? string.Empty);
                    changed |= UpsertKeyInSection(lines, SavedCoopSection, ListenSessionKey, "False");
                    changed |= UpsertKeyInSection(lines, ModMismatchSection, AutoRestartKey, "True");
                    changed |= UpsertKeyInSection(lines, ModMismatchSection, AutoConnectKey, "True");

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

        public static void RemoveServerModListEntry(string conanExePath, Action<string> log)
        {
            var sandboxDirectory = ResolveSandboxDirectory(conanExePath);

            foreach (var directory in GetExistingConfigDirectories(sandboxDirectory))
            {
                var path = Path.Combine(directory, "ServerSettings.ini");
                if (!File.Exists(path))
                {
                    continue;
                }

                try
                {
                    var lines = File.ReadAllLines(path);
                    var filtered = lines
                        .Where(line => !line.TrimStart().StartsWith(ServerModListPrefix, StringComparison.OrdinalIgnoreCase))
                        .ToArray();

                    if (filtered.Length != lines.Length)
                    {
                        File.WriteAllLines(path, filtered);
                        log?.Invoke("ServerModList очищен в " + Path.GetFileName(path) + ".");
                    }
                }
                catch (Exception ex)
                {
                    log?.Invoke("Не удалось очистить ServerModList в " + path + ": " + ex.Message);
                }
            }
        }

        public static void EnsureFavoriteServer(
            string conanExePath, string serverName, string serverIp, Action<string> log)
        {
            string host;
            int port;
            if (!TrySplitAddress(serverIp, out host, out port))
            {
                return;
            }

            var name = (serverName ?? string.Empty).Replace("\"", string.Empty).Trim();
            if (name.Length == 0)
            {
                name = host;
            }

            var entry = string.Format(
                "{0}=(ServerName=\"{1}\",ipAddress=\"{2}\",Port={3})", FavoriteServersKey, name, host, port);
            var hostMarker = "ipaddress=\"" + host.ToLowerInvariant() + "\"";
            var sandboxDirectory = ResolveSandboxDirectory(conanExePath);

            foreach (var directory in GetExistingConfigDirectories(sandboxDirectory))
            {
                var gameIniPath = Path.Combine(directory, "Game.ini");

                try
                {
                    var lines = File.Exists(gameIniPath)
                        ? File.ReadAllLines(gameIniPath).ToList()
                        : new List<string>();

                    if (AppendLineToSection(lines, FavoriteServersSection, entry, hostMarker))
                    {
                        File.WriteAllLines(gameIniPath, lines);
                        log?.Invoke("Сервер добавлен в избранное игры: " + name);
                    }
                }
                catch (Exception ex)
                {
                    log?.Invoke("Не удалось добавить сервер в избранное в " + gameIniPath + ": " + ex.Message);
                }
            }
        }

        private static bool TrySplitAddress(string serverIp, out string host, out int port)
        {
            host = null;
            port = 0;

            var value = (serverIp ?? string.Empty).Trim();
            var separator = value.LastIndexOf(':');
            if (separator <= 0 || separator == value.Length - 1)
            {
                return false;
            }

            if (!int.TryParse(value.Substring(separator + 1), out port) || port <= 0 || port > 65535)
            {
                return false;
            }

            host = value.Substring(0, separator).Trim();
            return host.Length > 0;
        }

        private static bool AppendLineToSection(
            List<string> lines, string section, string entry, string skipIfContains)
        {
            var sectionHeader = "[" + section + "]";
            var sectionRegex = new Regex(@"^\s*\[" + Regex.Escape(section) + @"\]\s*$", RegexOptions.IgnoreCase);
            var anySectionRegex = new Regex(@"^\s*\[.+\]\s*$");

            var sectionStart = lines.FindIndex(line => sectionRegex.IsMatch(line));
            if (sectionStart < 0)
            {
                if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[lines.Count - 1]))
                {
                    lines.Add(string.Empty);
                }

                lines.Add(sectionHeader);
                lines.Add(entry);
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

            var insertAt = sectionStart + 1;
            for (var i = sectionStart + 1; i < sectionEnd; i++)
            {
                if (lines[i].ToLowerInvariant().Contains(skipIfContains))
                {
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(lines[i]))
                {
                    insertAt = i + 1;
                }
            }

            lines.Insert(insertAt, entry);
            return true;
        }

        public static string GetServerModListPath(string conanExePath)
        {
            return Path.Combine(ResolveSandboxDirectory(conanExePath), "servermodlist.txt");
        }

        public static void WriteModRestartData(
            string conanExePath, string serverIp, string serverPassword, Action<string> log)
        {
            var sandboxDirectory = ResolveSandboxDirectory(conanExePath);
            var savedDirectory = Path.Combine(sandboxDirectory, "Saved");
            var path = Path.Combine(savedDirectory, "ModRestartData.json");

            try
            {
                Directory.CreateDirectory(savedDirectory);

                var data = new Dictionary<string, string>
                {
                    { "ServerAddress", serverIp ?? string.Empty },
                    { "ServerPassword", serverPassword ?? string.Empty }
                };

                var serverModListPath = GetServerModListPath(conanExePath);
                if (File.Exists(serverModListPath))
                {
                    data["ModList"] = serverModListPath.Replace('\\', '/');
                }

                File.WriteAllText(path, JsonConvert.SerializeObject(data), new UTF8Encoding(false));
                log?.Invoke("ModRestartData.json обновлён: " + serverIp);
            }
            catch (Exception ex)
            {
                log?.Invoke("Не удалось записать ModRestartData.json: " + ex.Message);
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
