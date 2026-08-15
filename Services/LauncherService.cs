using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RealmLauncher.Models;
using Steamworks;

namespace RealmLauncher.Services
{
    internal sealed class RateMeter
    {
        private DateTime _lastUtc;
        private long _lastBytes;
        private double _rate;

        public double BytesPerSecond
        {
            get { return _rate; }
        }

        public void Update(long totalBytesSoFar)
        {
            var now = DateTime.UtcNow;

            if (_lastUtc == default(DateTime))
            {
                _lastUtc = now;
                _lastBytes = totalBytesSoFar;
                return;
            }

            var seconds = (now - _lastUtc).TotalSeconds;
            if (seconds < 0.5)
            {
                return;
            }

            var delta = totalBytesSoFar - _lastBytes;
            _lastUtc = now;
            _lastBytes = totalBytesSoFar;

            if (delta < 0)
            {
                return;
            }

            var instant = delta / seconds;
            _rate = _rate <= 0 ? instant : (_rate * 0.6) + (instant * 0.4);
        }
    }

    public sealed class ModSyncProgress
    {
        public int CompletedMods { get; set; }
        public int TotalMods { get; set; }
        public string CurrentModName { get; set; }

        public double OverallFraction { get; set; }

        public long BytesDone { get; set; }
        public long BytesTotal { get; set; }

        public double BytesPerSecond { get; set; }
        public TimeSpan? Eta { get; set; }
    }

    public sealed class LauncherService
    {
        public const int ConanSteamAppId = 440900;
        private const string WorkshopApiUrl = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/";

        private static readonly HttpClient HttpClient = new HttpClient();
        private static readonly object SteamworksSync = new object();
        private static bool _steamworksInitialized;
        private static string _steamworksInitError;

        public LauncherService()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
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
                    // Reject malformed IDs from remote JSON before they reach any file path.
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

        public string WriteModListFile(string conanExePath, IEnumerable<string> mods, Action<string> log)
        {
            RequireGameExe(conanExePath);

            var sandboxDirectory = GameConfigService.ResolveSandboxDirectory(conanExePath);
            var modsDirectory = Path.Combine(sandboxDirectory, "Mods");
            Directory.CreateDirectory(modsDirectory);

            var workshopContentRoot = ResolveWorkshopContentRoot(conanExePath);
            log("Папка модов Workshop: " + workshopContentRoot);

            var modEntries = BuildAbsoluteModEntries(workshopContentRoot, mods, log);
            var modListPath = Path.Combine(modsDirectory, "modlist.txt");
            File.WriteAllLines(modListPath, modEntries);

            return modListPath;
        }

        public ModListSnapshot CaptureModListSnapshot(string conanExePath)
        {
            RequireGameExe(conanExePath);

            var modListPath = GetModListPath(conanExePath);
            if (!File.Exists(modListPath))
            {
                return new ModListSnapshot { Exists = false, Bytes = Array.Empty<byte>() };
            }

            return new ModListSnapshot { Exists = true, Bytes = File.ReadAllBytes(modListPath) };
        }

        public void RestoreModListSnapshot(string conanExePath, ModListSnapshot snapshot, Action<string> log)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(conanExePath) || !File.Exists(conanExePath))
            {
                return;
            }

            var modListPath = GetModListPath(conanExePath);
            Directory.CreateDirectory(Path.GetDirectoryName(modListPath));

