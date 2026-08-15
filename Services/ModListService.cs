using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using RealmLauncher.Models;

namespace RealmLauncher.Services
{
    public sealed class ModListExport
    {
        [JsonProperty("format")]
        public string Format { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("exportedUtc")]
        public DateTime ExportedUtc { get; set; }

        [JsonProperty("mods")]
        public List<string> Mods { get; set; }

        public ModListExport()
        {
            Format = "realm-modlist-1";
            Mods = new List<string>();
        }
    }

    public static class ModListService
    {
        private static readonly Regex ModIdPattern = new Regex(@"(\d{6,})", RegexOptions.Compiled);

        public static string ParseModId(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return null;
            }

            var text = input.Trim();

            var slash = text.IndexOf('/');
            if (slash > 0 && text.Substring(0, slash).All(char.IsDigit))
            {
                return text.Substring(0, slash);
            }

            if (text.All(char.IsDigit))
            {
                return text;
            }

            var match = Regex.Match(text, @"[?&]id=(\d+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            match = ModIdPattern.Match(text);
            return match.Success ? match.Groups[1].Value : null;
        }

        public static string FindPakName(string workshopContentRoot, string modId)
        {
            try
            {
                var directory = Path.Combine(workshopContentRoot, modId);
                if (!Directory.Exists(directory))
                {
                    return null;
                }

                var pak = Directory.GetFiles(directory, "*.pak", SearchOption.AllDirectories)
                    .OrderByDescending(f => new FileInfo(f).Length)
                    .FirstOrDefault();

                return pak != null ? Path.GetFileName(pak) : null;
            }
            catch
            {
                return null;
            }
        }

        public static string ToEntry(ModUpdateInfo mod)
        {
            return mod == null ? null : mod.ModId + "/" + mod.PakName;
        }

        public static List<string> ToEntries(IEnumerable<ModUpdateInfo> mods)
        {
            return (mods ?? Enumerable.Empty<ModUpdateInfo>())
                .Where(m => m != null && !string.IsNullOrWhiteSpace(m.ModId) && !string.IsNullOrWhiteSpace(m.PakName))
                .Select(ToEntry)
                .ToList();
        }

        public static string WriteModList(string conanExePath, IEnumerable<string> entries, Action<string> log)
        {
            if (string.IsNullOrWhiteSpace(conanExePath) || !File.Exists(conanExePath))
            {
                throw new InvalidOperationException("Не найден ConanSandbox.exe. Укажите путь к игре в настройках.");
            }

            var sandbox = GameConfigService.ResolveSandboxDirectory(conanExePath);
            var modsDirectory = Path.Combine(sandbox, "Mods");
            Directory.CreateDirectory(modsDirectory);

            var workshopRoot = LauncherService.ResolveWorkshopContentRoot(conanExePath);
            var lines = new List<string>();
            var missing = 0;

            foreach (var entry in entries ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(entry))
                {
                    continue;
                }

                var parts = entry.Split(new[] { '/' }, 2);
                if (parts.Length != 2 || !parts[0].Trim().All(char.IsDigit))
                {
                    continue;
                }

                var fullPath = Path.Combine(workshopRoot, parts[0].Trim(), parts[1].Trim());
                lines.Add(fullPath);

                if (!File.Exists(fullPath))
                {
                    missing++;
                }
            }

            var modListPath = Path.Combine(modsDirectory, "modlist.txt");
            File.WriteAllLines(modListPath, lines);

            log?.Invoke(missing > 0
                ? string.Format("modlist.txt записан ({0} строк), не найдено файлов: {1}", lines.Count, missing)
                : string.Format("modlist.txt записан ({0} строк).", lines.Count));

            return modListPath;
        }

        public static string BuildServerModIds(IEnumerable<string> entries)
        {
            var ids = (entries ?? Enumerable.Empty<string>())
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e => e.Split('/')[0].Trim())
                .Where(id => id.Length > 0 && id.All(char.IsDigit))
                .ToList();

            if (ids.Count == 0)
            {
                return string.Empty;
            }

            return string.Join(",", ids) + ",";
        }

        public static void ExportToFile(string path, string name, IEnumerable<string> entries)
        {
            var payload = new ModListExport
            {
                Name = name,
                ExportedUtc = DateTime.UtcNow,
                Mods = (entries ?? Enumerable.Empty<string>()).Where(e => !string.IsNullOrWhiteSpace(e)).ToList()
            };

            File.WriteAllText(path, JsonConvert.SerializeObject(payload, Formatting.Indented), new UTF8Encoding(false));
        }

        public static List<string> ImportFromFile(string path)
        {
            var text = File.ReadAllText(path);
            var trimmed = text.TrimStart();

            if (trimmed.StartsWith("{"))
            {
                var payload = JsonConvert.DeserializeObject<ModListExport>(text);
                if (payload == null || payload.Mods == null)
                {
                    throw new InvalidOperationException("Файл не содержит списка модов.");
                }

                return payload.Mods
                    .Select(NormalizeEntry)
                    .Where(e => e != null)
                    .ToList();
            }

            return text
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(NormalizeEntry)
                .Where(e => e != null)
                .ToList();
        }

        private static string NormalizeEntry(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            var value = raw.Trim().Replace('/', '\\');
            var parts = value.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return null;
            }

            var pak = parts[parts.Length - 1];
            if (!pak.EndsWith(".pak", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            for (var i = parts.Length - 2; i >= 0; i--)
            {
                if (parts[i].Length > 0 && parts[i].All(char.IsDigit))
                {
                    return parts[i] + "/" + pak;
                }
            }

            return null;
        }
    }
}
