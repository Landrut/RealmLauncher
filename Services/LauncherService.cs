using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RealmLauncher.Models;

namespace RealmLauncher.Services
{
    public sealed class LauncherService
    {
        private const int ConanSteamAppId = 440900;
        private const string SteamCmdZipUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip";
        private const string WorkshopApiUrl = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/";
        private static readonly HttpClient HttpClient = new HttpClient();

        public LauncherService()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        public string GetSteamCmdDirectory()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "steamcmd");
        }

        public string GetSteamCmdPath()
        {
            return Path.Combine(GetSteamCmdDirectory(), "steamcmd.exe");
        }

        public bool IsSteamCmdInstalled()
        {
            return File.Exists(GetSteamCmdPath());
        }

        public async Task InstallSteamCmdAsync(Action<string> log, CancellationToken cancellationToken)
        {
            var steamCmdDirectory = GetSteamCmdDirectory();
            var zipPath = Path.Combine(steamCmdDirectory, "steamcmd.zip");

            Directory.CreateDirectory(steamCmdDirectory);

            log("Скачиваю SteamCMD...");
            using (var response = await HttpClient.GetAsync(SteamCmdZipUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();

                using (var downloadStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await downloadStream.CopyToAsync(fileStream).ConfigureAwait(false);
                }
            }

            log("Распаковываю SteamCMD...");
            ExtractZipOverwrite(zipPath, steamCmdDirectory);

            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            if (!IsSteamCmdInstalled())
            {
                throw new InvalidOperationException("Установка SteamCMD не завершилась: steamcmd.exe не найден после распаковки.");
            }

            log("SteamCMD установлен.");
        }

        public async Task<ServerConfig> DownloadConfigAsync(string configUrl, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(configUrl))
            {
                throw new InvalidOperationException("Укажи URL до JSON конфигурации сервера.");
            }

            using (var response = await HttpClient.GetAsync(configUrl, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var config = JsonConvert.DeserializeObject<ServerConfig>(json);

                if (config == null)
                {
                    throw new InvalidOperationException("Не получилось разобрать JSON конфигурации.");
                }

                if (string.IsNullOrWhiteSpace(config.Ip))
                {
                    throw new InvalidOperationException("В JSON отсутствует поле ip.");
                }

                if (config.Mods == null)
                {
                    config.Mods = new List<string>();
                }

                return config;
            }
        }

        public List<string> ExtractModIds(IEnumerable<string> mods)
        {
            if (mods == null)
            {
                return new List<string>();
            }

            return mods
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Select(m => m.Split('/')[0].Trim())
                .Where(id => !string.IsNullOrWhiteSpace(id) && id.All(char.IsDigit))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        public async Task SyncModsAsync(
            string conanExePath,
            IEnumerable<ModUpdateInfo> modsToUpdate,
            Action<string> log,
            Action<int, int, string> progress,
            CancellationToken cancellationToken)
        {
            var steamCmdPath = GetSteamCmdPath();
            if (!File.Exists(steamCmdPath))
            {
                throw new InvalidOperationException("SteamCMD не найден. Нажми кнопку проверки и установи его.");
            }
            if (string.IsNullOrWhiteSpace(conanExePath) || !File.Exists(conanExePath))
            {
                throw new InvalidOperationException("Не найден ConanSandbox.exe. Укажи корректный путь.");
            }

            var updates = modsToUpdate != null
                ? modsToUpdate.Where(x => x != null && !string.IsNullOrWhiteSpace(x.ModId))
                    .GroupBy(x => x.ModId)
                    .Select(g => g.First())
                    .ToList()
                : new List<ModUpdateInfo>();

            if (updates.Count == 0)
            {
                log("Нет модов для обновления.");
                return;
            }

            var steamLibraryRoot = ResolveSteamLibraryRoot(conanExePath);
            log("Steam Library: " + steamLibraryRoot);
            log(string.Format("Запланировано к обновлению: {0}", updates.Count));
            log("Проверяю готовность SteamCMD...");
            await EnsureSteamCmdReadyAsync(steamCmdPath, cancellationToken).ConfigureAwait(false);

            const int batchSize = 12;
            var processed = 0;
            for (var batchStart = 0; batchStart < updates.Count; batchStart += batchSize)
            {
                var batch = updates.Skip(batchStart).Take(batchSize).ToList();
                for (var i = 0; i < batch.Count; i++)
                {
                    var item = batch[i];
                    var current = batchStart + i + 1;
                    var itemLabel = string.Format("{0}/{1}", item.ModId, item.PakName);
                    log(string.Format("Обновляю мод {0}/{1}: {2}", current, updates.Count, itemLabel));
                }

                var result = await RunSteamCmdAsync(
                    steamCmdPath,
                    BuildBatchArguments(steamLibraryRoot, batch.Select(x => x.ModId)),
                    cancellationToken).ConfigureAwait(false);

                if (result.ExitCode != 0)
                {
                    log("Пакет завершился ошибкой, перехожу к проверке модов по одному...");
                    await ProcessModsOneByOneAsync(steamCmdPath, steamLibraryRoot, updates.Count, batch, processed, log, progress, cancellationToken).ConfigureAwait(false);
                    processed += batch.Count;
                    log(string.Format("Готово: {0}/{1}", processed, updates.Count));
                    if (progress != null)
                    {
                        progress(processed, updates.Count, batch[batch.Count - 1].ModId);
                    }
                }
                else
                {
                    processed += batch.Count;
                    log(string.Format("Готово: {0}/{1}", processed, updates.Count));
                    if (progress != null)
                    {
                        progress(processed, updates.Count, batch[batch.Count - 1].ModId);
                    }
                }
            }

            log("Все моды синхронизированы.");
        }

        public string WriteModListFile(string conanExePath, IEnumerable<string> mods, Action<string> log)
        {
            if (string.IsNullOrWhiteSpace(conanExePath) || !File.Exists(conanExePath))
            {
                throw new InvalidOperationException("Не найден ConanSandbox.exe. Укажи корректный путь.");
            }

            var sandboxDirectory = ResolveConanSandboxDirectory(conanExePath);
            var modsDirectory = Path.Combine(sandboxDirectory, "Mods");
            Directory.CreateDirectory(modsDirectory);

            var workshopContentRoot = ResolveWorkshopContentRoot(conanExePath);
            log("Папка модов Workshop: " + workshopContentRoot);

            var modEntries = BuildAbsoluteModEntries(workshopContentRoot, mods, log);
            var modListPath = Path.Combine(modsDirectory, "modlist.txt");
            File.WriteAllLines(modListPath, modEntries);

            return modListPath;
        }

        public void LaunchServerConnection(string conanExePath, string serverIp)
        {
            if (string.IsNullOrWhiteSpace(conanExePath) || !File.Exists(conanExePath))
            {
                throw new InvalidOperationException("Не найден ConanSandbox.exe. Укажи корректный путь.");
            }
            if (string.IsNullOrWhiteSpace(serverIp))
            {
                throw new InvalidOperationException("IP сервера пустой.");
            }

            var safeIp = serverIp.Trim();
            TrySetLastConnected(conanExePath, safeIp);
            var launchExe = ResolvePreferredLaunchExe(conanExePath);

            Process.Start(new ProcessStartInfo
            {
                FileName = launchExe,
                Arguments = "-continuesession",
                UseShellExecute = true
            });
        }

        private static void ExtractZipOverwrite(string zipPath, string destinationDirectory)
        {
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in archive.Entries)
                {
                    var destinationPath = Path.Combine(destinationDirectory, entry.FullName);
                    var destinationFolder = Path.GetDirectoryName(destinationPath);

                    if (!string.IsNullOrWhiteSpace(destinationFolder))
                    {
                        Directory.CreateDirectory(destinationFolder);
                    }

                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        continue;
                    }

                    entry.ExtractToFile(destinationPath, true);
                }
            }
        }

        private static string ResolveConanSandboxDirectory(string conanExePath)
        {
            var exeDirectoryPath = Path.GetDirectoryName(conanExePath) ?? string.Empty;
            var exeDirectory = new DirectoryInfo(exeDirectoryPath);

            if (string.Equals(exeDirectory.Name, "ConanSandbox", StringComparison.OrdinalIgnoreCase))
            {
                return exeDirectory.FullName;
            }

            var nestedConanSandbox = Path.Combine(exeDirectory.FullName, "ConanSandbox");
            if (Directory.Exists(nestedConanSandbox))
            {
                return nestedConanSandbox;
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

            throw new InvalidOperationException("Не удалось найти папку ConanSandbox рядом с указанным ConanSandbox.exe.");
        }

        private static string ResolveSteamappsDirectory(string conanExePath)
        {
            var current = new DirectoryInfo(Path.GetDirectoryName(conanExePath) ?? string.Empty);

            while (current != null)
            {
                if (string.Equals(current.Name, "steamapps", StringComparison.OrdinalIgnoreCase))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new InvalidOperationException(
                "Не удалось определить папку steamapps от ConanSandbox.exe. " +
                "Ожидается путь внутри Steam-библиотеки, например ...\\steamapps\\common\\Conan Exiles\\...");
        }

        private static string ResolveSteamLibraryRoot(string conanExePath)
        {
            var steamappsDirectory = ResolveSteamappsDirectory(conanExePath);
            var steamapps = new DirectoryInfo(steamappsDirectory);
            if (steamapps.Parent == null)
            {
                throw new InvalidOperationException("Не удалось определить корень Steam-библиотеки.");
            }

            return steamapps.Parent.FullName;
        }

        private static string ResolveWorkshopContentRoot(string conanExePath)
        {
            var steamappsDirectory = ResolveSteamappsDirectory(conanExePath);
            return Path.Combine(steamappsDirectory, "workshop", "content", ConanSteamAppId.ToString());
        }

        private static string[] BuildAbsoluteModEntries(string workshopContentRoot, IEnumerable<string> mods, Action<string> log)
        {
            var entries = new List<string>();
            var rawMods = mods != null ? mods.Where(m => !string.IsNullOrWhiteSpace(m)).ToList() : new List<string>();

            foreach (var mod in rawMods)
            {
                var parts = mod.Split(new[] { '/' }, 2);
                if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
                {
                    continue;
                }

                var modId = parts[0].Trim();
                var pakFile = parts[1].Trim();
                var fullPath = Path.Combine(workshopContentRoot, modId, pakFile);
                entries.Add(fullPath);

                if (!File.Exists(fullPath))
                {
                    log("ВНИМАНИЕ: файл мода пока не найден: " + fullPath);
                }
            }

            return entries.ToArray();
        }

        private async Task EnsureSteamCmdReadyAsync(string steamCmdPath, CancellationToken cancellationToken)
        {
            var warmup = await RunSteamCmdAsync(steamCmdPath, "+quit", cancellationToken).ConfigureAwait(false);
            if (warmup.ExitCode == 0)
            {
                return;
            }

            await Task.Delay(1500, cancellationToken).ConfigureAwait(false);
            warmup = await RunSteamCmdAsync(steamCmdPath, "+quit", cancellationToken).ConfigureAwait(false);
            if (warmup.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    "SteamCMD не смог корректно инициализироваться. Код: " + warmup.ExitCode + Environment.NewLine +
                    "STDOUT (хвост): " + LastLines(warmup.StdOut, 20) + Environment.NewLine +
                    "STDERR (хвост): " + LastLines(warmup.StdErr, 20));
            }
        }

        private async Task<SteamCmdResult> RunSteamCmdAsync(string steamCmdPath, string arguments, CancellationToken cancellationToken)
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = steamCmdPath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(steamCmdPath) ?? AppDomain.CurrentDomain.BaseDirectory
            };

            using (var process = new Process { StartInfo = processStartInfo })
            {
                process.Start();

                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();

                await Task.Run(() => process.WaitForExit(), cancellationToken).ConfigureAwait(false);

                return new SteamCmdResult
                {
                    ExitCode = process.ExitCode,
                    StdOut = await stdoutTask.ConfigureAwait(false),
                    StdErr = await stderrTask.ConfigureAwait(false)
                };
            }
        }

        private static string LastLines(string text, int maxLines)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "(пусто)";
            }

            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            if (lines.Length <= maxLines)
            {
                return text.Trim();
            }

            return string.Join(Environment.NewLine, lines.Skip(lines.Length - maxLines)).Trim();
        }

        private sealed class SteamCmdResult
        {
            public int ExitCode { get; set; }
            public string StdOut { get; set; }
            public string StdErr { get; set; }
        }

        public async Task<ModUpdateAnalysis> AnalyzeModsAsync(
            string conanExePath,
            IEnumerable<string> mods,
            Action<string> log,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(conanExePath) || !File.Exists(conanExePath))
            {
                throw new InvalidOperationException("Не найден ConanSandbox.exe. Укажи корректный путь.");
            }

            var workshopContentRoot = ResolveWorkshopContentRoot(conanExePath);
            var entries = ParseModEntries(mods);
            var analysis = new ModUpdateAnalysis();

            if (entries.Count == 0)
            {
                return analysis;
            }

            log("Проверяю актуальность модов через Steam Web API...");

            Dictionary<string, WorkshopModMeta> remoteMeta;
            try
            {
                remoteMeta = await LoadWorkshopMetaAsync(entries.Select(x => x.ModId).Distinct().ToList(), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                log("Не удалось проверить версии через Steam API: " + ex.Message);
                log("Будет запрошено обновление всех модов.");

                foreach (var entry in entries)
                {
                    analysis.Updates.Add(new ModUpdateInfo
                    {
                        ModId = entry.ModId,
                        PakName = entry.PakName,
                        Status = "Требует проверки",
                        SizeBytes = 0
                    });
                }

                return analysis;
            }

            foreach (var entry in entries)
            {
                var localPath = Path.Combine(workshopContentRoot, entry.ModId, entry.PakName);
                var exists = File.Exists(localPath);
                var localUtc = exists ? File.GetLastWriteTimeUtc(localPath) : EpochUtc();

                WorkshopModMeta meta;
                if (!remoteMeta.TryGetValue(entry.ModId, out meta))
                {
                    continue;
                }

                if (!exists)
                {
                    analysis.Updates.Add(new ModUpdateInfo
                    {
                        ModId = entry.ModId,
                        PakName = entry.PakName,
                        Status = "Отсутствует",
                        SizeBytes = meta.SizeBytes
                    });
                    continue;
                }

                if (meta.UpdatedUtc > localUtc)
                {
                    analysis.Updates.Add(new ModUpdateInfo
                    {
                        ModId = entry.ModId,
                        PakName = entry.PakName,
                        Status = "Устарел",
                        SizeBytes = meta.SizeBytes
                    });
                }
            }

            return analysis;
        }

        public void DisableCinematicIntro(string conanExePath, Action<string> log)
        {
            var gameRoot = Path.GetDirectoryName(conanExePath) ?? string.Empty;
            var defaultGameIniPath = Path.Combine(gameRoot, "ConanSandbox", "Config", "DefaultGame.ini");
            if (!File.Exists(defaultGameIniPath))
            {
                log("DefaultGame.ini не найден, пропускаю отключение интро.");
                return;
            }

            var lines = File.ReadAllLines(defaultGameIniPath);
            var changed = false;

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.StartsWith("+StartupMovies=", StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = "-" + line.Substring(1);
                    changed = true;
                }
            }

            if (changed)
            {
                File.WriteAllLines(defaultGameIniPath, lines);
                log("Вступительный ролик Conan отключен.");
            }
        }

        private static string BuildBatchArguments(string steamLibraryRoot, IEnumerable<string> ids)
        {
            var builder = new StringBuilder();
            builder.Append("+force_install_dir \"");
            builder.Append(steamLibraryRoot);
            builder.Append("\" +login anonymous ");

            foreach (var id in ids)
            {
                builder.Append("+workshop_download_item ");
                builder.Append(ConanSteamAppId);
                builder.Append(" ");
                builder.Append(id);
                builder.Append(" validate ");
            }

            builder.Append("+quit");
            return builder.ToString();
        }

        private async Task ProcessModsOneByOneAsync(
            string steamCmdPath,
            string steamLibraryRoot,
            int totalCount,
            IList<ModUpdateInfo> batch,
            int alreadyProcessed,
            Action<string> log,
            Action<int, int, string> progress,
            CancellationToken cancellationToken)
        {
            var localProcessed = alreadyProcessed;
            for (var i = 0; i < batch.Count; i++)
            {
                var item = batch[i];
                var id = item.ModId;
                log(string.Format("Мод {0}/{1}: {2}/{3}", localProcessed + 1, totalCount, item.ModId, item.PakName));

                var args = string.Format(
                    "+force_install_dir \"{0}\" +login anonymous +workshop_download_item {1} {2} validate +quit",
                    steamLibraryRoot,
                    ConanSteamAppId,
                    id);

                var result = await RunSteamCmdAsync(steamCmdPath, args, cancellationToken).ConfigureAwait(false);
                if (result.ExitCode != 0)
                {
                    var stdoutTail = LastLines(result.StdOut, 20);
                    var stderrTail = LastLines(result.StdErr, 20);
                    throw new InvalidOperationException(
                        "steamcmd завершился с ошибкой при обработке мода " + id + ". Код: " + result.ExitCode + Environment.NewLine +
                        "STDOUT (хвост): " + stdoutTail + Environment.NewLine +
                        "STDERR (хвост): " + stderrTail);
                }

                localProcessed++;
                if (progress != null)
                {
                    progress(localProcessed, totalCount, item.ModId);
                }
            }
        }

        private static DateTime UnixTimeToUtc(long seconds)
        {
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return epoch.AddSeconds(seconds);
        }

        private static DateTime GetLocalWorkshopModLastWriteUtc(string workshopContentRoot, string modId)
        {
            var modDirectory = Path.Combine(workshopContentRoot, modId);
            if (!Directory.Exists(modDirectory))
            {
                return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            }

            var pakFile = Directory.GetFiles(modDirectory, "*.pak").FirstOrDefault();
            if (string.IsNullOrWhiteSpace(pakFile) || !File.Exists(pakFile))
            {
                return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            }

            return File.GetLastWriteTimeUtc(pakFile);
        }

        private async Task<Dictionary<string, WorkshopModMeta>> LoadWorkshopMetaAsync(IList<string> modIds, CancellationToken cancellationToken)
        {
            var form = new List<KeyValuePair<string, string>>();
            for (var i = 0; i < modIds.Count; i++)
            {
                form.Add(new KeyValuePair<string, string>("publishedfileids[" + i + "]", modIds[i]));
            }
            form.Add(new KeyValuePair<string, string>("itemcount", modIds.Count.ToString()));

            using (var content = new FormUrlEncodedContent(form))
            using (var response = await HttpClient.PostAsync(WorkshopApiUrl, content, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var root = JObject.Parse(json);
                var responseToken = root["response"];
                var details = responseToken != null ? responseToken["publishedfiledetails"] as JArray : null;
                if (details == null)
                {
                    throw new InvalidOperationException("Steam API вернул неожиданный формат.");
                }

                var map = new Dictionary<string, WorkshopModMeta>(StringComparer.Ordinal);
                foreach (var mod in details)
                {
                    var modId = mod["publishedfileid"] != null ? mod["publishedfileid"].ToString() : string.Empty;
                    var timeUpdatedRaw = mod["time_updated"] != null ? mod["time_updated"].ToString() : "0";
                    var sizeRaw = mod["file_size"] != null ? mod["file_size"].ToString() : "0";

                    long timeUpdatedUnix;
                    long sizeBytes;
                    if (string.IsNullOrWhiteSpace(modId) || !long.TryParse(timeUpdatedRaw, out timeUpdatedUnix))
                    {
                        continue;
                    }

                    if (!long.TryParse(sizeRaw, out sizeBytes))
                    {
                        sizeBytes = 0;
                    }

                    map[modId] = new WorkshopModMeta
                    {
                        UpdatedUtc = UnixTimeToUtc(timeUpdatedUnix),
                        SizeBytes = sizeBytes
                    };
                }

                return map;
            }
        }

        private static List<ModEntry> ParseModEntries(IEnumerable<string> mods)
        {
            var entries = new List<ModEntry>();
            var rawMods = mods != null ? mods.Where(x => !string.IsNullOrWhiteSpace(x)).ToList() : new List<string>();

            foreach (var mod in rawMods)
            {
                var parts = mod.Split(new[] { '/' }, 2);
                if (parts.Length != 2)
                {
                    continue;
                }

                var modId = parts[0].Trim();
                var pakName = parts[1].Trim();
                if (string.IsNullOrWhiteSpace(modId) || string.IsNullOrWhiteSpace(pakName))
                {
                    continue;
                }

                entries.Add(new ModEntry { ModId = modId, PakName = pakName });
            }

            return entries;
        }

        private static DateTime EpochUtc()
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        }

        private static void TrySetLastConnected(string conanExePath, string serverIp)
        {
            try
            {
                var sandboxDirectory = ResolveConanSandboxDirectory(conanExePath);
                var gameIniPath = Path.Combine(sandboxDirectory, "Saved", "Config", "WindowsNoEditor", "Game.ini");
                if (!File.Exists(gameIniPath))
                {
                    return;
                }

                var lines = File.ReadAllLines(gameIniPath).ToList();
                var hasLastConnected = false;
                var hasStartedListenServerSession = false;

                for (var i = 0; i < lines.Count; i++)
                {
                    if (lines[i].StartsWith("LastConnected=", StringComparison.OrdinalIgnoreCase))
                    {
                        lines[i] = "LastConnected=" + serverIp;
                        hasLastConnected = true;
                    }
                    else if (lines[i].StartsWith("StartedListenServerSession=", StringComparison.OrdinalIgnoreCase))
                    {
                        lines[i] = "StartedListenServerSession=False";
                        hasStartedListenServerSession = true;
                    }
                }

                if (!hasLastConnected)
                {
                    lines.Add("LastConnected=" + serverIp);
                }

                if (!hasStartedListenServerSession)
                {
                    lines.Add("StartedListenServerSession=False");
                }

                File.WriteAllLines(gameIniPath, lines);
            }
            catch
            {
                // Ничего не делаем: игра всё равно будет запущена, даже если не удалось обновить Game.ini
            }
        }

        private static string ResolvePreferredLaunchExe(string conanExePath)
        {
            var gameRoot = Path.GetDirectoryName(conanExePath) ?? string.Empty;
            var binWin64 = Path.Combine(gameRoot, "ConanSandbox", "Binaries", "Win64");
            var directExe = Path.Combine(binWin64, "ConanSandbox.exe");
            var battleyeExe = Path.Combine(binWin64, "ConanSandbox_BE.exe");

            if (File.Exists(directExe))
            {
                return directExe;
            }

            if (File.Exists(battleyeExe))
            {
                return battleyeExe;
            }

            return conanExePath;
        }

        private sealed class WorkshopModMeta
        {
            public DateTime UpdatedUtc { get; set; }
            public long SizeBytes { get; set; }
        }

        private sealed class ModEntry
        {
            public string ModId { get; set; }
            public string PakName { get; set; }
        }
    }
}