            if (snapshot.Exists)
            {
                File.WriteAllBytes(modListPath, snapshot.Bytes ?? Array.Empty<byte>());
                log("Исходный modlist.txt восстановлен.");
            }
            else if (File.Exists(modListPath))
            {
                File.Delete(modListPath);
                log("Временный modlist.txt удалён (до запуска лаунчера файла не было).");
            }
        }

        private static string GetModListPath(string conanExePath)
        {
            var sandboxDirectory = GameConfigService.ResolveSandboxDirectory(conanExePath);
            return Path.Combine(sandboxDirectory, "Mods", "modlist.txt");
        }

        public Process LaunchServerConnection(string conanExePath, string serverIp, bool useBattlEye, Action<string> log)
        {
            RequireGameExe(conanExePath);

            if (string.IsNullOrWhiteSpace(serverIp))
            {
                throw new InvalidOperationException("IP сервера пустой.");
            }

            GameConfigService.SetLastConnectedServer(conanExePath, serverIp.Trim(), log);

            var launchExe = GameConfigService.ResolveLaunchExe(conanExePath, useBattlEye);
            log("Запуск: " + Path.GetFileName(launchExe) + (useBattlEye ? " (BattlEye)" : " (без BattlEye)"));

            return Process.Start(new ProcessStartInfo
            {
                FileName = launchExe,
                Arguments = "-continuesession",
                WorkingDirectory = Path.GetDirectoryName(launchExe),
                UseShellExecute = true
            });
        }

        public Process LaunchLocalGame(string conanExePath, bool useBattlEye, Action<string> log)
        {
            RequireGameExe(conanExePath);

            var launchExe = GameConfigService.ResolveLaunchExe(conanExePath, useBattlEye);
            var arguments = (AppRuntimeConfig.LocalPlayArguments ?? string.Empty).Trim();

            log("Локальный запуск: " + Path.GetFileName(launchExe) +
                (string.IsNullOrEmpty(arguments) ? string.Empty : " " + arguments));

            return Process.Start(new ProcessStartInfo
            {
                FileName = launchExe,
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(launchExe),
                UseShellExecute = true
            });
        }

        public async Task<bool> WaitForGameProcessAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            var names = new[] { "ConanSandbox", "ConanSandbox-Win64-Shipping", "ConanSandbox_BE" };
            var started = DateTime.UtcNow;

            while (DateTime.UtcNow - started < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();

                foreach (var name in names)
                {
                    try
                    {
                        if (Process.GetProcessesByName(name).Any())
                        {
                            return true;
                        }
                    }
                    catch
                    {
                    }
                }

                await Task.Delay(400, cancellationToken).ConfigureAwait(false);
            }

            return false;
        }

        public async Task<ModUpdateAnalysis> AnalyzeModsAsync(
            string conanExePath,
            IEnumerable<string> mods,
            Action<string> log,
            Action<int, int> progress,
            CancellationToken cancellationToken)
        {
            RequireGameExe(conanExePath);

            var workshopContentRoot = ResolveWorkshopContentRoot(conanExePath);
            var entries = ParseModEntries(mods);
            var analysis = new ModUpdateAnalysis();

            if (entries.Count == 0)
            {
                return analysis;
            }

            EnsureSteamworksInitialized(log);
            log(string.Format("Проверка {0} мод(ов) через Steam...", entries.Count));

            var sizes = await TryLoadWorkshopSizesAsync(
                entries.Select(x => x.ModId).Distinct().ToList(), log, cancellationToken).ConfigureAwait(false);

            for (var i = 0; i < entries.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = entries[i];
                progress?.Invoke(i + 1, entries.Count);

                long size;
                if (!sizes.TryGetValue(entry.ModId, out size))
                {
                    size = 0;
                }

                var pakPath = Path.Combine(workshopContentRoot, entry.ModId, entry.PakName);
                var status = await ResolveModStatusAsync(entry, pakPath, cancellationToken).ConfigureAwait(false);

                analysis.All.Add(new ModUpdateInfo
                {
                    ModId = entry.ModId,
                    PakName = entry.PakName,
                    Status = status,
                    SizeBytes = size
                });

                if (!string.Equals(status, ModStatus.UpToDate, StringComparison.Ordinal))
                {
                    analysis.Updates.Add(new ModUpdateInfo
                    {
                        ModId = entry.ModId,
                        PakName = entry.PakName,
                        Status = status,
                        SizeBytes = size
                    });
                }
            }

            log(string.Format("Требуют загрузки: {0} из {1}.", analysis.Updates.Count, entries.Count));
            return analysis;
        }

        private static async Task<string> ResolveModStatusAsync(ModEntry entry, string pakPath, CancellationToken cancellationToken)
        {
            ulong rawId;
            if (!ulong.TryParse(entry.ModId, out rawId))
            {
                return ModStatus.Missing;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var queried = await SteamUGC.QueryFileAsync((Steamworks.Data.PublishedFileId)rawId).ConfigureAwait(false);
            if (!queried.HasValue)
            {
                return File.Exists(pakPath) ? ModStatus.UpToDate : ModStatus.Missing;
            }

            var item = queried.Value;

            if (!item.IsInstalled)
            {
                return ModStatus.Missing;
            }

            if (item.NeedsUpdate)
            {
                return ModStatus.Outdated;
            }

            return File.Exists(pakPath) ? ModStatus.UpToDate : ModStatus.Missing;
        }

        private async Task<Dictionary<string, long>> TryLoadWorkshopSizesAsync(
            IList<string> modIds, Action<string> log, CancellationToken cancellationToken)
        {
            var result = new Dictionary<string, long>(StringComparer.Ordinal);
            if (modIds == null || modIds.Count == 0)
            {
                return result;
            }

            try
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
                        return result;
                    }

                    foreach (var mod in details)
                    {
                        var modId = mod["publishedfileid"] != null ? mod["publishedfileid"].ToString() : string.Empty;
                        var sizeRaw = mod["file_size"] != null ? mod["file_size"].ToString() : "0";

                        long sizeBytes;
                        if (!string.IsNullOrWhiteSpace(modId) && long.TryParse(sizeRaw, out sizeBytes))
                        {
                            result[modId] = sizeBytes;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log?.Invoke("Не удалось получить размеры модов из Steam API: " + ex.Message);
            }

            return result;
        }

        public async Task SyncModsWithSteamworksAsync(
            string conanExePath,
            IEnumerable<ModUpdateInfo> modsToUpdate,
            bool autoSubscribe,
            Action<string> log,
            Action<ModSyncProgress> progress,
            CancellationToken cancellationToken)
        {
            RequireGameExe(conanExePath);

            var updates = modsToUpdate != null
                ? modsToUpdate.Where(x => x != null && !string.IsNullOrWhiteSpace(x.ModId))
                    .GroupBy(x => x.ModId)
                    .Select(g => g.First())
                    .ToList()
                : new List<ModUpdateInfo>();

            if (updates.Count == 0)
            {
                log("Нет модов для синхронизации.");
                return;
            }

            EnsureSteamworksInitialized(log);
            var workshopContentRoot = ResolveWorkshopContentRoot(conanExePath);

            var estimatedTotal = updates.Sum(x => Math.Max(0L, x.SizeBytes));
            var estimatedDoneBefore = 0L;
            var realBytesBefore = 0L;
            var meter = new RateMeter();

            for (var i = 0; i < updates.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var update = updates[i];
                var modSize = Math.Max(0L, update.SizeBytes);
                log(string.Format("Мод {0}/{1}: {2}", i + 1, updates.Count, update.PakName));

                var completed = i;
                var doneBefore = estimatedDoneBefore;
                var realBefore = realBytesBefore;
                var lastRealForMod = 0L;

                Action<long, long> report = (downloaded, total) =>
                {
                    lastRealForMod = downloaded;

                    var modFraction = total > 0 ? Math.Max(0d, Math.Min(1d, downloaded / (double)total)) : 0d;
                    var overall = (completed + modFraction) / updates.Count;

                    meter.Update(realBefore + downloaded);
                    var speed = meter.BytesPerSecond;

                    TimeSpan? eta = null;
                    var remaining = (total > 0 ? total - downloaded : 0L) +
                                    Math.Max(0L, estimatedTotal - doneBefore - modSize);
                    if (speed > 1024 && remaining > 0)
                    {
                        eta = TimeSpan.FromSeconds(remaining / speed);
                    }

                    progress?.Invoke(new ModSyncProgress
                    {
                        CompletedMods = completed,
                        TotalMods = updates.Count,
                        CurrentModName = update.PakName,
                        OverallFraction = overall,
                        BytesDone = downloaded,
                        BytesTotal = total,
                        BytesPerSecond = speed,
                        Eta = eta
                    });
                };

                report(0, modSize);
                await DownloadSingleModAsync(update, workshopContentRoot, autoSubscribe, false, log, report, cancellationToken)
                    .ConfigureAwait(false);

                estimatedDoneBefore += modSize;
                realBytesBefore += Math.Max(lastRealForMod, modSize);
            }

            progress?.Invoke(new ModSyncProgress
            {
                CompletedMods = updates.Count,
                TotalMods = updates.Count,
                CurrentModName = updates[updates.Count - 1].PakName,
                OverallFraction = 1d
            });

            log("Синхронизация модов завершена.");
        }

        public async Task<ModUpdateInfo> AddModByIdAsync(
            string conanExePath,
            string modId,
            Action<string> log,
            Action<ModSyncProgress> progress,
            CancellationToken cancellationToken)
        {
            RequireGameExe(conanExePath);
            EnsureSteamworksInitialized(log);

            ulong rawId;
            if (string.IsNullOrWhiteSpace(modId) || !ulong.TryParse(modId, out rawId))
            {
                throw new InvalidOperationException("Некорректный ID мода: " + modId);
            }

            var workshopContentRoot = ResolveWorkshopContentRoot(conanExePath);
            var publishedFileId = (Steamworks.Data.PublishedFileId)rawId;

            var queried = await SteamUGC.QueryFileAsync(publishedFileId).ConfigureAwait(false);
            if (!queried.HasValue)
            {
                throw new InvalidOperationException("Steam не знает мод с ID " + modId + ".");
            }

            var item = queried.Value;
            log("Найден мод: " + item.Title);

            if (!item.IsSubscribed)
            {
                if (!await item.Subscribe().ConfigureAwait(false))
                {
                    throw new InvalidOperationException("Не удалось подписаться на мод " + modId + ".");
                }
                log("Подписка оформлена: " + modId);
            }

            var meter = new RateMeter();
            Action<long, long> report = (downloaded, total) =>
            {
                meter.Update(downloaded);
                var speed = meter.BytesPerSecond;

                progress?.Invoke(new ModSyncProgress
                {
                    CompletedMods = 0,
                    TotalMods = 1,
                    CurrentModName = item.Title,
                    OverallFraction = total > 0 ? Math.Max(0d, Math.Min(1d, downloaded / (double)total)) : 0d,
                    BytesDone = downloaded,
                    BytesTotal = total,
                    BytesPerSecond = speed,
                    Eta = speed > 1024 && total > downloaded
                        ? TimeSpan.FromSeconds((total - downloaded) / speed)
                        : (TimeSpan?)null
                });
            };

            await DownloadAndTrackAsync(item, publishedFileId, modId, 0L, report, log, cancellationToken)
                .ConfigureAwait(false);

            var pakName = ModListService.FindPakName(workshopContentRoot, modId);
            if (string.IsNullOrWhiteSpace(pakName))
            {
                throw new InvalidOperationException(
                    "Мод скачан, но .pak в его папке не найден — возможно, это не мод для Conan Exiles.");
            }

            return new ModUpdateInfo
            {
                ModId = modId,
                PakName = pakName,
                Status = ModStatus.UpToDate,
                SizeBytes = item.SizeBytes
            };
        }

        public async Task UnsubscribeModAsync(
            string conanExePath,
            string modId,
            Action<string> log,
            CancellationToken cancellationToken)
        {
            RequireGameExe(conanExePath);
            EnsureSteamworksInitialized(log);

            ulong rawId;
            if (string.IsNullOrWhiteSpace(modId) || !ulong.TryParse(modId, out rawId))
            {
                throw new InvalidOperationException("Некорректный ID мода: " + modId);
            }

            var publishedFileId = (Steamworks.Data.PublishedFileId)rawId;
            var queried = await SteamUGC.QueryFileAsync(publishedFileId).ConfigureAwait(false);

            if (queried.HasValue && queried.Value.IsSubscribed)
            {
                await queried.Value.Unsubscribe().ConfigureAwait(false);
                log("Подписка снята: " + modId);
            }
            else
            {
                log("Мод не был подписан: " + modId);
            }

            await Task.Delay(800, cancellationToken).ConfigureAwait(false);
            DeleteWorkshopFolder(ResolveWorkshopContentRoot(conanExePath), modId, log);
        }

        public async Task ReinstallModAsync(
            string conanExePath,
            ModUpdateInfo mod,
            Action<string> log,
            Action<ModSyncProgress> progress,
            CancellationToken cancellationToken)
        {
            RequireGameExe(conanExePath);
            EnsureSteamworksInitialized(log);

            var workshopContentRoot = ResolveWorkshopContentRoot(conanExePath);
            var meter = new RateMeter();

            Action<long, long> report = (downloaded, total) =>
            {
                var fraction = total > 0 ? Math.Max(0d, Math.Min(1d, downloaded / (double)total)) : 0d;
                meter.Update(downloaded);
                var speed = meter.BytesPerSecond;

                progress?.Invoke(new ModSyncProgress
                {
                    CompletedMods = 0,
                    TotalMods = 1,
                    CurrentModName = mod.PakName,
                    OverallFraction = fraction,
                    BytesDone = downloaded,
                    BytesTotal = total,
                    BytesPerSecond = speed,
                    Eta = speed > 1024 && total > downloaded
                        ? TimeSpan.FromSeconds((total - downloaded) / speed)
                        : (TimeSpan?)null
                });
            };

            log("Переустановка мода: " + mod.PakName);
            await DownloadSingleModAsync(mod, workshopContentRoot, true, true, log, report, cancellationToken).ConfigureAwait(false);

            progress?.Invoke(new ModSyncProgress
            {
                CompletedMods = 1,
                TotalMods = 1,
                CurrentModName = mod.PakName,
                OverallFraction = 1d
            });

            log("Мод переустановлен: " + mod.PakName);
        }

        private static async Task DownloadSingleModAsync(
            ModUpdateInfo update,
            string workshopContentRoot,
            bool autoSubscribe,
            bool forceRefresh,
            Action<string> log,
            Action<long, long> report,
            CancellationToken cancellationToken)
        {
            var pakPath = Path.Combine(workshopContentRoot, update.ModId, update.PakName);
            var hadBefore = File.Exists(pakPath);
            var beforeUtc = hadBefore ? File.GetLastWriteTimeUtc(pakPath) : DateTime.MinValue;
            var beforeSize = hadBefore ? new FileInfo(pakPath).Length : -1L;

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

            if (!item.IsSubscribed)
            {
                if (!autoSubscribe)
                {
                    throw new InvalidOperationException(
                        "Мод " + update.ModId + " не подписан в Workshop, а автоподписка отключена. " +
                        "Включите опцию \"Авто-подписка на моды Workshop\".");
                }

                if (!await item.Subscribe().ConfigureAwait(false))
                {
                    throw new InvalidOperationException("Не удалось подписаться на мод " + update.ModId + ".");
                }
                log("Подписка оформлена: " + update.ModId);
            }

            if (forceRefresh)
            {
                item = await ForceRedownloadAsync(
                    item, publishedFileId, update.ModId, workshopContentRoot, log, cancellationToken).ConfigureAwait(false);

                hadBefore = false;
                beforeUtc = DateTime.MinValue;
                beforeSize = -1L;
            }

            var needsDownload = forceRefresh || !item.IsInstalled || item.NeedsUpdate ||
                                !string.Equals(update.Status, ModStatus.UpToDate, StringComparison.Ordinal);

            if (needsDownload)
            {
                await DownloadAndTrackAsync(
                    item, publishedFileId, update.ModId, update.SizeBytes, report, log, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (!await WaitForFileAsync(pakPath, TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("После загрузки не найден файл мода: " + pakPath);
            }

            if (!forceRefresh &&
                string.Equals(update.Status, ModStatus.Outdated, StringComparison.Ordinal) &&
                !HasLocalModFileChanged(pakPath, hadBefore, beforeUtc, beforeSize))
            {
                log("Steam не обновил файл сразу. Применяю форс-обновление...");

                item = await ForceRedownloadAsync(
                    item, publishedFileId, update.ModId, workshopContentRoot, log, cancellationToken).ConfigureAwait(false);

                await DownloadAndTrackAsync(
                    item, publishedFileId, update.ModId, update.SizeBytes, report, log, cancellationToken)
                    .ConfigureAwait(false);

                if (!await WaitForFileAsync(pakPath, TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false) ||
                    !HasLocalModFileChanged(pakPath, hadBefore, beforeUtc, beforeSize))
                {
                    throw new InvalidOperationException(
                        "Steam сообщил успешную загрузку, но локальный файл мода не изменился: " + pakPath);
                }

                log("Форс-обновление применено: " + update.ModId);
            }
        }

        private static async Task DownloadAndTrackAsync(
            Steamworks.Ugc.Item item,
            Steamworks.Data.PublishedFileId publishedFileId,
            string modId,
            long publishedSize,
            Action<long, long> report,
            Action<string> log,
            CancellationToken cancellationToken)
        {
            SteamUGC.Download(publishedFileId, true);

            var deadline = DateTime.UtcNow.AddHours(2);
            var waitStartedUtc = DateTime.UtcNow;
            var sawDownloading = false;

            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var downloading = item.IsDownloading;
                var pending = item.IsDownloadPending;
                if (downloading)
                {
                    sawDownloading = true;
                }

                var size = publishedSize > 0 ? publishedSize : item.SizeBytes;

                var amount = sawDownloading
                    ? Math.Max(0d, Math.Min(1d, item.DownloadAmount))
                    : 0d;

                report((long)(size * amount), size);

                var settled = item.IsInstalled && !item.NeedsUpdate && !downloading && !pending;
                if (settled && (sawDownloading || DateTime.UtcNow - waitStartedUtc > TimeSpan.FromSeconds(6)))
                {
                    report(size, size);
                    return;
                }

                if (!sawDownloading && !pending &&
                    DateTime.UtcNow - waitStartedUtc > TimeSpan.FromSeconds(90))
                {
                    break;
                }

                await Task.Delay(400, cancellationToken).ConfigureAwait(false);
            }

            if (item.IsInstalled && !item.NeedsUpdate)
            {
                return;
            }

            throw new InvalidOperationException("Steam не смог завершить загрузку мода " + modId + ".");
        }

        private static async Task<Steamworks.Ugc.Item> ForceRedownloadAsync(
            Steamworks.Ugc.Item item,
            Steamworks.Data.PublishedFileId publishedFileId,
            string modId,
            string workshopContentRoot,
            Action<string> log,
            CancellationToken cancellationToken)
        {
            await item.Unsubscribe().ConfigureAwait(false);
            await Task.Delay(1200, cancellationToken).ConfigureAwait(false);

            DeleteWorkshopFolder(workshopContentRoot, modId, log);

            var refreshed = await SteamUGC.QueryFileAsync(publishedFileId).ConfigureAwait(false);
            var target = refreshed.HasValue ? refreshed.Value : item;

            if (!await target.Subscribe().ConfigureAwait(false))
            {
                throw new InvalidOperationException("Не удалось переподписаться на мод " + modId + ".");
            }

            await Task.Delay(600, cancellationToken).ConfigureAwait(false);

            var afterSubscribe = await SteamUGC.QueryFileAsync(publishedFileId).ConfigureAwait(false);
            return afterSubscribe.HasValue ? afterSubscribe.Value : target;
        }

        private static void DeleteWorkshopFolder(string workshopContentRoot, string modId, Action<string> log)
        {
            if (string.IsNullOrWhiteSpace(modId) || !modId.All(char.IsDigit))
            {
                return;
            }

            var directory = Path.Combine(workshopContentRoot, modId);
            if (!Directory.Exists(directory))
            {
                return;
            }

            try
            {
                Directory.Delete(directory, true);
                log?.Invoke("Локальные файлы мода удалены, будет полная перекачка: " + modId);
            }
            catch (Exception ex)
            {
                log?.Invoke("Не удалось удалить папку мода " + modId + ": " + ex.Message);
            }
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

            var addresses = await Dns.GetHostAddressesAsync(host).ConfigureAwait(false);
            var address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                          ?? addresses.FirstOrDefault();
            if (address == null)
            {
                throw new InvalidOperationException("Не удалось разрешить адрес сервера: " + host);
            }

            using (var udp = new UdpClient(address.AddressFamily))
            {
                udp.Client.ReceiveTimeout = 3500;
                udp.Client.SendTimeout = 3500;

                var endpoint = new IPEndPoint(address, queryPort);
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
            if (buffer == null || buffer.Length < 6 || buffer[4] != 0x49)
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

        private static void RequireGameExe(string conanExePath)
        {
            if (string.IsNullOrWhiteSpace(conanExePath) || !File.Exists(conanExePath))
            {
                throw new InvalidOperationException("Не найден ConanSandbox.exe. Укажите корректный путь в настройках.");
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
            return info.LastWriteTimeUtc > beforeUtc.AddSeconds(1) || info.Length != beforeSize;
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

        public static string ResolveWorkshopContentRoot(string conanExePath)
        {
            var steamappsDirectory = ResolveSteamappsDirectory(conanExePath);
            return Path.Combine(steamappsDirectory, "workshop", "content", ConanSteamAppId.ToString());
        }

        private static string[] BuildAbsoluteModEntries(string workshopContentRoot, IEnumerable<string> mods, Action<string> log)
        {
            var entries = new List<string>();

            foreach (var entry in ParseModEntries(mods))
            {
                var fullPath = Path.Combine(workshopContentRoot, entry.ModId, entry.PakName);
                entries.Add(fullPath);

                if (!File.Exists(fullPath))
                {
                    log("ВНИМАНИЕ: файл мода пока не найден: " + fullPath);
                }
            }

            return entries.ToArray();
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
                if (string.IsNullOrWhiteSpace(modId) || string.IsNullOrWhiteSpace(pakName) || !modId.All(char.IsDigit))
                {
                    continue;
                }

                entries.Add(new ModEntry { ModId = modId, PakName = pakName });
            }

            return entries;
        }

        private sealed class ModEntry
        {
            public string ModId { get; set; }
            public string PakName { get; set; }
        }

        public sealed class ModListSnapshot
        {
            public bool Exists { get; set; }
            public byte[] Bytes { get; set; }
        }
    }
}
