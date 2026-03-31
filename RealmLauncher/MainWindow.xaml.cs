using Microsoft.Win32;
using RealmLauncher.Models;
using RealmLauncher.Services;
using RealmLauncher.Ui;
using RealmLauncher.Views;
using System;
using System.Diagnostics;
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
using System.Windows.Threading;
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
        private readonly System.Collections.Generic.HashSet<string> _allowedHosts = AppRuntimeConfig.BuildAllowedHosts();
        private readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(12)
        };
        private LauncherSettings _settings;
        private CancellationTokenSource _cts;
        private readonly DispatcherTimer _serverStatusTimer;
        private bool _isRefreshingServerStatus;
        private string _serverStatusText = "проверка...";
        private string _serverPlayersText = "Игроки: --/--";
        private Brush _serverStatusBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F4C542"));

        private TextBox txtConfigUrl => txtConfigUrlInput;
        private PasswordBox txtServerPassword => txtServerPasswordInput;
        private TextBox txtNews => txtNewsBox;
        private TextBlock lblSteamStatusCtl => lblSteamCmdStatus;
        private System.Windows.Shapes.Ellipse serverStatusDotCtl => serverStatusDot;
        private TextBlock lblServerStatusCtl => lblServerStatusText;
        private TextBlock lblPlayersCtl => lblPlayersCount;
        private TextBox txtLog => txtLogBox;
        private TextBlock lblStatusCtl => lblStatus;
        private ProgressBar progressModsCtl => progressMods;
        private Button btnPlay => btnPlayMain;
        private Button btnDiscordCtl => btnDiscord;

        private TextBox txtConanExe => SettingsPage.txtConanExe;
        private CheckBox chkDisableIntro => SettingsPage.chkDisableIntro;
        private CheckBox chkAutoSubscribe => SettingsPage.chkAutoSubscribe;
        private Button btnCheckUpdates => SettingsPage.btnCheckUpdates;
        private Button btnCheckSteamCmd => SettingsPage.btnCheckSteamCmd;
        private Button btnBrowseConanExe => SettingsPage.btnBrowseConanExe;

        public MainWindow()
        {
            InitializeComponent();
            WirePageEvents();
            ApplyThemeAssets();
            LoadSettings();
            ShowMainPage();
            _serverStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _serverStatusTimer.Tick += async (_, __) => await RefreshServerStatusAsync();
            SizeChanged += MainWindow_SizeChanged;
            Loaded += MainWindow_OnLoadedSetClip;
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_OnLoadedSetClip(object sender, RoutedEventArgs e)
        {
            UpdateWindowClip();
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateWindowClip();
        }

        private void UpdateWindowClip()
        {
            Clip = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), 17, 17);
        }

        private void WirePageEvents()
        {
            btnPlay.Click += BtnPlay_OnClick;
            btnDiscordCtl.Click += BtnOpenDiscord_OnClick;
            SettingsPage.btnCheckSteamCmd.Click += BtnCheckSteamCmd_OnClick;
            SettingsPage.btnCheckUpdates.Click += BtnCheckUpdates_OnClick;
            SettingsPage.btnBrowseConanExe.Click += BtnBrowseConanExe_OnClick;
        }

        private void BtnOpenDiscord_OnClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var url = AppRuntimeConfig.DiscordInviteUrl;
                var discordUri = UrlSecurity.RequireAllowedHttpsUrl(url, _allowedHosts, "DiscordInviteUrl");
                Process.Start(new ProcessStartInfo(discordUri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ShowError("Не удалось открыть ссылку Discord:\n" + ex.Message);
            }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadNewsAsync();
            await RefreshServerStatusAsync();
            _serverStatusTimer.Start();
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
            var defaultConfigUrl = AppRuntimeConfig.ServerConfigUrl;
            txtConfigUrl.Text = !string.IsNullOrWhiteSpace(_settings.ConfigUrl)
                ? _settings.ConfigUrl
                : defaultConfigUrl;
            txtConanExe.Text = _settings.ConanExePath ?? string.Empty;
            txtServerPassword.Password = _settings.GetServerPassword();
            chkDisableIntro.IsChecked = _settings.DisableCinematicIntro;
            chkAutoSubscribe.IsChecked = _settings.AutomaticallySubscribeToWorkshopMods;
            UpdateSteamCmdStatus();
        }

        private void SaveSettings()
        {
            _settings.ConfigUrl = txtConfigUrl.Text.Trim();
            _settings.ConanExePath = txtConanExe.Text.Trim();
            _settings.SetServerPassword(txtServerPassword.Password);
            _settings.DisableCinematicIntro = chkDisableIntro.IsChecked == true;
            _settings.AutomaticallySubscribeToWorkshopMods = chkAutoSubscribe.IsChecked == true;
            _settings.Save();
        }

        private void ShowMainPage()
        {
            MainPageGrid.Visibility = Visibility.Visible;
            SettingsPage.Visibility = Visibility.Collapsed;
            btnOpenSettings.Visibility = Visibility.Visible;
            btnBackToMain.Visibility = Visibility.Collapsed;
        }

        private void ShowSettingsPage()
        {
            MainPageGrid.Visibility = Visibility.Collapsed;
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
            var newsUrl = AppRuntimeConfig.NewsFeedUrl;
            if (string.IsNullOrWhiteSpace(newsUrl))
            {
                txtNews.Text = "URL новостей не задан.\n\nДобавь ключ NewsFeedUrl в AppRuntimeConfig и укажи raw-ссылку на Gist.";
                return;
            }

            try
            {
                var newsUri = UrlSecurity.RequireAllowedHttpsUrl(newsUrl, _allowedHosts, "NewsFeedUrl");
                var raw = await _httpClient.GetStringAsync(newsUri);
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
                var config = await _launcherService.DownloadConfigAsync(_settings.ConfigUrl, _allowedHosts, _cts.Token);
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

                await EnsureSteamClientReadyAsync();
                _launcherService.EnsureSteamworksInitialized(AppendLog);
                SetProgress(StageSteamReady, "Steam готов.");

                SetStatus("Проверка актуальности модов...");
                var analysis = await _launcherService.AnalyzeModsAsync(_settings.ConanExePath, config.Mods, AppendLog, _cts.Token);
                SetProgress(StageAnalysisDone, "Проверка модов завершена.");

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
                    SetProgress(StageModsStart, "Синхронизирую моды через Steamworks...");
                    await _launcherService.SyncModsWithSteamworksAsync(
                        _settings.ConanExePath,
                        uniqueUpdates,
                        chkAutoSubscribe.IsChecked == true,
                        AppendLog,
                        UpdateModSyncProgress,
                        _cts.Token);
                    SetProgress(StageModsEnd, "Моды синхронизированы через Steamworks.");
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
                ShowError(ex.Message);
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
                if (IsSteamClientRunning())
                {
                    UpdateSteamCmdStatus();
                    AppendLog("Steam уже запущен.");
                    ShowInfo("Steam уже запущен и готов к загрузке модов.");
                    return;
                }

                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "steam://open/main",
                        UseShellExecute = true
                    });
                }
                catch
                {
                }

                for (var i = 0; i < 10; i++)
                {
                    if (IsSteamClientRunning())
                    {
                        break;
                    }
                    await Task.Delay(500);
                }

                UpdateSteamCmdStatus();
                SetStatus(IsSteamClientRunning() ? "Steam запущен и готов." : "Steam не удалось запустить автоматически.");
            }
            catch (Exception ex)
            {
                SetStatus("Ошибка.");
                AppendLog("ОШИБКА: " + ex.Message);
                ShowError(ex.Message);
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
            btnDiscordCtl.IsEnabled = enabled;
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
                    ShowWarning("Неверный пароль сервера.");
                    return false;
                }
            }
            else if (!string.IsNullOrWhiteSpace(config.Password))
            {
                if (!string.Equals(entered, config.Password, StringComparison.Ordinal))
                {
                    ShowWarning("Неверный пароль сервера.");
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

            return AskYesNo(message, "Подтверждение обновления модов");
        }

        private static bool IsSteamClientRunning()
        {
            try
            {
                return Process.GetProcessesByName("steam").Any();
            }
            catch
            {
                return false;
            }
        }

        private async Task EnsureSteamClientReadyAsync()
        {
            if (IsSteamClientRunning())
            {
                UpdateSteamCmdStatus();
                return;
            }

            AppendLog("Steam не запущен. Пытаюсь запустить Steam...");
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "steam://open/main",
                    UseShellExecute = true
                });
            }
            catch
            {
            }

            for (var i = 0; i < 20; i++)
            {
                if (IsSteamClientRunning())
                {
                    UpdateSteamCmdStatus();
                    return;
                }
                await Task.Delay(500);
            }

            throw new InvalidOperationException("Steam не запущен. Запустите клиент Steam и повторите.");
        }

        private void UpdateSteamCmdStatus()
        {
            var steamText = IsSteamClientRunning()
                ? "Steam: запущен"
                : "Steam: не запущен";
            lblSteamStatusCtl.Text = steamText;
            lblServerStatusCtl.Text = "Сервер: " + _serverStatusText;
            lblPlayersCtl.Text = _serverPlayersText;
            serverStatusDotCtl.Fill = _serverStatusBrush;
        }

        private async Task RefreshServerStatusAsync()
        {
            if (_isRefreshingServerStatus)
            {
                return;
            }

            _isRefreshingServerStatus = true;
            try
            {
                _serverStatusText = "проверка...";
                _serverPlayersText = "Игроки: --/--";
                _serverStatusBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F4C542"));
                UpdateSteamCmdStatus();

                var configUrl = !string.IsNullOrWhiteSpace(txtConfigUrl.Text)
                    ? txtConfigUrl.Text.Trim()
                    : AppRuntimeConfig.ServerConfigUrl;

                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
                {
                    var config = await _launcherService.DownloadConfigAsync(configUrl, _allowedHosts, cts.Token);
                    var host = ExtractHost(config.Ip);
                    var queryPort = config.QueryPort ?? AppRuntimeConfig.DefaultQueryPort;
                    var serverInfo = await _launcherService.QueryServerInfoAsync(host, queryPort, cts.Token);

                    if (serverInfo.IsOnline)
                    {
                        _serverStatusText = "онлайн";
                        _serverPlayersText = string.Format("Игроки: {0}/{1}", serverInfo.Players, serverInfo.MaxPlayers);
                        _serverStatusBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4BE37D"));
                    }
                    else
                    {
                        _serverStatusText = "офлайн";
                        _serverPlayersText = "Игроки: 0/0";
                        _serverStatusBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6666"));
                    }
                }
            }
            catch
            {
                _serverStatusText = "недоступен";
                _serverPlayersText = "Игроки: --/--";
                _serverStatusBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6666"));
            }
            finally
            {
                UpdateSteamCmdStatus();
                _isRefreshingServerStatus = false;
            }
        }

        private static string ExtractHost(string ipWithPort)
        {
            if (string.IsNullOrWhiteSpace(ipWithPort))
            {
                return string.Empty;
            }

            var raw = ipWithPort.Trim();
            var colonIndex = raw.LastIndexOf(':');
            if (colonIndex > 0 && raw.Count(c => c == ':') == 1)
            {
                return raw.Substring(0, colonIndex);
            }

            return raw;
        }

        private void StartProgress(string status)
        {
            progressModsCtl.Minimum = 0;
            progressModsCtl.Maximum = ProgressScale;
            progressModsCtl.Value = 0;
            if (!string.IsNullOrWhiteSpace(status))
            {
                SetStatus(status);
            }
        }

        private void SetProgress(double fraction, string status)
        {
            var clamped = Math.Max(0d, Math.Min(1d, fraction));
            progressModsCtl.Value = (int)Math.Round(clamped * ProgressScale);
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
            var manifestUrl = AppRuntimeConfig.UpdateManifestUrl;
            if (string.IsNullOrWhiteSpace(manifestUrl))
            {
                if (userInitiated)
                {
                    ShowInfo("URL манифеста обновлений не задан (UpdateManifestUrl в AppRuntimeConfig).", "Проверка обновлений");
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
                var result = await _updateService.CheckForUpdatesAsync(manifestUrl, currentVersion, _allowedHosts, CancellationToken.None);
                if (!result.IsUpdateAvailable || result.Manifest == null)
                {
                    if (userInitiated)
                    {
                        SetProgress(1.0, "Обновлений не найдено.");
                        ShowInfo("Установлена последняя версия (" + currentVersion + ").", "Проверка обновлений");
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

                if (!AskYesNo(message, "Обновление лаунчера"))
                {
                    return;
                }

                await DownloadAndApplyLauncherUpdateAsync(result.Manifest);
            }
            catch (Exception ex)
            {
                if (userInitiated)
                {
                    ShowError("Не удалось проверить/установить обновление:\n" + ex.Message, "Обновление лаунчера");
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
                _allowedHosts,
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

        private void ShowInfo(string message, string title = "REALM RolePlay Launcher")
        {
            RealmDialog.Show(this, title, message, RealmDialogButtons.Ok, RealmDialogType.Info);
        }

        private void ShowWarning(string message, string title = "REALM RolePlay Launcher")
        {
            RealmDialog.Show(this, title, message, RealmDialogButtons.Ok, RealmDialogType.Warning);
        }

        private void ShowError(string message, string title = "REALM RolePlay Launcher")
        {
            RealmDialog.Show(this, title, message, RealmDialogButtons.Ok, RealmDialogType.Error);
        }

        private bool AskYesNo(string message, string title = "REALM RolePlay Launcher")
        {
            return RealmDialog.Show(this, title, message, RealmDialogButtons.YesNo, RealmDialogType.Question) == MessageBoxResult.Yes;
        }

        private void SetStatus(string text)
        {
            lblStatusCtl.Text = text;
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



