using Microsoft.Win32;
using RealmLauncher.Models;
using RealmLauncher.Services;
using System;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Newtonsoft.Json.Linq;

namespace RealmLauncher
{
    public partial class MainWindow : Window
    {
        private const int ProgressScale = 1000;
        private const double StageConfigLoaded = 0.10;
        private const double StagePasswordValidated = 0.14;
        private const double StageSteamReady = 0.22;
        private const double StageAnalysisDone = 0.34;
        private const double StageModsStart = 0.34;
        private const double StageModsEnd = 0.90;
        private const double StageModlistDone = 0.96;
        private const double StageLaunched = 1.00;

        private readonly LauncherService _launcherService = new LauncherService();
        private readonly LauncherUpdateService _updateService = new LauncherUpdateService();
        private readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(12)
        };
        private LauncherSettings _settings;
        private CancellationTokenSource _cts;

        public MainWindow()
        {
            InitializeComponent();
            ApplyThemeAssets();
            LoadSettings();
            ShowMainPage();
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadNewsAsync();
            await CheckLauncherUpdateAsync(false);
        }

        private void ApplyThemeAssets()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var repoRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", ".."));
            var logoPath = PickExisting(new[]
            {
                Path.Combine(baseDir, "Assets", "realm_logo.png"),
                Path.Combine(baseDir, "Assets", "logo.png"),
                Path.Combine(baseDir, "Assets", "realm_emblem.png"),
                Path.Combine(repoRoot, "Assets", "realm_logo.png"),
                Path.Combine(repoRoot, "Assets", "logo.png"),
                Path.Combine(repoRoot, "Assets", "realm_emblem.png")
            });

            if (logoPath != null)
            {
                imgLogo.Source = new BitmapImage(new Uri(logoPath));
            }

            var bgPath = PickExisting(new[]
            {
                Path.Combine(baseDir, "Assets", "bg.png"),
                Path.Combine(baseDir, "Assets", "bg.jpg"),
                Path.Combine(repoRoot, "Assets", "bg.png"),
                Path.Combine(repoRoot, "Assets", "bg.jpg")
            });

