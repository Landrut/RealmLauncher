using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RealmLauncher.Models;
using Steamworks;

namespace RealmLauncher.Services
{
    public sealed class LauncherService
    {
        private const int ConanSteamAppId = 440900;
        private const string SteamCmdZipUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip";
        private const string WorkshopApiUrl = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/";
        private static readonly HttpClient HttpClient = new HttpClient();
        private static readonly object SteamworksSync = new object();
        private static bool _steamworksInitialized;
        private static string _steamworksInitError;

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

        public bool IsSteamworksInitialized()
        {
            lock (SteamworksSync)
            {
                return _steamworksInitialized;
            }
        }

        public void EnsureSteamworksInitialized(Action<string> log)
        {
            lock (SteamworksSync)
            {
                if (_steamworksInitialized)
                {
                    return;
                }

                if (!string.IsNullOrWhiteSpace(_steamworksInitError))
                {
                    throw new InvalidOperationException(_steamworksInitError);
                }

                try
                {
                    EnsureSteamAppIdFile();
                    SteamClient.Init((uint)ConanSteamAppId, true);
                    if (!SteamClient.IsValid)
                    {
                        throw new InvalidOperationException("Steamworks не инициализирован (SteamClient.IsValid=false).");
                    }

                    if (!SteamClient.IsLoggedOn)
                    {
                        throw new InvalidOperationException("Steam запущен, но пользователь не авторизован в клиенте Steam.");
                    }

                    _steamworksInitialized = true;
                    log("Steamworks подключен. Пользователь: " + SteamClient.Name);
                }
                catch (Exception ex)
                {
                    _steamworksInitError = "Не удалось инициализировать Steamworks: " + ex.Message;
                    throw new InvalidOperationException(_steamworksInitError, ex);
                }
            }
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

        public async Task<ServerConfig> DownloadConfigAsync(string configUrl, ISet<string> allowedHosts, CancellationToken cancellationToken)
        {
            var configUri = UrlSecurity.RequireAllowedHttpsUrl(configUrl, allowedHosts, "URL JSON сервера");

            using (var response = await HttpClient.GetAsync(configUri, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var config = JsonConvert.DeserializeObject<ServerConfig>(json);

                if (config == null)
                {
                    throw new InvalidOperationException("Не получилось разобрать JSON конфигурацию.");
                }

                if (string.IsNullOrWhiteSpace(config.Ip))
                {
                    throw new InvalidOperationException("В JSON отсутствует поле ip.");
                }

                if (config.Mods == null)
                {
                    config.Mods = new List<string>();
                }
                else
                {
                    // Reject malformed IDs from remote JSON before they reach steamcmd args.
                    config.Mods = config.Mods
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Where(x =>
                        {
                            var parts = x.Split(new[] { '/' }, 2);
                            return parts.Length == 2 &&
                                   !string.IsNullOrWhiteSpace(parts[0]) &&
                                   parts[0].Trim().All(char.IsDigit);
                        })
                        .ToList();
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
            Action<double, double, string> progress,
            CancellationToken cancellationToken)
        {
            var steamCmdPath = GetSteamCmdPath();
            if (!File.Exists(steamCmdPath))
            {
                throw new InvalidOperationException("SteamCMD не найден. Нажмите кнопку проверки и установи его.");
            }
            if (string.IsNullOrWhiteSpace(conanExePath) || !File.Exists(conanExePath))
            {
                throw new InvalidOperationException("Не найден ConanSandbox.exe. Укажите корректный путь.");
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
            log("Проверка готовности SteamCMD...");
            await EnsureSteamCmdReadyAsync(steamCmdPath, cancellationToken).ConfigureAwait(false);

            var processed = 0;
            for (var i = 0; i < updates.Count; i++)
            {
                var item = updates[i];
                var itemLabel = string.Format("{0}/{1}", item.ModId, item.PakName);
                log(string.Format("Обновление мода {0}/{1}: {2}", i + 1, updates.Count, itemLabel));

                var lastSignalUtc = DateTime.UtcNow;
                var effectivePercent = 0.0;
                var gate = new object();
                using (var progressCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    var fallbackProgressTask = Task.Run(async () =>
                    {
                        while (!progressCts.IsCancellationRequested)
                        {
                            await Task.Delay(700, progressCts.Token).ConfigureAwait(false);

                            lock (gate)
                            {
                                if ((DateTime.UtcNow - lastSignalUtc).TotalSeconds >= 1.5 && effectivePercent < 95)
                                {
                                    effectivePercent = Math.Min(95, effectivePercent + 2.5);
                                    if (progress != null)
                                    {
                                        var current = processed + (effectivePercent / 100.0);
                                        progress(current, updates.Count, itemLabel);
                                    }
                                }
                            }
                        }
                    }, progressCts.Token);

                var result = await RunSteamCmdAsync(
                    steamCmdPath,
                    BuildBatchArguments(steamLibraryRoot, new[] { item.ModId }),
                    cancellationToken,
                    percent =>
                    {
                        lock (gate)
                        {
                            lastSignalUtc = DateTime.UtcNow;
                            if (percent > effectivePercent)
                            {
                                effectivePercent = percent;
                            }

                            if (progress != null)
                            {
                                var current = processed + (effectivePercent / 100.0);
                                progress(current, updates.Count, itemLabel);
                            }
                        }
                    }).ConfigureAwait(false);

                    progressCts.Cancel();
                    try
                    {
                        await fallbackProgressTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }

                if (result.ExitCode != 0)
                {
                    var stdoutTail = LastLines(result.StdOut, 20);
                    var stderrTail = LastLines(result.StdErr, 20);
                    throw new InvalidOperationException(
                        "steamcmd завершился с ошибкой при обработке мода " + item.ModId + ". Код: " + result.ExitCode + Environment.NewLine +
                        "STDOUT (хвост): " + stdoutTail + Environment.NewLine +
                        "STDERR (хвост): " + stderrTail);
                }
                }

                processed++;
                if (progress != null)
                {
                    progress(processed, updates.Count, itemLabel);
                }
                log(string.Format("Готово: {0}/{1}", processed, updates.Count));
            }

            log("Все моды синхронизированы.");
        }

        public string WriteModListFile(string conanExePath, IEnumerable<string> mods, Action<string> log)
        {
            if (string.IsNullOrWhiteSpace(conanExePath) || !File.Exists(conanExePath))
            {
                throw new InvalidOperationException("Не найден ConanSandbox.exe. Укажите корректный путь.");
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
                throw new InvalidOperationException("Не найден ConanSandbox.exe. Укажите корректный путь.");
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

        public async Task SyncModsWithSteamworksAsync(
            string conanExePath,
            IEnumerable<ModUpdateInfo> modsToUpdate,
            bool autoSubscribe,
            Action<string> log,
            Action<double, double, string> progress,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(conanExePath) || !File.Exists(conanExePath))
            {
                throw new InvalidOperationException("Не найден ConanSandbox.exe. Укажите корректный путь.");
            }

            var updates = modsToUpdate != null
                ? modsToUpdate.Where(x => x != null && !string.IsNullOrWhiteSpace(x.ModId))
                    .GroupBy(x => x.ModId)
                    .Select(g => g.First())
                    .ToList()
                : new List<ModUpdateInfo>();

            if (updates.Count == 0)
            {
                log("Нет модов для синхронизации через Steamworks.");
                return;
            }

            EnsureSteamworksInitialized(log);
            var workshopContentRoot = ResolveWorkshopContentRoot(conanExePath);
            var processed = 0d;

            for (var i = 0; i < updates.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var update = updates[i];
                var modLabel = string.Format("{0}/{1}", update.ModId, update.PakName);
                log(string.Format("Обновляю мод {0}/{1}: {2}", i + 1, updates.Count, modLabel));
                progress?.Invoke(processed, updates.Count, modLabel);
                var pakPath = Path.Combine(workshopContentRoot, update.ModId, update.PakName);
                var hadBefore = File.Exists(pakPath);
                var beforeUtc = hadBefore ? File.GetLastWriteTimeUtc(pakPath) : DateTime.MinValue;
                var beforeSize = hadBefore ? new FileInfo(pakPath).Length : -1L;
                var isMissingByAnalysis = string.Equals(update.Status, "Отсутствует", StringComparison.OrdinalIgnoreCase);
                var isOutdatedByAnalysis = string.Equals(update.Status, "Устарел", StringComparison.OrdinalIgnoreCase);

                ulong rawId;
                if (!ulong.TryParse(update.ModId, out rawId))
                {
                    throw new InvalidOperationException("Некорректный id мода: " + update.ModId);
                }

                var publishedFileId = (Steamworks.Data.PublishedFileId)rawId;
                var queried = await SteamUGC.QueryFileAsync(publishedFileId).ConfigureAwait(false);
                if (!queried.HasValue)
                {
                    throw new InvalidOperationException("Steamworks не вернул данные для мода " + update.ModId);
                }

                var item = queried.Value;
                if (autoSubscribe && !item.IsSubscribed)
                {
                    var subscribed = await item.Subscribe().ConfigureAwait(false);
                    if (!subscribed)
                    {
                        throw new InvalidOperationException("Не удалось подписаться на мод " + update.ModId + " через Steamworks.");
                    }
                    log("Подписка оформлена: " + update.ModId);
                }
                else if (!autoSubscribe && !item.IsSubscribed)
                {
                    throw new InvalidOperationException(
                        "Мод " + update.ModId + " не подписан в Workshop, а автоподписка отключена. " +
                        "Включите опцию \"Авто-подписка на моды Workshop\".");
                }

                var needsDownload = isMissingByAnalysis || isOutdatedByAnalysis || !item.IsInstalled || item.NeedsUpdate;
                if (needsDownload)
                {
                    var ok = await item.DownloadAsync(
                        fraction =>
                        {
                            var clamped = Math.Max(0d, Math.Min(1d, fraction));
                            progress?.Invoke(processed + clamped, updates.Count, modLabel);
                        },
                        1800,
                        cancellationToken).ConfigureAwait(false);

                    if (!ok)
                    {
                        throw new InvalidOperationException("Steam не смог завершить загрузку мода " + update.ModId);
                    }
                }

                var exists = await WaitForFileAsync(pakPath, TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);
                if (!exists)
                {
                    throw new InvalidOperationException("После загрузки не найден файл мода: " + pakPath);
                }

                // Для "Устарел" дополнительно убеждаемся, что файл реально изменился.
                if (isOutdatedByAnalysis)
                {
                    var changedAfterDownload = HasLocalModFileChanged(pakPath, hadBefore, beforeUtc, beforeSize);
                    if (!changedAfterDownload)
                    {
                        log("Steam не обновил файл сразу. Применяю форс-обновление (отписка -> подписка -> загрузка)...");

                        await item.Unsubscribe().ConfigureAwait(false);
                        await Task.Delay(1200, cancellationToken).ConfigureAwait(false);
                        var resubscribed = await item.Subscribe().ConfigureAwait(false);
                        if (!resubscribed)
                        {
                            throw new InvalidOperationException("Не удалось переподписаться на мод " + update.ModId + " для форс-обновления.");
                        }

                        var refreshed = await SteamUGC.QueryFileAsync(publishedFileId).ConfigureAwait(false);
                        if (refreshed.HasValue)
                        {
                            item = refreshed.Value;
                        }

                        var forcedOk = await item.DownloadAsync(
                            fraction =>
                            {
                                var clamped = Math.Max(0d, Math.Min(1d, fraction));
                                progress?.Invoke(processed + clamped, updates.Count, modLabel);
                            },
                            1800,
                            cancellationToken).ConfigureAwait(false);

                        if (!forcedOk)
                        {
                            throw new InvalidOperationException("Форс-обновление мода " + update.ModId + " не завершилось успешно.");
                        }

                        exists = await WaitForFileAsync(pakPath, TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
                        if (!exists || !HasLocalModFileChanged(pakPath, hadBefore, beforeUtc, beforeSize))
                        {
                            throw new InvalidOperationException(
                                "Steam сообщил успешную загрузку, но локальный файл мода не изменился: " + pakPath);
                        }

                        log("Форс-обновление применено: " + update.ModId);
                    }
                }

                processed += 1d;
                progress?.Invoke(processed, updates.Count, modLabel);
                log(string.Format("Готово: {0}/{1}", (int)processed, updates.Count));
            }

            log("Синхронизация модов через Steamworks завершена.");
        }

        public async Task<ServerQueryInfo> QueryServerInfoAsync(string host, int queryPort, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                throw new InvalidOperationException("Не указан хост сервера для query.");
            }

            const string queryString = "Source Engine Query";
            var queryPacket = new List<byte> { 0xFF, 0xFF, 0xFF, 0xFF, 0x54 };
            queryPacket.AddRange(Encoding.ASCII.GetBytes(queryString));
            queryPacket.Add(0x00);

            using (var udp = new UdpClient())
            {
                udp.Client.ReceiveTimeout = 3500;
                udp.Client.SendTimeout = 3500;

                var endpoint = new IPEndPoint(Dns.GetHostAddresses(host).First(), queryPort);
                await udp.SendAsync(queryPacket.ToArray(), queryPacket.Count, endpoint).ConfigureAwait(false);
                var response = await ReceiveWithCancellationAsync(udp, cancellationToken).ConfigureAwait(false);

                if (response.Length >= 9 && response[4] == 0x41)
                {
                    var challenge = response.Skip(5).Take(4).ToArray();
                    var challengePacket = new List<byte>(queryPacket);
                    challengePacket.AddRange(challenge);
                    await udp.SendAsync(challengePacket.ToArray(), challengePacket.Count, endpoint).ConfigureAwait(false);
                    response = await ReceiveWithCancellationAsync(udp, cancellationToken).ConfigureAwait(false);
                }

                return ParseA2SInfo(response);
            }
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

        private static void EnsureSteamAppIdFile()
        {
            var appIdPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "steam_appid.txt");
            if (!File.Exists(appIdPath))
            {
                File.WriteAllText(appIdPath, ConanSteamAppId.ToString());
                return;
            }

            var current = File.ReadAllText(appIdPath).Trim();
            if (!string.Equals(current, ConanSteamAppId.ToString(), StringComparison.Ordinal))
            {
                File.WriteAllText(appIdPath, ConanSteamAppId.ToString());
            }
        }

        private static async Task<bool> WaitForFileAsync(string fullPath, TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (File.Exists(fullPath))
            {
                return true;
            }

            var started = DateTime.UtcNow;
            while (DateTime.UtcNow - started < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(300, cancellationToken).ConfigureAwait(false);
                if (File.Exists(fullPath))
                {
                    return true;
                }
            }

            return File.Exists(fullPath);
        }

        private static bool HasLocalModFileChanged(string fullPath, bool hadBefore, DateTime beforeUtc, long beforeSize)
        {
            if (!File.Exists(fullPath))
            {
                return false;
            }

            if (!hadBefore)
            {
                return true;
            }

            var info = new FileInfo(fullPath);
            var afterUtc = info.LastWriteTimeUtc;
            var afterSize = info.Length;
            return afterUtc > beforeUtc.AddSeconds(1) || afterSize != beforeSize;
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
                if (!modId.All(char.IsDigit))
                {
                    continue;
                }
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

        private async Task<SteamCmdResult> RunSteamCmdAsync(
            string steamCmdPath,
            string arguments,
            CancellationToken cancellationToken,
            Action<int> percentChanged = null)
        {
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
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
                process.OutputDataReceived += (_, args) =>
                {
                    if (args.Data == null) return;
                    lock (stdout) stdout.AppendLine(args.Data);

                    if (percentChanged != null)
                    {
                        var percent = ParseSteamPercent(args.Data);
                        if (percent.HasValue)
                        {
                            percentChanged(percent.Value);
                        }
                    }
                };
                process.ErrorDataReceived += (_, args) =>
                {
                    if (args.Data == null) return;
                    lock (stderr) stderr.AppendLine(args.Data);
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await Task.Run(() => process.WaitForExit(), cancellationToken).ConfigureAwait(false);

                return new SteamCmdResult
                {
                    ExitCode = process.ExitCode,
                    StdOut = stdout.ToString(),
                    StdErr = stderr.ToString()
                };
            }
        }

        private static int? ParseSteamPercent(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return null;
            }

            var match = Regex.Match(line, @"\[\s*(\d{1,3})%\]");
            if (!match.Success)
            {
                match = Regex.Match(line, @"(?:^|[^\d])(\d{1,3})\s*%(?:[^\d]|$)");
            }
            if (!match.Success)
            {
                return null;
            }

            int value;
            if (!int.TryParse(match.Groups[1].Value, out value))
            {
                return null;
            }

            if (value < 0) value = 0;
            if (value > 100) value = 100;
            return value;
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

        private static async Task<byte[]> ReceiveWithCancellationAsync(UdpClient udp, CancellationToken cancellationToken)
        {
            var receiveTask = udp.ReceiveAsync();
            var delayTask = Task.Delay(Timeout.Infinite, cancellationToken);
            var completed = await Task.WhenAny(receiveTask, delayTask).ConfigureAwait(false);
            if (completed == delayTask)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var result = await receiveTask.ConfigureAwait(false);
            return result.Buffer;
        }

        private static ServerQueryInfo ParseA2SInfo(byte[] buffer)
        {
            var info = new ServerQueryInfo { IsOnline = false, Name = string.Empty, Players = 0, MaxPlayers = 0 };
            if (buffer == null || buffer.Length < 6)
            {
                return info;
            }

            if (buffer[4] != 0x49)
            {
                return info;
            }

            var offset = 6; // 4*FF + header + protocol
            var name = ReadNullTerminatedString(buffer, ref offset);
            ReadNullTerminatedString(buffer, ref offset); // map
            ReadNullTerminatedString(buffer, ref offset); // folder
            ReadNullTerminatedString(buffer, ref offset); // game
            offset += 2; // app id
            if (offset + 1 >= buffer.Length)
            {
                return info;
            }

            var players = buffer[offset++];
            var maxPlayers = buffer[offset];

            info.IsOnline = true;
            info.Name = name;
            info.Players = players;
            info.MaxPlayers = maxPlayers;
            return info;
        }

        private static string ReadNullTerminatedString(byte[] buffer, ref int offset)
        {
            if (offset >= buffer.Length)
            {
                return string.Empty;
            }

            var start = offset;
            while (offset < buffer.Length && buffer[offset] != 0x00)
            {
                offset++;
            }

            var value = Encoding.UTF8.GetString(buffer, start, Math.Max(0, offset - start));
            if (offset < buffer.Length && buffer[offset] == 0x00)
            {
                offset++;
            }
            return value;
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
                throw new InvalidOperationException("Не найден ConanSandbox.exe. Укажите корректный путь.");
            }

            var workshopContentRoot = ResolveWorkshopContentRoot(conanExePath);
            var entries = ParseModEntries(mods);
            var analysis = new ModUpdateAnalysis();

            if (entries.Count == 0)
            {
                return analysis;
            }

            log("Проверка актуальности модов через Steam Web API...");

            Dictionary<string, WorkshopModMeta> remoteMeta;
            try
            {
                remoteMeta = await LoadWorkshopMetaAsync(entries.Select(x => x.ModId).Distinct().ToList(), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                log("Не удалось проверить версии через Steam API: " + ex.Message);
                log("Запрос обновления всех модов.");

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
                if (!modId.All(char.IsDigit))
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