            if (bgPath != null)
            {
                Background = new ImageBrush(new BitmapImage(new Uri(bgPath)))
                {
                    Stretch = Stretch.UniformToFill,
                    Opacity = 0.16
                };
            }
        }

        private static string PickExisting(string[] paths)
        {
            for (var i = 0; i < paths.Length; i++)
            {
                if (File.Exists(paths[i]))
                {
                    return paths[i];
                }
            }

            return null;
        }

        private void LoadSettings()
        {
            _settings = LauncherSettings.Load();
            txtConfigUrl.Text = _settings.ConfigUrl ?? string.Empty;
            txtConanExe.Text = _settings.ConanExePath ?? string.Empty;
            txtServerPassword.Password = _settings.ServerPassword ?? string.Empty;
            chkDisableIntro.IsChecked = _settings.DisableCinematicIntro;
            chkAutoSubscribe.IsChecked = _settings.AutomaticallySubscribeToWorkshopMods;
            UpdateSteamCmdStatus();
        }

        private void SaveSettings()
        {
            _settings.ConfigUrl = txtConfigUrl.Text.Trim();
            _settings.ConanExePath = txtConanExe.Text.Trim();
            _settings.ServerPassword = txtServerPassword.Password;
            _settings.DisableCinematicIntro = chkDisableIntro.IsChecked == true;
            _settings.AutomaticallySubscribeToWorkshopMods = chkAutoSubscribe.IsChecked == true;
            _settings.Save();
        }

        private void ShowMainPage()
        {
            MainPage.Visibility = Visibility.Visible;
            SettingsPage.Visibility = Visibility.Collapsed;
            btnOpenSettings.Visibility = Visibility.Visible;
            btnBackToMain.Visibility = Visibility.Collapsed;
        }

        private void ShowSettingsPage()
        {
            MainPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Visible;
            btnOpenSettings.Visibility = Visibility.Collapsed;
            btnBackToMain.Visibility = Visibility.Visible;
        }

        private void BtnOpenSettings_OnClick(object sender, RoutedEventArgs e)
        {
            ShowSettingsPage();
        }

        private void BtnBackToMain_OnClick(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            ShowMainPage();
        }

        private async Task LoadNewsAsync()
        {
            var newsUrl = ConfigurationManager.AppSettings["NewsFeedUrl"] ?? string.Empty;
            if (string.IsNullOrWhiteSpace(newsUrl))
            {
                txtNews.Text = "URL новостей не задан.\n\nДобавь ключ NewsFeedUrl в App.config и укажи raw-ссылку на Gist.";
                return;
            }

            try
            {
                var raw = await _httpClient.GetStringAsync(newsUrl);
                var newsText = NormalizeNewsText(raw);
                txtNews.Text = string.IsNullOrWhiteSpace(newsText)
                    ? "Лента новостей пуста."
                    : newsText.Trim();
            }
            catch (Exception ex)
            {
                txtNews.Text = "Не удалось загрузить новости.\n\n" + ex.Message;
            }
        }

        private static string NormalizeNewsText(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            var text = raw.Trim();
            if (text.StartsWith("{") || text.StartsWith("["))
            {
                try
                {
                    var token = JToken.Parse(text);

                    var directNewsToken = token["news"];
                    var directNews = directNewsToken != null ? directNewsToken.ToString() : null;
                    if (!string.IsNullOrWhiteSpace(directNews))
                    {
                        return directNews;
                    }

                    var items = token["items"] as JArray;
                    if (items != null && items.Count > 0)
                    {
                        var lines = items
                            .Select(item =>
                            {
                                var titleToken = item["title"];
                                var bodyToken = item["body"];
                                var title = titleToken != null ? titleToken.ToString() : null;
                                var body = bodyToken != null ? bodyToken.ToString() : null;
                                if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(body))
                                {
                                    return "• " + title.Trim() + "\n" + body.Trim();
                                }
                                if (!string.IsNullOrWhiteSpace(title))
                                {
                                    return "• " + title.Trim();
                                }
                                return null;
                            })
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .ToList();

                        if (lines.Count > 0)
                        {
                            return string.Join("\n\n", lines);
                        }
                    }
                }
                catch
                {
                }
            }

            return text;
        }

        private async void BtnPlay_OnClick(object sender, RoutedEventArgs e)
        {
            try
            {
                ToggleUi(false);
                txtLog.Clear();
                AppendLog("Старт REALM RolePlay Launcher...");
                StartProgress("Инициализация...");
                SaveSettings();
                ShowMainPage();

                if (_cts != null)
                {
                    _cts.Dispose();
                }
                _cts = new CancellationTokenSource();

                SetStatus("Скачиваю конфиг сервера...");
                var config = await _launcherService.DownloadConfigAsync(_settings.ConfigUrl, _cts.Token);
                SetProgress(StageConfigLoaded, "Конфиг сервера загружен.");
                AppendLog(string.Format("Сервер: {0}", config.Name));
                AppendLog(string.Format("IP: {0}", config.Ip));
                AppendLog(string.Format("Модов в списке: {0}", config.Mods.Count));

                if (!ValidateServerPassword(config))
                {
                    SetStatus("Неверный пароль сервера.");
                    return;
                }
                SetProgress(StagePasswordValidated, "Пароль сервера проверен.");

                if (chkDisableIntro.IsChecked == true)
                {
                    _launcherService.DisableCinematicIntro(_settings.ConanExePath, AppendLog);
                }

                var steamReady = await EnsureSteamCmdInstalledAsync();
                if (!steamReady)
                {
                    SetStatus("SteamCMD не установлен.");
                    return;
                }
                SetProgress(StageSteamReady, "SteamCMD готов.");

                SetStatus("Проверка актуальности модов...");
                var analysis = await _launcherService.AnalyzeModsAsync(_settings.ConanExePath, config.Mods, AppendLog, _cts.Token);
                SetProgress(StageAnalysisDone, "Проверка модов завершена.");

                if (chkAutoSubscribe.IsChecked == true)
                {
                    var idsForSubscription = analysis.Updates
                        .Where(x => string.Equals(x.Status, "Отсутствует", StringComparison.OrdinalIgnoreCase))
                        .Select(x => x.ModId)
                        .Distinct()
                        .ToList();
                    _launcherService.TryOpenWorkshopPagesForSubscription(idsForSubscription, AppendLog);
                }

                if (analysis.Updates.Count > 0)
                {
                    if (!ConfirmUpdates(analysis))
                    {
                        SetStatus("Обновление модов отменено пользователем.");
                        return;
                    }

                    var uniqueUpdates = analysis.Updates
                        .GroupBy(x => x.ModId)
                        .Select(g => g.First())
                        .ToList();

                    SetProgress(StageModsStart, "Проверяю и обновляю моды...");
                    await _launcherService.SyncModsAsync(_settings.ConanExePath, uniqueUpdates, AppendLog, UpdateModSyncProgress, _cts.Token);
                    SetProgress(StageModsEnd, "Моды синхронизированы.");
                }
                else
                {
                    AppendLog("Все моды актуальны, обновление не требуется.");
                    SetProgress(StageModsEnd, "Обновление модов не требуется.");
                }

                SetStatus("Обновляю modlist.txt...");
                var modListPath = _launcherService.WriteModListFile(_settings.ConanExePath, config.Mods, AppendLog);
                AppendLog("modlist.txt обновлён: " + modListPath);
                SetProgress(StageModlistDone, "modlist.txt обновлён.");

                SetStatus("Запускаю подключение к серверу...");
                _launcherService.LaunchServerConnection(_settings.ConanExePath, config.Ip);
                AppendLog("Игра запущена с авто-подключением.");
                SetProgress(StageLaunched, "Готово. Игра запускается.");
            }
            catch (Exception ex)
            {
                SetStatus("Ошибка.");
                AppendLog("ОШИБКА: " + ex.Message);
                MessageBox.Show(this, ex.Message, "REALM RolePlay Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ToggleUi(true);
            }
        }

        private async void BtnCheckSteamCmd_OnClick(object sender, RoutedEventArgs e)
        {
            try
            {
                ToggleUi(false);
                if (_launcherService.IsSteamCmdInstalled())
                {
                    UpdateSteamCmdStatus();
                    AppendLog("SteamCMD уже установлен.");
                    MessageBox.Show(this, "SteamCMD уже установлен и готов к работе.\n\nПуть:\n" + _launcherService.GetSteamCmdPath(),
                        "REALM RolePlay Launcher", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var installed = await AskAndInstallSteamCmdAsync();
                if (!installed)
                {
                    SetStatus("Установка SteamCMD отменена.");
                    return;
                }

                SetStatus("SteamCMD установлен и готов.");
            }
            catch (Exception ex)
            {
                SetStatus("Ошибка.");
                AppendLog("ОШИБКА: " + ex.Message);
                MessageBox.Show(this, ex.Message, "REALM RolePlay Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ToggleUi(true);
            }
        }

        private async void BtnCheckUpdates_OnClick(object sender, RoutedEventArgs e)
        {
            await CheckLauncherUpdateAsync(true);
        }

        private void BtnCloseApp_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void HeaderBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void BtnBrowseConanExe_OnClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "ConanSandbox.exe|ConanSandbox.exe|Исполняемые файлы (*.exe)|*.exe|Все файлы (*.*)|*.*",
                Title = "Выберите ConanSandbox.exe"
            };

            if (dialog.ShowDialog(this) == true)
            {
                txtConanExe.Text = dialog.FileName;
            }
        }

        private void ToggleUi(bool enabled)
        {
            txtConfigUrl.IsEnabled = enabled;
            txtConanExe.IsEnabled = enabled;
            txtServerPassword.IsEnabled = enabled;
            chkDisableIntro.IsEnabled = enabled;
            chkAutoSubscribe.IsEnabled = enabled;
            btnCheckUpdates.IsEnabled = enabled;
            btnCheckSteamCmd.IsEnabled = enabled;
            btnBrowseConanExe.IsEnabled = enabled;
            btnPlay.IsEnabled = enabled;
            btnBackToMain.IsEnabled = enabled;
            btnOpenSettings.IsEnabled = enabled;
        }

        private bool ValidateServerPassword(ServerConfig config)
        {
            var entered = txtServerPassword.Password ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(config.PasswordSha256))
            {
                var enteredHash = ComputeSha256(entered);
                if (!string.Equals(enteredHash, config.PasswordSha256.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(this, "Неверный пароль сервера.", "REALM RolePlay Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
            }
            else if (!string.IsNullOrWhiteSpace(config.Password))
            {
                if (!string.Equals(entered, config.Password, StringComparison.Ordinal))
                {
                    MessageBox.Show(this, "Неверный пароль сервера.", "REALM RolePlay Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
            }

            return true;
        }

        private static string ComputeSha256(string input)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(input ?? string.Empty);
                var hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                for (var i = 0; i < hash.Length; i++)
                {
                    sb.Append(hash[i].ToString("x2"));
                }
                return sb.ToString();
            }
        }

        private bool ConfirmUpdates(ModUpdateAnalysis analysis)
        {
            var totalMb = analysis.TotalSizeBytes() / 1024d / 1024d;
            var lines = analysis.Updates
                .Take(20)
                .Select(x =>
                {
                    var sizeText = x.SizeBytes > 0 ? string.Format("{0:0.0} MB", x.SizeBytes / 1024d / 1024d) : "размер неизвестен";
                    return string.Format("- [{0}] {1}/{2} ({3})", x.Status, x.ModId, x.PakName, sizeText);
                })
                .ToList();

            if (analysis.Updates.Count > 20)
            {
                lines.Add(string.Format("- ... и ещё {0} мод(ов)", analysis.Updates.Count - 20));
            }

            var message =
                string.Format("Найдено модов для установки/обновления: {0}\n", analysis.Updates.Count) +
                string.Format("Оценочный размер загрузки: {0:0.0} MB\n\n", totalMb) +
                string.Join("\n", lines) +
                "\n\nПродолжить?";

            return MessageBox.Show(this, message, "Подтверждение обновления модов",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        }

        private async Task<bool> EnsureSteamCmdInstalledAsync()
        {
            if (_launcherService.IsSteamCmdInstalled())
            {
                UpdateSteamCmdStatus();
                return true;
            }

            AppendLog("SteamCMD не найден.");
            return await AskAndInstallSteamCmdAsync();
        }

        private async Task<bool> AskAndInstallSteamCmdAsync()
        {
            var steamCmdPath = _launcherService.GetSteamCmdPath();
            var message =
                "SteamCMD не установлен.\n\n" +
                "Зачем он нужен:\n" +
                "- автоматическая загрузка и обновление модов Conan Exiles;\n" +
                "- проверка актуальности модов перед запуском.\n\n" +
                "SteamCMD будет установлен в папку лаунчера:\n" +
                steamCmdPath + "\n\n" +
                "Установить сейчас?";

            if (MessageBox.Show(this, message, "Установка SteamCMD", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                UpdateSteamCmdStatus();
                return false;
            }

            SetStatus("Установка SteamCMD...");
            await _launcherService.InstallSteamCmdAsync(AppendLog, _cts != null ? _cts.Token : CancellationToken.None);
            UpdateSteamCmdStatus();
            return true;
        }

        private void UpdateSteamCmdStatus()
        {
            lblSteamCmdStatus.Text = _launcherService.IsSteamCmdInstalled()
                ? "SteamCMD: установлен"
                : "SteamCMD: не установлен";
        }

        private void StartProgress(string status)
        {
            progressMods.Minimum = 0;
            progressMods.Maximum = ProgressScale;
            progressMods.Value = 0;
            if (!string.IsNullOrWhiteSpace(status))
            {
                SetStatus(status);
            }
        }

        private void SetProgress(double fraction, string status)
        {
            var clamped = Math.Max(0d, Math.Min(1d, fraction));
            progressMods.Value = (int)Math.Round(clamped * ProgressScale);
            if (!string.IsNullOrWhiteSpace(status))
            {
                SetStatus(status);
            }
        }

        private void UpdateModSyncProgress(double current, double total, string modLabel)
        {
            var totalSafe = Math.Max(1d, total);
            var modFraction = Math.Max(0d, Math.Min(1d, current / totalSafe));
            var overall = StageModsStart + ((StageModsEnd - StageModsStart) * modFraction);

            var totalInt = (int)Math.Round(totalSafe);
            var completed = (int)Math.Floor(Math.Max(0d, Math.Min(totalSafe, current)));
            var inCurrentPercent = (int)Math.Round((Math.Max(0d, current - completed)) * 100d);
            if (inCurrentPercent > 100)
            {
                inCurrentPercent = 100;
            }

            var status = string.Format("Обновление модов: {0}/{1} ({2}% - {3})", completed, totalInt, inCurrentPercent, modLabel);
            Dispatcher.Invoke(() => SetProgress(overall, status));
        }

        private async Task CheckLauncherUpdateAsync(bool userInitiated)
        {
            var manifestUrl = ConfigurationManager.AppSettings["UpdateManifestUrl"] ?? string.Empty;
            if (string.IsNullOrWhiteSpace(manifestUrl))
            {
                if (userInitiated)
                {
                    MessageBox.Show(this, "URL манифеста обновлений не задан (UpdateManifestUrl в App.config).",
                        "Проверка обновлений", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return;
            }

            try
            {
                if (userInitiated)
                {
                    ToggleUi(false);
                    StartProgress("Проверка обновлений лаунчера...");
                }

                var currentVersion = typeof(MainWindow).Assembly.GetName().Version ?? new Version(1, 0, 0, 0);
                var result = await _updateService.CheckForUpdatesAsync(manifestUrl, currentVersion, CancellationToken.None);
                if (!result.IsUpdateAvailable || result.Manifest == null)
                {
                    if (userInitiated)
                    {
                        SetProgress(1.0, "Обновлений не найдено.");
                        MessageBox.Show(this, "Установлена последняя версия (" + currentVersion + ").",
                            "Проверка обновлений", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    return;
                }

                var sizeText = result.Manifest.SizeBytes.HasValue && result.Manifest.SizeBytes.Value > 0
                    ? FormatSize(result.Manifest.SizeBytes.Value)
                    : "размер неизвестен";
                var changelog = string.IsNullOrWhiteSpace(result.Manifest.Changelog)
                    ? string.Empty
                    : ("\n\nИзменения:\n" + result.Manifest.Changelog.Trim());

                var message =
                    "Доступно обновление лаунчера.\n\n" +
                    "Текущая версия: " + result.CurrentVersion + "\n" +
                    "Новая версия: " + result.LatestVersion + "\n" +
                    "Размер: " + sizeText + changelog + "\n\nСкачать и установить сейчас?";

                if (MessageBox.Show(this, message, "Обновление лаунчера", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                {
                    return;
                }

                await DownloadAndApplyLauncherUpdateAsync(result.Manifest);
            }
            catch (Exception ex)
            {
                if (userInitiated)
                {
                    MessageBox.Show(this, "Не удалось проверить/установить обновление:\n" + ex.Message,
                        "Обновление лаунчера", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                AppendLog("ОШИБКА обновления лаунчера: " + ex.Message);
            }
            finally
            {
                if (userInitiated)
                {
                    ToggleUi(true);
                }
            }
        }

        private async Task DownloadAndApplyLauncherUpdateAsync(LauncherUpdateManifest manifest)
        {
            StartProgress("Скачиваю обновление лаунчера...");
            SetProgress(0.05, "Подготовка к скачиванию обновления...");

            var packagePath = await _updateService.DownloadPackageAsync(
                manifest,
                (downloaded, total) =>
                {
                    var fraction = 0.0;
                    if (total.HasValue && total.Value > 0)
                    {
                        fraction = Math.Max(0d, Math.Min(1d, downloaded / (double)total.Value));
                    }

                    var totalLabel = total.HasValue && total.Value > 0 ? FormatSize(total.Value) : "неизвестно";
                    var status = string.Format("Скачивание обновления: {0} / {1}", FormatSize(downloaded), totalLabel);
                    Dispatcher.Invoke(() => SetProgress(0.05 + (0.85 * fraction), status));
                },
                CancellationToken.None);

            SetProgress(0.95, "Устанавливаю обновление...");
            _updateService.InstallAndRestart(packagePath);
            SetProgress(1.0, "Обновление установлено. Перезапуск...");
            Application.Current.Shutdown();
        }

        private static string FormatSize(long bytes)
        {
            if (bytes <= 0) return "0 B";
            var units = new[] { "B", "KB", "MB", "GB" };
            var size = (double)bytes;
            var unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }
            return string.Format("{0:0.##} {1}", size, units[unit]);
        }

        private void SetStatus(string text)
        {
            lblStatus.Text = text;
        }

        private void AppendLog(string line)
        {
            Dispatcher.Invoke(() =>
            {
                var message = string.Format("[{0:HH:mm:ss}] {1}", DateTime.Now, line);
                if (txtLog.Text.Length == 0)
                {
                    txtLog.Text = message;
                }
                else
                {
                    txtLog.AppendText(Environment.NewLine + message);
                }

                txtLog.CaretIndex = txtLog.Text.Length;
                txtLog.ScrollToEnd();
            });
        }
    }
}
