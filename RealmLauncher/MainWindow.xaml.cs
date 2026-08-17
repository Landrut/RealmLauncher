using Microsoft.Win32;
using RealmLauncher.Models;
using RealmLauncher.Services;
using RealmLauncher.Theme;
using RealmLauncher.Ui;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Threading;
using Ellipse = System.Windows.Shapes.Ellipse;
using Polygon = System.Windows.Shapes.Polygon;
using Polyline = System.Windows.Shapes.Polyline;
using Newtonsoft.Json.Linq;

namespace RealmLauncher
{
    public partial class MainWindow : Window
    {
        private const int ProgressScale = 1000;
        private const double StageConfigLoaded = 0.06;
        private const double StagePasswordValidated = 0.10;
        private const double StageSteamReady = 0.16;
        private const double StageAnalysisStart = 0.16;
        private const double StageAnalysisDone = 0.30;
        private const double StageModsStart = 0.30;
        private const double StageModsEnd = 0.92;
        private const double StageModlistDone = 0.96;
        private const double StageLaunched = 1.00;

        private const int MaxLogLines = 400;
        private static readonly TimeSpan ServerConfigCacheTtl = TimeSpan.FromMinutes(10);

        private readonly LauncherService _launcherService = new LauncherService();
        private readonly LauncherUpdateService _updateService = new LauncherUpdateService();
        private readonly HashSet<string> _allowedHosts = AppRuntimeConfig.BuildAllowedHosts();
        private readonly OnlineHistory _onlineHistory = new OnlineHistory();
        private readonly DiscordRichPresence _discord = new DiscordRichPresence(AppRuntimeConfig.DiscordApplicationId);
        private readonly ObservableCollection<ModUpdateInfo> _mods = new ObservableCollection<ModUpdateInfo>();

        private readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(12)
        };

        private static readonly Dictionary<string, string> EmojiIconUrls = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "📢", "https://cdnjs.cloudflare.com/ajax/libs/twemoji/14.0.2/72x72/1f4e2.png" },
            { "🔥", "https://cdnjs.cloudflare.com/ajax/libs/twemoji/14.0.2/72x72/1f525.png" },
            { "⚒️", "https://cdnjs.cloudflare.com/ajax/libs/twemoji/14.0.2/72x72/2692.png" },
            { "⚒", "https://cdnjs.cloudflare.com/ajax/libs/twemoji/14.0.2/72x72/2692.png" },
            { "🛠", "https://cdnjs.cloudflare.com/ajax/libs/twemoji/14.0.2/72x72/1f6e0.png" },
            { "🎣", "https://cdnjs.cloudflare.com/ajax/libs/twemoji/14.0.2/72x72/1f3a3.png" },
            { "🌿", "https://cdnjs.cloudflare.com/ajax/libs/twemoji/14.0.2/72x72/1f33f.png" },
            { "🎭", "https://cdnjs.cloudflare.com/ajax/libs/twemoji/14.0.2/72x72/1f3ad.png" },
            { "📜", "https://cdnjs.cloudflare.com/ajax/libs/twemoji/14.0.2/72x72/1f4dc.png" },
            { "📚", "https://cdnjs.cloudflare.com/ajax/libs/twemoji/14.0.2/72x72/1f4da.png" },
            { "🛡", "https://cdnjs.cloudflare.com/ajax/libs/twemoji/14.0.2/72x72/1f6e1.png" },
            { "♨️", "https://cdnjs.cloudflare.com/ajax/libs/twemoji/14.0.2/72x72/2668.png" },
            { "♨", "https://cdnjs.cloudflare.com/ajax/libs/twemoji/14.0.2/72x72/2668.png" },
            { "🍺", "https://cdnjs.cloudflare.com/ajax/libs/twemoji/14.0.2/72x72/1f37a.png" },
            { "🏛", "https://cdnjs.cloudflare.com/ajax/libs/twemoji/14.0.2/72x72/1f3db.png" },
            { "🧭", "https://cdnjs.cloudflare.com/ajax/libs/twemoji/14.0.2/72x72/1f9ed.png" },
            { "⚙️", "https://cdnjs.cloudflare.com/ajax/libs/twemoji/14.0.2/72x72/2699.png" },
            { "⚙", "https://cdnjs.cloudflare.com/ajax/libs/twemoji/14.0.2/72x72/2699.png" },
            { "⚔️", "https://cdnjs.cloudflare.com/ajax/libs/twemoji/14.0.2/72x72/2694.png" },
            { "⚔", "https://cdnjs.cloudflare.com/ajax/libs/twemoji/14.0.2/72x72/2694.png" },
            { "✅", "https://cdnjs.cloudflare.com/ajax/libs/twemoji/14.0.2/72x72/2705.png" },
            { "❗", "https://cdnjs.cloudflare.com/ajax/libs/twemoji/14.0.2/72x72/2757.png" },
            { "ℹ️", "https://cdnjs.cloudflare.com/ajax/libs/twemoji/14.0.2/72x72/2139.png" },
            { "ℹ", "https://cdnjs.cloudflare.com/ajax/libs/twemoji/14.0.2/72x72/2139.png" }
        };

        private static readonly Dictionary<string, BitmapImage> EmojiImageCache =
            new Dictionary<string, BitmapImage>(StringComparer.Ordinal);

        private readonly List<string> _logLines = new List<string>();

        private LauncherSettings _settings;
        private CancellationTokenSource _cts;
        private readonly DispatcherTimer _serverStatusTimer;

        private bool _isRefreshingServerStatus;
        private bool _isLoadingSettings;
        private bool _isClosing;
        private bool _isBusy;
        private bool _serverWasOffline;

        private bool _acceptModProgress;

        private double _progressBandLow = StageModsStart;
        private double _progressBandHigh = StageModsEnd;

        private bool _presencePlaying;
        private DateTime _presenceSince = DateTime.UtcNow;
        private bool? _lastServerOnline;
        private int _lastPlayers;
        private int _lastMaxPlayers;

        private string _rawNews;
        private ServerConfig _cachedServerConfig;
        private DateTime _cachedServerConfigUtc = DateTime.MinValue;

        private System.Windows.Forms.NotifyIcon _trayIcon;

        private PasswordBox txtServerPassword { get { return txtServerPasswordInput; } }
        private RichTextBox txtNews { get { return rtbNewsBox; } }
        private TextBox txtLog { get { return txtLogBox; } }
        private Button btnPlay { get { return btnPlayMain; } }

        private TextBox txtConanExe { get { return SettingsPage.txtConanExe; } }
        private CheckBox chkAutoSubscribe { get { return SettingsPage.chkAutoSubscribe; } }
        private CheckBox chkBattlEye { get { return SettingsPage.chkBattlEye; } }
        private CheckBox chkNotifyOnline { get { return SettingsPage.chkNotifyOnline; } }
        private CheckBox chkDiscordStatus { get { return SettingsPage.chkDiscordStatus; } }
        private CheckBox chkAutoStart { get { return SettingsPage.chkAutoStart; } }
        private CheckBox chkStartMinimized { get { return SettingsPage.chkStartMinimized; } }
        private Button btnCheckUpdates { get { return SettingsPage.btnCheckUpdates; } }
        private Button btnCheckSteamCmd { get { return SettingsPage.btnCheckSteamCmd; } }
        private Button btnBrowseConanExe { get { return SettingsPage.btnBrowseConanExe; } }
        private Button btnDetectGame { get { return SettingsPage.btnDetectGame; } }
        private Panel themeOptions { get { return SettingsPage.themeOptions; } }

        public MainWindow()
        {
            InitializeComponent();
            lstMods.ItemsSource = _mods;
            lstMods.AddHandler(Button.ClickEvent, new RoutedEventHandler(ModRow_OnButtonClick));

            WirePageEvents();
            ApplyBrandingAssets();
            LoadSettings();
            ShowMainPage();
            ShowNewsTab();

            _serverStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _serverStatusTimer.Tick += ServerStatusTimer_OnTick;

            Loaded += MainWindow_Loaded;
            Loaded += (s, e) => UpdateWindowClip();
            SizeChanged += (s, e) => UpdateWindowClip();
            sparklineHost.SizeChanged += (s, e) => RedrawSparkline();

            if (Environment.GetCommandLineArgs().Any(a =>
                    string.Equals(a, StartupManager.MinimizedArgument, StringComparison.OrdinalIgnoreCase)))
            {
                WindowState = WindowState.Minimized;
            }
        }

        private void UpdateWindowClip()
        {
            Clip = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), 16, 16);
        }

        private void WirePageEvents()
        {
            btnPlay.Click += BtnPlay_OnClick;
            btnDiscord.Click += BtnOpenDiscord_OnClick;

            SettingsPage.btnCheckSteamCmd.Click += BtnCheckSteamCmd_OnClick;
            SettingsPage.btnCheckUpdates.Click += BtnCheckUpdates_OnClick;
            SettingsPage.btnBrowseConanExe.Click += BtnBrowseConanExe_OnClick;
            SettingsPage.btnDetectGame.Click += BtnDetectGame_OnClick;

            SettingsPage.txtConanExe.TextChanged += SettingsControl_OnChanged;
            SettingsPage.chkAutoSubscribe.Checked += SettingsControl_OnChanged;
            SettingsPage.chkAutoSubscribe.Unchecked += SettingsControl_OnChanged;
            SettingsPage.chkBattlEye.Checked += SettingsControl_OnChanged;
            SettingsPage.chkBattlEye.Unchecked += SettingsControl_OnChanged;
            SettingsPage.chkNotifyOnline.Checked += SettingsControl_OnChanged;
            SettingsPage.chkNotifyOnline.Unchecked += SettingsControl_OnChanged;
            SettingsPage.chkDiscordStatus.Checked += DiscordStatusOption_OnChanged;
            SettingsPage.chkDiscordStatus.Unchecked += DiscordStatusOption_OnChanged;
            SettingsPage.chkDeveloperMode.Checked += DeveloperMode_OnChanged;
            SettingsPage.chkDeveloperMode.Unchecked += DeveloperMode_OnChanged;
            SettingsPage.chkAutoStart.Checked += StartupOption_OnChanged;
            SettingsPage.chkAutoStart.Unchecked += StartupOption_OnChanged;
            SettingsPage.chkStartMinimized.Checked += StartupOption_OnChanged;
            SettingsPage.chkStartMinimized.Unchecked += StartupOption_OnChanged;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            EnsureGameLocated(false);

            var newsTask = LoadNewsAsync();
            var statusTask = RefreshServerStatusAsync();
            await Task.WhenAll(newsTask, statusTask);

            _serverStatusTimer.Start();

            if (_discord.IsEnabled && chkDiscordStatus.IsChecked == true && await _discord.ConnectAsync())
            {
                AppendLog("Discord Rich Presence подключен.");
                ShowIdlePresence();
            }

            await CheckLauncherUpdateAsync(false);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            _isClosing = true;
            _serverStatusTimer.Stop();

            try
            {
                SaveSettings();
            }
            catch
            {
            }

            if (_cts != null)
            {
                try
                {
                    _cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            }

            _discord.Dispose();
            _httpClient.Dispose();

            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }

            base.OnClosing(e);
        }

        private void ServerStatusTimer_OnTick(object sender, EventArgs e)
        {
            var ignored = RefreshServerStatusAsync();
        }

        private void ApplyBrandingAssets()
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
                try
                {
                    var logo = new BitmapImage();
                    logo.BeginInit();
                    logo.CacheOption = BitmapCacheOption.OnLoad;
                    logo.UriSource = new Uri(logoPath);
                    logo.EndInit();
                    logo.Freeze();
                    imgLogo.Source = logo;
                }
                catch
                {
                }
            }

            var assemblyVersion = typeof(MainWindow).Assembly.GetName().Version;
            if (assemblyVersion != null)
            {
                txtLauncherVersion.Text = string.Format(
                    "v{0}.{1}.{2}.{3}",
                    Math.Max(0, assemblyVersion.Major),
                    Math.Max(0, assemblyVersion.Minor),
                    Math.Max(0, assemblyVersion.Build),
                    Math.Max(0, assemblyVersion.Revision));
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

        private void BuildThemeOptions(string selectedKey)
        {
            themeOptions.Children.Clear();

            var chipStyle = TryFindResource("ThemeChipStyle") as Style;
            var first = true;

            foreach (var theme in ThemeManager.Available)
            {
                var chip = new RadioButton
                {
                    Style = chipStyle,
                    GroupName = "LauncherTheme",
                    Content = theme.DisplayName,
                    Background = theme.PreviewBrush,
                    Tag = theme,
                    IsChecked = string.Equals(theme.Key, selectedKey, StringComparison.Ordinal),
                    Margin = new Thickness(0, first ? 0 : 8, 0, 0)
                };

                chip.Checked += ThemeChip_OnChecked;
                themeOptions.Children.Add(chip);
                first = false;
            }
        }

        private void ThemeChip_OnChecked(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings || _settings == null)
            {
                return;
            }

            ThemeManager.Apply(GetSelectedThemeKey());
            SaveSettings();

            if (!string.IsNullOrWhiteSpace(_rawNews))
            {
                RenderNews(_rawNews);
            }

            RedrawSparkline();
            RebuildLinks(_cachedServerConfig);
        }

        private string GetSelectedThemeKey()
        {
            var selected = themeOptions.Children
                .OfType<RadioButton>()
                .FirstOrDefault(chip => chip.IsChecked == true);

            var definition = selected != null ? selected.Tag as ThemeManager.ThemeDefinition : null;
            return definition != null ? definition.Key : ThemeManager.DefaultThemeKey;
        }

        private void LoadSettings()
        {
            _isLoadingSettings = true;
            try
            {
                _settings = LauncherSettings.Load();
                _settings.ConfigUrl = AppRuntimeConfig.ServerConfigUrl;

                txtConanExe.Text = _settings.ConanExePath ?? string.Empty;
                txtServerPassword.Password = _settings.GetServerPassword();
                chkAutoSubscribe.IsChecked = _settings.AutomaticallySubscribeToWorkshopMods;
                chkBattlEye.IsChecked = _settings.LaunchWithBattlEye;
                chkNotifyOnline.IsChecked = _settings.NotifyWhenServerOnline;
                chkDiscordStatus.IsChecked = _settings.ShowDiscordStatus;
                chkDeveloperMode.IsChecked = _settings.DeveloperMode;
                chkStartMinimized.IsChecked = _settings.StartMinimized;
                chkAutoStart.IsChecked = StartupManager.IsEnabled();

                var themeKey = ThemeManager.Normalize(_settings.UiTheme);
                BuildThemeOptions(themeKey);
                ThemeManager.Apply(themeKey);

                ApplyDeveloperMode();
                RestoreDevModList();
                UpdateSteamStatusLabel();
            }
            finally
            {
                _isLoadingSettings = false;
            }
        }

        private void SettingsControl_OnChanged(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings || _settings == null)
            {
                return;
            }

            SaveSettings();
        }

        private void SettingsControl_OnChanged(object sender, TextChangedEventArgs e)
        {
            if (_isLoadingSettings || _settings == null)
            {
                return;
            }

            SaveSettings();
            UpdateBranchBadge();
        }

        private void StartupOption_OnChanged(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings || _settings == null)
            {
                return;
            }

            string error;
            if (!StartupManager.TrySet(chkAutoStart.IsChecked == true, chkStartMinimized.IsChecked == true, out error))
            {
                AppendLog("Не удалось изменить автозапуск: " + error);
            }

            SaveSettings();
        }

        private void SaveSettings()
        {
            if (_settings == null)
            {
                return;
            }

            _settings.ConfigUrl = AppRuntimeConfig.ServerConfigUrl;
            _settings.ConanExePath = (txtConanExe.Text ?? string.Empty).Trim();
            _settings.SetServerPassword(txtServerPassword.Password);
            _settings.AutomaticallySubscribeToWorkshopMods = chkAutoSubscribe.IsChecked == true;
            _settings.LaunchWithBattlEye = chkBattlEye.IsChecked == true;
            _settings.NotifyWhenServerOnline = chkNotifyOnline.IsChecked == true;
            _settings.ShowDiscordStatus = chkDiscordStatus.IsChecked == true;
            _settings.DeveloperMode = chkDeveloperMode.IsChecked == true;
            _settings.StartMinimized = chkStartMinimized.IsChecked == true;
            _settings.UiTheme = GetSelectedThemeKey();

            string error;
            if (!_settings.TrySave(out error) && !_isClosing)
            {
                AppendLog("Не удалось сохранить настройки: " + error);
            }
        }

        private bool EnsureGameLocated(bool announce)
        {
            var current = (txtConanExe.Text ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(current) && File.Exists(current))
            {
                UpdateBranchBadge();
                return true;
            }

            var found = SteamLocator.FindConanInstall();
            if (found == null)
            {
                SettingsPage.txtGameHint.Text = "Не удалось найти Conan Exiles через Steam. Укажите путь вручную.";
                UpdateBranchBadge();
                return false;
            }

            txtConanExe.Text = found.LauncherExePath;
            SettingsPage.txtGameHint.Text = "Игра найдена автоматически через Steam.";
            AppendLog("Игра найдена: " + found.LauncherExePath);

            if (announce)
            {
                ShowInfo("Conan Exiles найден:\n" + found.LauncherExePath, "Поиск игры");
            }

            UpdateBranchBadge();
            WarnIfLegacyBranch(found);
            return true;
        }

        private void UpdateBranchBadge()
        {
            var path = (txtConanExe.Text ?? string.Empty).Trim();
            var info = SteamLocator.DescribeInstallFromExePath(path);

            if (info == null || string.IsNullOrWhiteSpace(info.BranchKey))
            {
                SettingsPage.branchBadge.Visibility = Visibility.Collapsed;
                return;
            }

            SettingsPage.branchBadge.Visibility = Visibility.Visible;

            if (info.IsLegacyBranch)
            {
                SettingsPage.txtBranchBadge.Text = info.BranchKey.ToUpperInvariant();
                SettingsPage.txtBranchBadge.Foreground = ThemeBrush("DangerBrush", "#E8776B");
                SettingsPage.txtGameHint.Text = "Внимание: выбрана бета-ветка Steam. Моды сервера рассчитаны на Enhanced.";
            }
            else
            {
                SettingsPage.txtBranchBadge.Text = "ENHANCED";
                SettingsPage.txtBranchBadge.Foreground = ThemeBrush("AccentBrush", "#D9903F");
            }
        }

        private void WarnIfLegacyBranch(ConanInstallInfo info)
        {
            if (info == null || !info.IsLegacyBranch)
            {
                return;
            }

            AppendLog("ВНИМАНИЕ: игра стоит на ветке '" + info.BranchKey + "', а не на Enhanced.");
            ShowWarning(
                "В Steam для Conan Exiles выбрана ветка \"" + info.BranchKey + "\".\n\n" +
                "Моды нашего сервера собраны под Enhanced и на этой ветке работать не будут.\n\n" +
                "Откройте свойства игры в Steam и переключите бета-версию на «Нет».",
                "Не та версия игры");
        }

        private void BtnDetectGame_OnClick(object sender, RoutedEventArgs e)
        {
            txtConanExe.Text = string.Empty;
            if (!EnsureGameLocated(true))
            {
                ShowWarning(
                    "Не удалось найти Conan Exiles через Steam.\n\n" +
                    "Убедитесь, что игра установлена, либо укажите путь вручную.",
                    "Поиск игры");
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
                WarnIfLegacyBranch(SteamLocator.DescribeInstallFromExePath(dialog.FileName));
            }
        }

        private void ShowMainPage()
        {
            MainPageGrid.Visibility = Visibility.Visible;
            SettingsPage.Visibility = Visibility.Collapsed;
            btnNavMain.Tag = "active";
            btnNavSettings.Tag = null;
        }

        private void ShowSettingsPage()
        {
            MainPageGrid.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Visible;
            btnNavMain.Tag = null;
            btnNavSettings.Tag = "active";
        }

        private void BtnNavMain_OnClick(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            ShowMainPage();
        }

        private void BtnNavSettings_OnClick(object sender, RoutedEventArgs e)
        {
            ShowSettingsPage();
        }

        private void SelectPanelTab(string tab)
        {
            var isNews = tab == "news";
            var isLog = tab == "log";
            var isMods = tab == "mods";

            rtbNewsBox.Visibility = isNews ? Visibility.Visible : Visibility.Collapsed;
            logPane.Visibility = isLog ? Visibility.Visible : Visibility.Collapsed;
            modsPane.Visibility = isMods ? Visibility.Visible : Visibility.Collapsed;

            btnTabNews.Tag = isNews ? "active" : null;
            btnTabLog.Tag = isLog ? "active" : null;
            btnTabMods.Tag = isMods ? "active" : null;

            btnRefreshNews.Visibility = isNews ? Visibility.Visible : Visibility.Collapsed;
            btnClearLog.Visibility = isLog ? Visibility.Visible : Visibility.Collapsed;
            btnCheckMods.Visibility = isMods ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowNewsTab()
        {
            SelectPanelTab("news");
        }

        private void ShowLogTab()
        {
            SelectPanelTab("log");
        }

        private void ShowModsTab()
        {
            SelectPanelTab("mods");
        }

        private void BtnTabNews_OnClick(object sender, RoutedEventArgs e)
        {
            ShowNewsTab();
        }

        private void BtnTabLog_OnClick(object sender, RoutedEventArgs e)
        {
            ShowLogTab();
        }

        private void BtnTabMods_OnClick(object sender, RoutedEventArgs e)
        {
            ShowModsTab();
        }

        private void BtnWebsite_OnClick(object sender, RoutedEventArgs e)
        {
            OpenExternalUrl(AppRuntimeConfig.WebsiteUrl, "WebsiteUrl");
        }

        private async void BtnCheckMods_OnClick(object sender, RoutedEventArgs e)
        {
            try
            {
                SetBusy(true);
                StartProgress("Проверка модов...");
                ShowModsTab();

                if (!EnsureGameLocated(false))
                {
                    ShowWarning("Не удалось найти Conan Exiles. Укажите путь к игре в настройках.");
                    SetStatus("Игра не найдена.");
                    return;
                }

                var gamePath = (txtConanExe.Text ?? string.Empty).Trim();

                if (_cts != null)
                {
                    _cts.Dispose();
                }
                _cts = new CancellationTokenSource();
                var token = _cts.Token;

                SetStatus("Загрузка списка модов сервера...");
                var config = await GetServerConfigAsync(true, token);
                RebuildLinks(config);
                AppendLog(string.Format("Модов в списке сервера: {0}", config.Mods.Count));

                await EnsureSteamClientReadyAsync();
                _launcherService.EnsureSteamworksInitialized(AppendLog);

                var analysis = await _launcherService.AnalyzeModsAsync(
                    gamePath,
                    config.Mods,
                    AppendLog,
                    (done, total) =>
                    {
                        var fraction = done / (double)Math.Max(1, total);
                        Dispatcher.BeginInvoke(new Action(() =>
                            SetProgress(fraction, string.Format("Проверка модов: {0}/{1}", done, total))));
                    },
                    token);

                PopulateModList(analysis);

                if (analysis.Updates.Count == 0)
                {
                    SetProgress(1.0, "Все моды актуальны.");
                    return;
                }

                SetProgress(1.0, string.Format("Требуют загрузки: {0} мод(ов), {1}.",
                    analysis.Updates.Count, FormatSize(analysis.TotalSizeBytes())));

                if (!ConfirmUpdates(analysis, gamePath))
                {
                    SetStatus("Загрузка отменена. Моды будут докачаны при запуске игры.");
                    return;
                }

                StartProgress("Загрузка модов...");
                _progressBandLow = 0d;
                _progressBandHigh = 1d;
                _acceptModProgress = true;
                try
                {
                    await _launcherService.SyncModsWithSteamworksAsync(
                        gamePath,
                        analysis.Updates,
                        chkAutoSubscribe.IsChecked == true,
                        AppendLog,
                        OnModSyncProgress,
                        token);
                }
                finally
                {
                    _acceptModProgress = false;
                }

                foreach (var mod in analysis.Updates)
                {
                    SetModStatus(mod.ModId, ModStatus.Done);
                }

                SetProgress(1.0, string.Format("Загружено модов: {0}. Можно запускать игру.", analysis.Updates.Count));
            }
            catch (OperationCanceledException)
            {
                SetStatus("Проверка отменена.");
                AppendLog("Проверка модов отменена.");
            }
            catch (Exception ex)
            {
                SetStatus("Ошибка.");
                AppendLog("ОШИБКА проверки модов: " + ex.Message);
                ShowError(ex.Message);
            }
            finally
            {
                SetBusy(false);
                lblTransferRate.Text = string.Empty;
            }
        }

        private void BtnCloseApp_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnMinimizeApp_OnClick(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void HeaderBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void BtnOpenDiscord_OnClick(object sender, RoutedEventArgs e)
        {
            OpenExternalUrl(AppRuntimeConfig.DiscordInviteUrl, "DiscordInviteUrl");
        }

        private void OpenExternalUrl(string url, string label)
        {
            try
            {
                var uri = UrlSecurity.RequireAllowedHttpsUrl(url, _allowedHosts, label);
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ShowError("Не удалось открыть ссылку:\n" + ex.Message);
            }
        }

        private void BtnClearLog_OnClick(object sender, RoutedEventArgs e)
        {
            ClearLog();
        }

        private void BtnCopyLog_OnClick(object sender, RoutedEventArgs e)
        {
            if (_logLines.Count == 0)
            {
                SetStatus("Журнал пуст — копировать нечего.");
                return;
            }

            try
            {
                Clipboard.SetText(string.Join(Environment.NewLine, _logLines));
                SetStatus("Журнал скопирован в буфер обмена.");
            }
            catch (Exception ex)
            {
                ShowError("Не удалось скопировать журнал:\n" + ex.Message);
            }
        }

        private void BtnOpenGameFolder_OnClick(object sender, RoutedEventArgs e)
        {
            var path = (txtConanExe.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                ShowWarning("Сначала укажите путь к игре в настройках.");
                return;
            }

            OpenFolder(Path.GetDirectoryName(path));
        }

        private void BtnOpenLogs_OnClick(object sender, RoutedEventArgs e)
        {
            var path = (txtConanExe.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                ShowWarning("Сначала укажите путь к игре в настройках.");
                return;
            }

            try
            {
                var logs = GameConfigService.GetLogsDirectory(path);
                if (!Directory.Exists(logs))
                {
                    ShowInfo("Папка логов появится после первого запуска игры:\n" + logs);
                    return;
                }

                OpenFolder(logs);
            }
            catch (Exception ex)
            {
                ShowError("Не удалось открыть логи:\n" + ex.Message);
            }
        }

        private void OpenFolder(string directory)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                {
                    ShowWarning("Папка не найдена:\n" + directory);
                    return;
                }

                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + directory + "\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ShowError("Не удалось открыть папку:\n" + ex.Message);
            }
        }

        private async void BtnRefreshNews_OnClick(object sender, RoutedEventArgs e)
        {
            btnRefreshNews.IsEnabled = false;
            try
            {
                await LoadNewsAsync();
            }
            finally
            {
                btnRefreshNews.IsEnabled = !_isBusy;
            }
        }

        private async Task LoadNewsAsync()
        {
            Exception lastError = null;

            foreach (var newsUrl in AppRuntimeConfig.NewsFeedUrls)
            {
                if (string.IsNullOrWhiteSpace(newsUrl))
                {
                    continue;
                }

                try
                {
                    var newsUri = UrlSecurity.RequireAllowedHttpsUrl(newsUrl, _allowedHosts, "NewsFeedUrl");
                    _rawNews = await _httpClient.GetStringAsync(newsUri);
                    RenderNews(_rawNews);
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }

            SetNewsPlainText("Не удалось загрузить новости.\n\n" +
                             (lastError != null ? lastError.Message : "URL новостей не задан."));
        }

        private void RenderNews(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                SetNewsPlainText("Лента новостей пуста.");
                return;
            }

            var text = raw.Trim();
            if (text.StartsWith("{") || text.StartsWith("["))
            {
                try
                {
                    var token = JToken.Parse(text);
                    var items = token as JArray ?? token["items"] as JArray;
                    if (items != null && items.Count > 0)
                    {
                        SetNewsItems(items);
                        return;
                    }

                    var directNewsToken = token["news"];
                    var directNews = directNewsToken != null ? directNewsToken.ToString() : null;
                    if (!string.IsNullOrWhiteSpace(directNews))
                    {
                        SetNewsPlainText(directNews);
                        return;
                    }
                }
                catch
                {
                }
            }

            SetNewsPlainText(text);
        }

        private void SetNewsItems(JArray items)
        {
            var doc = CreateNewsDocument();
            var titleBrush = ThemeBrush("TextPrimaryBrush", "#F6ECD9");
            var linkBrush = ThemeBrush("AccentBrush", "#D9903F");
            var bodyBrush = ThemeBrush("TextSecondaryBrush", "#C3B091");
            var cardBackground = ThemeBrush("SurfaceBrush", "#B31B150D");
            var cardBorder = ThemeBrush("StrokeBrush", "#3A2C1B");

            foreach (var item in items.OfType<JObject>())
            {
                var title = item.Value<string>("title");
                title = title != null ? title.Trim() : null;

                var body = item.Value<string>("body");
                body = body != null ? body.Trim() : null;
                if (string.IsNullOrWhiteSpace(body))
                {
                    var description = item.Value<string>("description");
                    var summary = item.Value<string>("summary");
                    body = description != null ? description.Trim() : (summary != null ? summary.Trim() : null);
                }

                var link = item.Value<string>("url");
                link = link != null ? link.Trim() : null;
                if (string.IsNullOrWhiteSpace(link))
                {
                    var alternate = item.Value<string>("link");
                    link = alternate != null ? alternate.Trim() : null;
                }

                string mdTitle, mdLink;
                if (TryExtractMarkdownLink(title, out mdTitle, out mdLink))
                {
                    title = mdTitle;
                    if (string.IsNullOrWhiteSpace(link))
                    {
                        link = mdLink;
                    }
                }

                if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(body))
                {
                    continue;
                }

                var panel = new StackPanel();

                if (!string.IsNullOrWhiteSpace(title))
                {
                    var titleBlock = new TextBlock
                    {
                        Foreground = titleBrush,
                        FontSize = 14.5,
                        FontWeight = FontWeights.Bold,
                        TextWrapping = TextWrapping.Wrap
                    };

                    Uri uri;
                    if (!string.IsNullOrWhiteSpace(link) && Uri.TryCreate(link, UriKind.Absolute, out uri))
                    {
                        var hyperlink = new Hyperlink
                        {
                            NavigateUri = uri,
                            TextDecorations = null,
                            Foreground = linkBrush,
                            Cursor = Cursors.Hand
                        };
                        AddTextWithEmojiInlines(hyperlink.Inlines, title, 15);
                        hyperlink.RequestNavigate += NewsHyperlink_RequestNavigate;
                        titleBlock.Inlines.Add(hyperlink);
                    }
                    else
                    {
                        AddTextWithEmojiInlines(titleBlock.Inlines, title, 15);
                    }

                    panel.Children.Add(titleBlock);
                }

                if (!string.IsNullOrWhiteSpace(body))
                {
                    var bodyBlock = new TextBlock
                    {
                        Margin = new Thickness(0, 7, 0, 0),
                        Foreground = bodyBrush,
                        FontSize = 13,
                        TextWrapping = TextWrapping.Wrap
                    };
                    AddTextWithEmojiInlines(bodyBlock.Inlines, body, 14);
                    panel.Children.Add(bodyBlock);
                }

                doc.Blocks.Add(new BlockUIContainer(new Border
                {
                    Background = cardBackground,
                    BorderBrush = cardBorder,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(14, 12, 14, 13),
                    Margin = new Thickness(0, 0, 0, 10),
                    Child = panel
                }));
            }

            if (!doc.Blocks.Any())
            {
                SetNewsPlainText("Лента новостей пуста.");
                return;
            }

            txtNews.Document = doc;
        }

        private void SetNewsPlainText(string text)
        {
            var doc = CreateNewsDocument();
            doc.Blocks.Add(new Paragraph(new Run(text ?? string.Empty))
            {
                Foreground = ThemeBrush("TextSecondaryBrush", "#C3B091"),
                FontSize = 13.2,
                Margin = new Thickness(0)
            });
            txtNews.Document = doc;
        }

        private FlowDocument CreateNewsDocument()
        {
            return new FlowDocument
            {
                Background = Brushes.Transparent,
                PagePadding = new Thickness(0),
                TextAlignment = TextAlignment.Left,
                FontFamily = new FontFamily("TT Norms Pro, Segoe UI Emoji"),
                LineHeight = 19
            };
        }

        private Brush ThemeBrush(string resourceKey, string fallbackHex)
        {
            var brush = TryFindResource(resourceKey) as Brush;
            return brush ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallbackHex));
        }

        private void NewsHyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            OpenExternalUrl(e.Uri.ToString(), "NewsLink");
        }

        private static bool TryExtractMarkdownLink(string text, out string title, out string url)
        {
            title = null;
            url = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var m = Regex.Match(text.Trim(), @"^\[(?<t>[^\]]+)\]\((?<u>https?://[^\)]+)\)$");
            if (!m.Success)
            {
                return false;
            }

            title = m.Groups["t"].Value.Trim();
            url = m.Groups["u"].Value.Trim();
            return true;
        }

        private void AddTextWithEmojiInlines(InlineCollection inlines, string text, double emojiSize)
        {
            if (inlines == null || string.IsNullOrEmpty(text))
            {
                return;
            }

            var buffer = new StringBuilder();
            var enumerator = StringInfo.GetTextElementEnumerator(text);
            while (enumerator.MoveNext())
            {
                var element = enumerator.GetTextElement();

                InlineUIContainer emojiInline;
                if (TryCreateEmojiInline(element, emojiSize, out emojiInline))
                {
                    if (buffer.Length > 0)
                    {
                        inlines.Add(new Run(buffer.ToString()));
                        buffer.Clear();
                    }

                    inlines.Add(emojiInline);
                    continue;
                }

                buffer.Append(element);
            }

            if (buffer.Length > 0)
            {
                inlines.Add(new Run(buffer.ToString()));
            }
        }

        private bool TryCreateEmojiInline(string element, double size, out InlineUIContainer inline)
        {
            inline = null;
            if (string.IsNullOrWhiteSpace(element))
            {
                return false;
            }

            string url;
            if (!EmojiIconUrls.TryGetValue(element, out url))
            {
                return false;
            }

            var source = GetEmojiImage(url);
            if (source == null)
            {
                return false;
            }

            inline = new InlineUIContainer(new Image
            {
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(0, -2, 0, -2),
                Source = source
            })
            {
                BaselineAlignment = BaselineAlignment.TextBottom
            };

            return true;
        }

        private BitmapImage GetEmojiImage(string url)
        {
            BitmapImage cached;
            if (EmojiImageCache.TryGetValue(url, out cached))
            {
                return cached;
            }

            BitmapImage image = null;
            try
            {
                var safeUri = UrlSecurity.RequireAllowedHttpsUrl(url, _allowedHosts, "EmojiIcon");

                image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelHeight = 36;
                image.UriSource = safeUri;
                image.EndInit();
            }
            catch
            {
                image = null;
            }

            EmojiImageCache[url] = image;
            return image;
        }

        private async void BtnPlay_OnClick(object sender, RoutedEventArgs e)
        {
            try
            {
                SetBusy(true);
                ClearLog();
                AppendLog("Старт REALM RolePlay Launcher...");
                StartProgress("Инициализация...");
                SaveSettings();
                ShowMainPage();

                if (!EnsureGameLocated(false))
                {
                    ShowWarning("Не удалось найти Conan Exiles. Укажите путь к игре в настройках.");
                    SetStatus("Игра не найдена.");
                    return;
                }

                var gamePath = (txtConanExe.Text ?? string.Empty).Trim();
                WarnIfLegacyBranch(SteamLocator.DescribeInstallFromExePath(gamePath));

                if (_cts != null)
                {
                    _cts.Dispose();
                }
                _cts = new CancellationTokenSource();
                var token = _cts.Token;

                SetStatus("Загрузка конфига сервера...");
                var config = await GetServerConfigAsync(true, token);
                SetProgress(StageConfigLoaded, "Конфиг сервера загружен.");
                AppendLog(string.Format("Сервер: {0} ({1})", config.Name, config.Ip));
                AppendLog(string.Format("Модов в списке: {0}", config.Mods.Count));
                RebuildLinks(config);

                if (!ValidateServerPassword(config))
                {
                    SetStatus("Неверный пароль сервера.");
                    return;
                }
                SetProgress(StagePasswordValidated, "Пароль сервера проверен.");

                await EnsureSteamClientReadyAsync();
                _launcherService.EnsureSteamworksInitialized(AppendLog);
                SetProgress(StageSteamReady, "Steam готов.");

                SetStatus("Проверка модов...");
                ShowModsTab();
                var analysis = await _launcherService.AnalyzeModsAsync(
                    gamePath,
                    config.Mods,
                    AppendLog,
                    (done, total) =>
                    {
                        var fraction = StageAnalysisStart +
                                       ((StageAnalysisDone - StageAnalysisStart) * (done / (double)Math.Max(1, total)));
                        Dispatcher.BeginInvoke(new Action(() =>
                            SetProgress(fraction, string.Format("Проверка модов: {0}/{1}", done, total))));
                    },
                    token);

                PopulateModList(analysis);
                SetProgress(StageAnalysisDone, "Проверка модов завершена.");

                if (analysis.Updates.Count > 0)
                {
                    if (!ConfirmUpdates(analysis, gamePath))
                    {
                        SetStatus("Обновление модов отменено.");
                        return;
                    }

                    SetProgress(StageModsStart, "Синхронизация модов...");
                    _progressBandLow = StageModsStart;
                    _progressBandHigh = StageModsEnd;
                    _acceptModProgress = true;
                    try
                    {
                        await _launcherService.SyncModsWithSteamworksAsync(
                            gamePath,
                            analysis.Updates,
                            chkAutoSubscribe.IsChecked == true,
                            AppendLog,
                            OnModSyncProgress,
                            token);
                    }
                    finally
                    {
                        _acceptModProgress = false;
                    }

                    foreach (var mod in analysis.Updates)
                    {
                        SetModStatus(mod.ModId, ModStatus.Done);
                    }

                    SetProgress(StageModsEnd, "Моды синхронизированы.");
                }
                else
                {
                    AppendLog("Все моды актуальны.");
                    SetProgress(StageModsEnd, "Обновление модов не требуется.");
                }

                var modListSnapshot = _launcherService.CaptureModListSnapshot(gamePath);
                var modListWasReplaced = false;
                try
                {
                    SetStatus("Обновление modlist.txt...");
                    var modListPath = _launcherService.WriteModListFile(gamePath, config.Mods, AppendLog);
                    modListWasReplaced = true;
                    AppendLog("modlist.txt обновлён: " + modListPath);
                    SetProgress(StageModlistDone, "modlist.txt обновлён.");

                    SetStatus("Запуск игры и подключение к серверу...");
                    _launcherService.LaunchServerConnection(
                        gamePath, config.Name, config.Ip, config.GamePassword,
                        chkBattlEye.IsChecked == true, AppendLog);
                    AppendLog("Игра запущена с автоподключением к " + config.Ip);
                    SetProgress(StageLaunched, "Готово. Игра запускается.");
                    ShowPlayingPresence();
                }
                finally
                {
                    if (modListWasReplaced)
                    {
                        AppendLog("Ожидание запуска игры перед восстановлением modlist.txt...");
                        var appeared = await _launcherService.WaitForGameProcessAsync(
                            TimeSpan.FromSeconds(90), CancellationToken.None);
                        AppendLog(appeared ? "Игра запущена." : "Процесс игры не обнаружен.");

                        await Task.Delay(appeared ? 6000 : 1000);

                        try
                        {
                            _launcherService.RestoreModListSnapshot(gamePath, modListSnapshot, AppendLog);
                        }
                        catch (Exception restoreEx)
                        {
                            AppendLog("ПРЕДУПРЕЖДЕНИЕ: не удалось восстановить modlist.txt: " + restoreEx.Message);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                SetStatus("Загрузка отменена.");
                AppendLog("Операция отменена пользователем.");
            }
            catch (Exception ex)
            {
                SetStatus("Ошибка.");
                AppendLog("ОШИБКА: " + ex.Message);
                ShowError(ex.Message);
            }
            finally
            {
                SetBusy(false);
                lblTransferRate.Text = string.Empty;
            }
        }

        private void BtnCancelSync_OnClick(object sender, RoutedEventArgs e)
        {
            if (_cts == null)
            {
                return;
            }

            try
            {
                _cts.Cancel();
                SetStatus("Отмена...");
                AppendLog("Запрошена отмена загрузки.");
                btnCancelSync.IsEnabled = false;
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void SetBusy(bool busy)
        {
            _isBusy = busy;

            txtConanExe.IsEnabled = !busy;
            txtServerPassword.IsEnabled = !busy;
            chkAutoSubscribe.IsEnabled = !busy;
            chkBattlEye.IsEnabled = !busy;
            chkNotifyOnline.IsEnabled = !busy;
            chkAutoStart.IsEnabled = !busy;
            chkStartMinimized.IsEnabled = !busy;
            themeOptions.IsEnabled = !busy;
            btnCheckUpdates.IsEnabled = !busy;
            btnCheckSteamCmd.IsEnabled = !busy;
            btnBrowseConanExe.IsEnabled = !busy;
            btnDetectGame.IsEnabled = !busy;
            btnPlay.IsEnabled = !busy;
            btnDiscord.IsEnabled = !busy;
            btnRefreshNews.IsEnabled = !busy;
            btnClearLog.IsEnabled = !busy;
            btnCheckMods.IsEnabled = !busy;
            btnNavMain.IsEnabled = !busy;
            btnNavSettings.IsEnabled = !busy;

            btnCancelSync.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            btnCancelSync.IsEnabled = busy;

            var spinner = busy ? Visibility.Visible : Visibility.Collapsed;
            spinnerFooter.Visibility = spinner;
            spinnerPanel.Visibility = spinner;
        }

        private void PopulateModList(ModUpdateAnalysis analysis)
        {
            _mods.Clear();
            foreach (var mod in analysis.All)
            {
                _mods.Add(mod);
            }

            txtModsPlaceholder.Visibility = _mods.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            btnTabMods.Content = _mods.Count > 0 ? "Моды " + _mods.Count : "Моды";
        }

        private void SetModStatus(string modId, string status)
        {
            var mod = _mods.FirstOrDefault(m => string.Equals(m.ModId, modId, StringComparison.Ordinal));
            if (mod != null)
            {
                mod.Status = status;
            }
        }

        private async void ModRow_OnButtonClick(object sender, RoutedEventArgs e)
        {
            var button = e.OriginalSource as Button;
            var action = button != null ? button.Tag as string : null;
            if (action != "reinstall" && action != "remove")
            {
                return;
            }

            var mod = button.DataContext as ModUpdateInfo;
            if (mod == null || _isBusy)
            {
                return;
            }

            if (action == "remove")
            {
                await RemoveModAsync(mod);
                return;
            }

            if (!AskYesNo("Переустановить мод «" + mod.DisplayName + "»?\n\nSteam скачает его заново.", "Переустановка мода"))
            {
                return;
            }

            var gamePath = (txtConanExe.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(gamePath) || !File.Exists(gamePath))
            {
                ShowWarning("Сначала укажите путь к игре в настройках.");
                return;
            }

            var previousStatus = mod.Status;
            try
            {
                SetBusy(true);
                StartProgress("Переустановка мода...");

                if (_cts != null)
                {
                    _cts.Dispose();
                }
                _cts = new CancellationTokenSource();

                await EnsureSteamClientReadyAsync();
                mod.Status = ModStatus.Downloading;

                _progressBandLow = 0d;
                _progressBandHigh = 1d;
                _acceptModProgress = true;
                try
                {
                    await _launcherService.ReinstallModAsync(gamePath, mod, AppendLog, OnModSyncProgress, _cts.Token);
                }
                finally
                {
                    _acceptModProgress = false;
                }

                mod.Status = ModStatus.Done;
                SetProgress(1.0, "Мод переустановлен.");
            }
            catch (OperationCanceledException)
            {
                mod.Status = previousStatus;
                SetStatus("Переустановка отменена.");
            }
            catch (Exception ex)
            {
                mod.Status = ModStatus.Failed;
                AppendLog("ОШИБКА переустановки: " + ex.Message);
                ShowError(ex.Message);
            }
            finally
            {
                _acceptModProgress = false;
                SetBusy(false);
                lblTransferRate.Text = string.Empty;
            }
        }

        private CheckBox chkDeveloperMode { get { return SettingsPage.chkDeveloperMode; } }

        private void RestoreDevModList()
        {
            if (_settings == null || _settings.DevModList == null || _settings.DevModList.Count == 0)
            {
                return;
            }

            _mods.Clear();
            foreach (var entry in _settings.DevModList)
            {
                var parts = (entry ?? string.Empty).Split(new[] { '/' }, 2);
                if (parts.Length != 2)
                {
                    continue;
                }

                _mods.Add(new ModUpdateInfo
                {
                    ModId = parts[0],
                    PakName = parts[1],
                    Status = ModStatus.UpToDate
                });
            }

            if (_mods.Count > 0)
            {
                txtModsPlaceholder.Visibility = Visibility.Collapsed;
                btnTabMods.Content = "Моды " + _mods.Count;
            }
        }

        private void ApplyDeveloperMode()
        {
            var on = chkDeveloperMode.IsChecked == true;
            devPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            lstMods.AllowDrop = on;

            lstMods.Tag = on ? "dev" : null;
        }

        private void DeveloperMode_OnChanged(object sender, RoutedEventArgs e)
        {
            ApplyDeveloperMode();

            if (_isLoadingSettings || _settings == null)
            {
                return;
            }

            SaveSettings();
        }

        private void SaveDevModList(bool writeModList)
        {
            if (_settings == null)
            {
                return;
            }

            _settings.DevModList = ModListService.ToEntries(_mods);
            SaveSettings();

            if (!writeModList)
            {
                return;
            }

            var gamePath = (txtConanExe.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(gamePath) || !File.Exists(gamePath))
            {
                return;
            }

            try
            {
                ModListService.WriteModList(gamePath, _settings.DevModList, AppendLog);
            }
            catch (Exception ex)
            {
                AppendLog("Не удалось записать modlist.txt: " + ex.Message);
            }
        }

        private void MoveSelectedMod(int offset)
        {
            var index = lstMods.SelectedIndex;
            var target = index + offset;
            if (index < 0 || target < 0 || target >= _mods.Count)
            {
                return;
            }

            _mods.Move(index, target);
            lstMods.SelectedIndex = target;
            lstMods.ScrollIntoView(_mods[target]);
            SaveDevModList(true);
        }

        private void BtnDevUp_OnClick(object sender, RoutedEventArgs e)
        {
            MoveSelectedMod(-1);
        }

        private void BtnDevDown_OnClick(object sender, RoutedEventArgs e)
        {
            MoveSelectedMod(1);
        }

        private async Task RemoveModAsync(ModUpdateInfo mod)
        {
            var choice = RealmDialog.ShowChoice(
                this,
                "Удаление мода",
                "Мод «" + mod.DisplayName + "»\n\n" +
                "Убрать только из списка модов — файлы останутся на диске и подписка сохранится.\n\n" +
                "Отписаться и удалить — мод пропадёт из Workshop-подписок, файлы будут удалены.",
                "Убрать из списка",
                "Отписаться и удалить");

            if (choice == MessageBoxResult.Cancel || choice == MessageBoxResult.None)
            {
                return;
            }

            var unsubscribe = choice == MessageBoxResult.No;

            if (unsubscribe)
            {
                var gamePath = (txtConanExe.Text ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(gamePath) || !File.Exists(gamePath))
                {
                    ShowWarning("Сначала укажите путь к игре в настройках.");
                    return;
                }

                try
                {
                    SetBusy(true);
                    StartProgress("Отписка от мода " + mod.DisplayName + "...");

                    if (_cts != null)
                    {
                        _cts.Dispose();
                    }
                    _cts = new CancellationTokenSource();

                    await EnsureSteamClientReadyAsync();
                    _launcherService.EnsureSteamworksInitialized(AppendLog);
                    await _launcherService.UnsubscribeModAsync(gamePath, mod.ModId, AppendLog, _cts.Token);

                    SetProgress(1.0, "Мод удалён: " + mod.DisplayName);
                }
                catch (Exception ex)
                {
                    AppendLog("ОШИБКА отписки: " + ex.Message);
                    ShowError(ex.Message);
                    return;
                }
                finally
                {
                    SetBusy(false);
                }
            }
            else
            {
                AppendLog("Мод убран из списка: " + mod.DisplayName);
                SetStatus("Мод убран из списка: " + mod.DisplayName);
            }

            _mods.Remove(mod);
            btnTabMods.Content = _mods.Count > 0 ? "Моды " + _mods.Count : "Моды";
            txtModsPlaceholder.Visibility = _mods.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            SaveDevModList(true);
        }

        private async void BtnDevAddMod_OnClick(object sender, RoutedEventArgs e)
        {
            var modId = ModListService.ParseModId(txtDevModId.Text);
            if (string.IsNullOrWhiteSpace(modId))
            {
                ShowWarning("Укажите ID мода или ссылку на страницу Workshop.");
                return;
            }

            if (_mods.Any(m => string.Equals(m.ModId, modId, StringComparison.Ordinal)))
            {
                ShowInfo("Этот мод уже есть в списке.");
                return;
            }

            var gamePath = (txtConanExe.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(gamePath) || !File.Exists(gamePath))
            {
                ShowWarning("Сначала укажите путь к игре в настройках.");
                return;
            }

            try
            {
                SetBusy(true);
                StartProgress("Добавление мода " + modId + "...");
                ShowModsTab();

                if (_cts != null)
                {
                    _cts.Dispose();
                }
                _cts = new CancellationTokenSource();

                await EnsureSteamClientReadyAsync();
                _launcherService.EnsureSteamworksInitialized(AppendLog);

                _progressBandLow = 0d;
                _progressBandHigh = 1d;
                _acceptModProgress = true;
                ModUpdateInfo added;
                try
                {
                    added = await _launcherService.AddModByIdAsync(
                        gamePath, modId, AppendLog, OnModSyncProgress, _cts.Token);
                }
                finally
                {
                    _acceptModProgress = false;
                }

                _mods.Add(added);
                btnTabMods.Content = "Моды " + _mods.Count;
                txtModsPlaceholder.Visibility = Visibility.Collapsed;
                lstMods.SelectedItem = added;
                lstMods.ScrollIntoView(added);
                txtDevModId.Clear();

                SaveDevModList(true);
                SetProgress(1.0, "Мод добавлен: " + added.DisplayName);
            }
            catch (OperationCanceledException)
            {
                SetStatus("Добавление отменено.");
            }
            catch (Exception ex)
            {
                AppendLog("ОШИБКА добавления мода: " + ex.Message);
                ShowError(ex.Message);
            }
            finally
            {
                SetBusy(false);
                lblTransferRate.Text = string.Empty;
            }
        }

        private void BtnDevExport_OnClick(object sender, RoutedEventArgs e)
        {
            if (_mods.Count == 0)
            {
                ShowWarning("Список модов пуст.");
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Экспорт списка модов",
                Filter = "Список модов REALM (*.json)|*.json|Все файлы (*.*)|*.*",
                FileName = "realm-modlist.json"
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                ModListService.ExportToFile(dialog.FileName, "REALM RolePlay", ModListService.ToEntries(_mods));
                AppendLog("Список модов экспортирован: " + dialog.FileName);
                ShowInfo(
                    "Список из " + _mods.Count + " мод(ов) сохранён.\n\n" +
                    "Файл можно передать другому человеку — при импорте пути к модам подставятся под его установку.",
                    "Экспорт списка модов");
            }
            catch (Exception ex)
            {
                ShowError("Не удалось сохранить файл:\n" + ex.Message);
            }
        }

        private void BtnDevImport_OnClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Импорт списка модов",
                Filter = "Список модов (*.json;*.txt)|*.json;*.txt|Все файлы (*.*)|*.*"
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                var entries = ModListService.ImportFromFile(dialog.FileName);
                if (entries.Count == 0)
                {
                    ShowWarning("В файле не нашлось ни одного мода.");
                    return;
                }

                var gamePath = (txtConanExe.Text ?? string.Empty).Trim();
                var workshopRoot = string.IsNullOrWhiteSpace(gamePath) || !File.Exists(gamePath)
                    ? null
                    : LauncherService.ResolveWorkshopContentRoot(gamePath);

                _mods.Clear();
                var missing = 0;

                foreach (var entry in entries)
                {
                    var parts = entry.Split(new[] { '/' }, 2);
                    var present = workshopRoot != null &&
                                  File.Exists(Path.Combine(workshopRoot, parts[0], parts[1]));
                    if (!present)
                    {
                        missing++;
                    }

                    _mods.Add(new ModUpdateInfo
                    {
                        ModId = parts[0],
                        PakName = parts[1],
                        Status = present ? ModStatus.UpToDate : ModStatus.Missing
                    });
                }

                btnTabMods.Content = "Моды " + _mods.Count;
                txtModsPlaceholder.Visibility = Visibility.Collapsed;
                ShowModsTab();
                SaveDevModList(true);

                AppendLog(string.Format("Импортирован список: {0} мод(ов), отсутствует локально: {1}", entries.Count, missing));
                ShowInfo(
                    string.Format("Загружено модов: {0}\nНет на этом компьютере: {1}\n\n", entries.Count, missing) +
                    (missing > 0
                        ? "Нажмите «Проверить моды», чтобы докачать недостающие."
                        : "Все моды уже установлены, порядок применён к modlist.txt."),
                    "Импорт списка модов");
            }
            catch (Exception ex)
            {
                ShowError("Не удалось импортировать файл:\n" + ex.Message);
            }
        }

        private void BtnDevServerIds_OnClick(object sender, RoutedEventArgs e)
        {
            var ids = ModListService.BuildServerModIds(ModListService.ToEntries(_mods));
            if (string.IsNullOrWhiteSpace(ids))
            {
                ShowWarning("Список модов пуст.");
                return;
            }

            try
            {
                Clipboard.SetText(ids);
                AppendLog("Список id для сервера скопирован (" + _mods.Count + " мод(ов)).");
                ShowInfo(
                    "Список скопирован в буфер обмена — вставьте его в Dedicated Server Launcher.\n\n" + ids,
                    "Список модов для сервера");
            }
            catch (Exception ex)
            {
                ShowError("Не удалось скопировать в буфер обмена:\n" + ex.Message);
            }
        }

        private void BtnDevOpenModList_OnClick(object sender, RoutedEventArgs e)
        {
            var gamePath = (txtConanExe.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(gamePath) || !File.Exists(gamePath))
            {
                ShowWarning("Сначала укажите путь к игре в настройках.");
                return;
            }

            try
            {
                var modsDirectory = Path.Combine(GameConfigService.ResolveSandboxDirectory(gamePath), "Mods");
                var modListPath = Path.Combine(modsDirectory, "modlist.txt");

                if (File.Exists(modListPath))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + modListPath + "\"")
                    {
                        UseShellExecute = true
                    });
                    return;
                }

                if (!Directory.Exists(modsDirectory))
                {
                    ShowInfo("Папка модов появится после первого запуска игры:\n" + modsDirectory);
                    return;
                }

                OpenFolder(modsDirectory);
            }
            catch (Exception ex)
            {
                ShowError("Не удалось открыть папку modlist.txt:\n" + ex.Message);
            }
        }

        private void BtnDevLocalPlay_OnClick(object sender, RoutedEventArgs e)
        {
            var gamePath = (txtConanExe.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(gamePath) || !File.Exists(gamePath))
            {
                ShowWarning("Сначала укажите путь к игре в настройках.");
                return;
            }

            if (_mods.Count == 0)
            {
                ShowWarning("Список модов пуст — нечего проверять.");
                return;
            }

            try
            {
                ModListService.WriteModList(gamePath, ModListService.ToEntries(_mods), AppendLog);
                _launcherService.LaunchLocalGame(gamePath, chkBattlEye.IsChecked == true, AppendLog);
                SetStatus("Игра запущена локально, без подключения к серверу.");
            }
            catch (Exception ex)
            {
                AppendLog("ОШИБКА локального запуска: " + ex.Message);
                ShowError(ex.Message);
            }
        }

        private Point _dragStart;
        private ModUpdateInfo _dragItem;

        private void LstMods_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStart = e.GetPosition(null);
            _dragItem = ItemUnderMouse(e.OriginalSource as DependencyObject);
        }

        private void LstMods_OnPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _dragItem == null || chkDeveloperMode.IsChecked != true)
            {
                return;
            }

            var moved = e.GetPosition(null) - _dragStart;
            if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            var carried = _dragItem;
            var startIndex = _mods.IndexOf(carried);

            carried.IsDragging = true;
            lstMods.SelectedItem = carried;

            try
            {
                var result = DragDrop.DoDragDrop(lstMods, carried, DragDropEffects.Move);

                if (result == DragDropEffects.Move && _mods.IndexOf(carried) != startIndex)
                {
                    SaveDevModList(true);
                }
            }
            finally
            {
                carried.IsDragging = false;
                _dragItem = null;
            }
        }

        private void LstMods_OnDragOver(object sender, DragEventArgs e)
        {
            if (chkDeveloperMode.IsChecked != true || !e.Data.GetDataPresent(typeof(ModUpdateInfo)))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            e.Effects = DragDropEffects.Move;
            e.Handled = true;

            var dragged = e.Data.GetData(typeof(ModUpdateInfo)) as ModUpdateInfo;
            var target = ItemUnderMouse(e.OriginalSource as DependencyObject);
            if (dragged == null || target == null || ReferenceEquals(dragged, target))
            {
                return;
            }

            var from = _mods.IndexOf(dragged);
            var to = _mods.IndexOf(target);
            if (from < 0 || to < 0 || from == to)
            {
                return;
            }

            _mods.Move(from, to);
            lstMods.SelectedItem = dragged;
        }

        private void LstMods_OnDrop(object sender, DragEventArgs e)
        {
            var dragged = e.Data.GetData(typeof(ModUpdateInfo)) as ModUpdateInfo;
            if (dragged != null)
            {
                dragged.IsDragging = false;
                lstMods.SelectedItem = dragged;
            }

            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private static ModUpdateInfo ItemUnderMouse(DependencyObject source)
        {
            while (source != null && !(source is ListBoxItem))
            {
                source = VisualTreeHelper.GetParent(source);
            }

            var container = source as ListBoxItem;
            return container != null ? container.DataContext as ModUpdateInfo : null;
        }

        private void OnModSyncProgress(ModSyncProgress progress)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_isClosing || progress == null || !_acceptModProgress)
                {
                    return;
                }

                var fraction = Math.Max(0d, Math.Min(1d, progress.OverallFraction));
                var overall = _progressBandLow + ((_progressBandHigh - _progressBandLow) * fraction);

                var sizeText = progress.BytesTotal > 0
                    ? string.Format(" — {0} / {1}", FormatSize(progress.BytesDone), FormatSize(progress.BytesTotal))
                    : string.Empty;

                SetProgress(overall, string.Format("Загрузка модов {0}/{1}: {2}{3}",
                    Math.Min(progress.CompletedMods + 1, progress.TotalMods),
                    progress.TotalMods,
                    progress.CurrentModName,
                    sizeText));

                lblTransferRate.Text = FormatRate(progress.BytesPerSecond, progress.Eta);

                var current = _mods.FirstOrDefault(m =>
                    string.Equals(m.PakName, progress.CurrentModName, StringComparison.Ordinal));
                if (current != null && !string.Equals(current.Status, ModStatus.Downloading, StringComparison.Ordinal))
                {
                    current.Status = ModStatus.Downloading;
                }
            }));
        }

        private static string FormatRate(double bytesPerSecond, TimeSpan? eta)
        {
            if (bytesPerSecond < 1024)
            {
                return string.Empty;
            }

            var text = string.Format(CultureInfo.InvariantCulture, "{0:0.0} МБ/с", bytesPerSecond / 1024d / 1024d);

            if (eta.HasValue && eta.Value.TotalSeconds > 1 && eta.Value.TotalHours < 12)
            {
                text += eta.Value.TotalMinutes >= 1
                    ? string.Format(" · осталось {0:0} мин", Math.Ceiling(eta.Value.TotalMinutes))
                    : string.Format(" · осталось {0:0} с", eta.Value.TotalSeconds);
            }

            return text;
        }

        private bool ConfirmUpdates(ModUpdateAnalysis analysis, string gamePath)
        {
            var totalBytes = analysis.TotalSizeBytes();
            var lines = analysis.Updates
                .Take(20)
                .Select(x => string.Format("• [{0}] {1} ({2})",
                    x.Status,
                    x.DisplayName,
                    x.SizeBytes > 0 ? x.SizeText : "размер неизвестен"))
                .ToList();

            if (analysis.Updates.Count > 20)
            {
                lines.Add(string.Format("• ... и ещё {0} мод(ов)", analysis.Updates.Count - 20));
            }

            var spaceWarning = string.Empty;
            try
            {
                var free = SteamLocator.GetFreeSpaceBytes(LauncherService.ResolveWorkshopContentRoot(gamePath));
                if (free >= 0)
                {
                    spaceWarning = string.Format("\nСвободно на диске: {0}", FormatSize(free));
                    if (totalBytes > 0 && free < totalBytes)
                    {
                        spaceWarning += string.Format("\n\nВНИМАНИЕ: не хватает примерно {0}.", FormatSize(totalBytes - free));
                    }
                }
            }
            catch
            {
            }

            var message =
                string.Format("Нужно скачать модов: {0}\n", analysis.Updates.Count) +
                string.Format("Примерный объём: {0}{1}\n\n", FormatSize(totalBytes), spaceWarning) +
                string.Join("\n", lines) +
                "\n\nПродолжить?";

            return AskYesNo(message, "Обновление модов");
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

        private static void TryStartSteamClient()
        {
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
        }

        private async Task EnsureSteamClientReadyAsync()
        {
            if (IsSteamClientRunning())
            {
                UpdateSteamStatusLabel();
                return;
            }

            AppendLog("Steam не запущен. Выполняется запуск Steam...");
            TryStartSteamClient();

            for (var i = 0; i < 20; i++)
            {
                if (IsSteamClientRunning())
                {
                    UpdateSteamStatusLabel();
                    return;
                }
                await Task.Delay(500);
            }

            throw new InvalidOperationException("Steam не запущен. Запустите клиент Steam и повторите.");
        }

        private async void BtnCheckSteamCmd_OnClick(object sender, RoutedEventArgs e)
        {
            try
            {
                SetBusy(true);
                btnCancelSync.Visibility = Visibility.Collapsed;

                if (IsSteamClientRunning())
                {
                    UpdateSteamStatusLabel();
                    AppendLog("Steam уже запущен.");
                    ShowInfo("Steam уже запущен и готов к загрузке модов.");
                    return;
                }

                TryStartSteamClient();

                for (var i = 0; i < 10; i++)
                {
                    if (IsSteamClientRunning())
                    {
                        break;
                    }
                    await Task.Delay(500);
                }

                UpdateSteamStatusLabel();
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
                SetBusy(false);
            }
        }

        private void UpdateSteamStatusLabel()
        {
            var running = IsSteamClientRunning();
            lblSteamCmdStatus.Text = running ? "запущен" : "не запущен";
            lblSteamCmdStatus.Foreground = running
                ? ThemeBrush("SuccessBrush", "#7FC98A")
                : ThemeBrush("TextMutedBrush", "#8C7A5D");
        }

        private async Task<ServerConfig> GetServerConfigAsync(bool forceRefresh, CancellationToken cancellationToken)
        {
            if (!forceRefresh &&
                _cachedServerConfig != null &&
                DateTime.UtcNow - _cachedServerConfigUtc < ServerConfigCacheTtl)
            {
                return _cachedServerConfig;
            }

            var config = await _launcherService.DownloadConfigAsync(
                AppRuntimeConfig.ServerConfigUrls, _allowedHosts, AppendLog, cancellationToken);

            _cachedServerConfig = config;
            _cachedServerConfigUtc = DateTime.UtcNow;
            return config;
        }

        private async Task RefreshServerStatusAsync()
        {
            if (_isRefreshingServerStatus || _isClosing)
            {
                return;
            }

            _isRefreshingServerStatus = true;
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
                {
                    var config = await GetServerConfigAsync(false, cts.Token);
                    RebuildLinks(config);

                    var host = ExtractHost(config.Ip);
                    var queryPort = config.QueryPort ?? AppRuntimeConfig.DefaultQueryPort;
                    var serverInfo = await _launcherService.QueryServerInfoAsync(host, queryPort, cts.Token);

                    if (_isClosing)
                    {
                        return;
                    }

                    if (serverInfo.IsOnline)
                    {
                        _lastServerOnline = true;
                        _lastPlayers = serverInfo.Players;
                        _lastMaxPlayers = serverInfo.MaxPlayers;

                        SetServerStatusUi("Онлайн",
                            string.Format("{0}/{1}", serverInfo.Players, serverInfo.MaxPlayers),
                            ThemeBrush("SuccessBrush", "#7FC98A"));

                        _onlineHistory.Add(serverInfo.Players);
                        RedrawSparkline();

                        if (_serverWasOffline)
                        {
                            _serverWasOffline = false;
                            if (chkNotifyOnline.IsChecked == true)
                            {
                                NotifyServerOnline(config.Name, serverInfo.Players, serverInfo.MaxPlayers);
                            }
                        }
                    }
                    else
                    {
                        _serverWasOffline = true;
                        _lastServerOnline = false;
                        SetServerStatusUi("Офлайн", "0/0", ThemeBrush("DangerBrush", "#E8776B"));
                    }
                }
            }
            catch
            {
                if (!_isClosing)
                {
                    _serverWasOffline = true;
                    _lastServerOnline = false;
                    SetServerStatusUi("Недоступен", "--/--", ThemeBrush("DangerBrush", "#E8776B"));
                }
            }
            finally
            {
                if (!_isClosing)
                {
                    UpdateSteamStatusLabel();

                    RefreshDiscordPresence();
                }
                _isRefreshingServerStatus = false;
            }
        }

        private void SetServerStatusUi(string status, string players, Brush accent)
        {
            lblServerStatusText.Text = status;
            lblPlayersCount.Text = players;
            serverStatusDot.Fill = accent;
        }

        private void NotifyServerOnline(string serverName, int players, int maxPlayers)
        {
            AppendLog("Сервер снова в сети.");

            try
            {
                if (_trayIcon == null)
                {
                    var exePath = Process.GetCurrentProcess().MainModule.FileName;
                    _trayIcon = new System.Windows.Forms.NotifyIcon
                    {
                        Icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath),
                        Text = "REALM RolePlay Launcher"
                    };
                }

                _trayIcon.Visible = true;
                _trayIcon.BalloonTipTitle = "Сервер снова в сети";
                _trayIcon.BalloonTipText = string.Format("{0} — игроков: {1}/{2}",
                    string.IsNullOrWhiteSpace(serverName) ? "REALM RolePlay" : serverName, players, maxPlayers);
                _trayIcon.ShowBalloonTip(8000);
            }
            catch
            {
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

        private void RedrawSparkline()
        {
            sparklineCanvas.Children.Clear();

            var samples = _onlineHistory.Samples;
            var width = sparklineHost.ActualWidth;
            var height = sparklineHost.ActualHeight;

            if (samples.Count < 3 || width < 20 || height < 10)
            {
                lblSparklineHint.Visibility = Visibility.Visible;
                return;
            }

            lblSparklineHint.Visibility = Visibility.Collapsed;

            var oldest = samples[0].TimeUtc;
            var span = (DateTime.UtcNow - oldest).TotalSeconds;
            if (span < 1)
            {
                return;
            }

            var peak = Math.Max(1, _onlineHistory.PeakPlayers());
            var points = new PointCollection();

            foreach (var sample in samples)
            {
                var x = ((sample.TimeUtc - oldest).TotalSeconds / span) * width;
                var y = height - 2 - ((sample.Players / (double)peak) * (height - 6));
                points.Add(new Point(x, y));
            }

            var accent = ThemeBrush("AccentBrush", "#D9903F");

            var fillPoints = new PointCollection(points) { new Point(width, height), new Point(0, height) };
            sparklineCanvas.Children.Add(new Polygon
            {
                Points = fillPoints,
                Fill = ThemeBrush("AccentSoftBrush", "#3355391E"),
                IsHitTestVisible = false
            });

            sparklineCanvas.Children.Add(new Polyline
            {
                Points = points,
                Stroke = accent,
                StrokeThickness = 1.6,
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false
            });

            var last = points[points.Count - 1];
            var dot = new Ellipse { Width = 5, Height = 5, Fill = accent };
            Canvas.SetLeft(dot, last.X - 2.5);
            Canvas.SetTop(dot, last.Y - 2.5);
            sparklineCanvas.Children.Add(dot);

            sparklineHost.ToolTip = string.Format("Пик за сутки: {0} игрок(ов)", peak);
        }

        private void RebuildLinks(ServerConfig config)
        {
            linksHost.Items.Clear();

            if (config == null || config.Links == null)
            {
                return;
            }

            var chipStyle = TryFindResource("LinkChipStyle") as Style;

            foreach (var link in config.Links)
            {
                if (link == null || string.IsNullOrWhiteSpace(link.Title) || string.IsNullOrWhiteSpace(link.Url))
                {
                    continue;
                }

                var url = link.Url;
                var button = new Button
                {
                    Style = chipStyle,
                    Content = link.Title,
                    ToolTip = url
                };
                button.Click += (s, e) => OpenExternalUrl(url, "ServerLink");
                linksHost.Items.Add(button);
            }
        }

        private void DiscordStatusOption_OnChanged(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings || _settings == null)
            {
                return;
            }

            SaveSettings();

            if (chkDiscordStatus.IsChecked == true)
            {
                ShowIdlePresence();
            }
            else
            {
                _discord.ClearPresence();
            }
        }

        private static string Or(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private void ShowIdlePresence()
        {
            _presencePlaying = false;
            RefreshDiscordPresence();
        }

        private void ShowPlayingPresence()
        {
            _presencePlaying = true;
            _presenceSince = DateTime.UtcNow;
            RefreshDiscordPresence();
        }

        private void RefreshDiscordPresence()
        {
            if (!_discord.IsConnected || chkDiscordStatus.IsChecked != true)
            {
                return;
            }

            var cfg = _cachedServerConfig != null ? _cachedServerConfig.Discord : null;
            var serverName = _cachedServerConfig != null ? _cachedServerConfig.Name : null;

            var details = _presencePlaying
                ? Or(cfg != null ? cfg.DetailsPlaying : null, AppRuntimeConfig.DiscordDefaultPlayingDetails)
                : Or(cfg != null ? cfg.DetailsIdle : null, AppRuntimeConfig.DiscordDefaultIdleDetails);

            _discord.SetPresence(
                details,
                BuildPresenceState(serverName),
                Or(cfg != null ? cfg.LargeImage : null, AppRuntimeConfig.DiscordDefaultLargeImageKey),
                Or(cfg != null ? cfg.LargeText : null, AppRuntimeConfig.DiscordDefaultLargeText),
                _presencePlaying ? _presenceSince : (DateTime?)null);
        }

        private string BuildPresenceState(string serverName)
        {
            var name = Or(serverName, "REALM RolePlay");

            if (!_lastServerOnline.HasValue)
            {
                return name;
            }

            if (!_lastServerOnline.Value)
            {
                return name + " · сервер офлайн";
            }

            return string.Format("{0} · {1}/{2} игроков", name, _lastPlayers, _lastMaxPlayers);
        }

        private void StartProgress(string status)
        {
            progressMods.Minimum = 0;
            progressMods.Maximum = ProgressScale;
            progressMods.Value = 0;
            lblProgressPercent.Text = "0%";
            lblTransferRate.Text = string.Empty;
            if (!string.IsNullOrWhiteSpace(status))
            {
                SetStatus(status);
            }
        }

        private void SetProgress(double fraction, string status)
        {
            var clamped = Math.Max(0d, Math.Min(1d, fraction));
            progressMods.Value = (int)Math.Round(clamped * ProgressScale);
            lblProgressPercent.Text = ((int)Math.Round(clamped * 100)).ToString(CultureInfo.InvariantCulture) + "%";
            if (!string.IsNullOrWhiteSpace(status))
            {
                SetStatus(status);
            }
        }

        private async void BtnCheckUpdates_OnClick(object sender, RoutedEventArgs e)
        {
            await CheckLauncherUpdateAsync(true);
        }

        private async Task<LauncherUpdateCheckResult> CheckManifestWithFallbackAsync(Version currentVersion)
        {
            Exception lastError = null;

            foreach (var url in AppRuntimeConfig.UpdateManifestUrls)
            {
                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                try
                {
                    return await _updateService.CheckForUpdatesAsync(
                        url, currentVersion, _allowedHosts, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }

            throw lastError ?? new InvalidOperationException("URL манифеста обновлений не задан.");
        }

        private async Task CheckLauncherUpdateAsync(bool userInitiated)
        {
            var manifestUrl = AppRuntimeConfig.UpdateManifestUrl;
            if (string.IsNullOrWhiteSpace(manifestUrl))
            {
                if (userInitiated)
                {
                    ShowInfo("URL манифеста обновлений не задан.", "Проверка обновлений");
                }
                return;
            }

            try
            {
                if (userInitiated)
                {
                    SetBusy(true);
                    btnCancelSync.Visibility = Visibility.Collapsed;
                    StartProgress("Проверка обновлений лаунчера...");
                }

                var currentVersion = typeof(MainWindow).Assembly.GetName().Version ?? new Version(1, 0, 0, 0);
                var result = await CheckManifestWithFallbackAsync(currentVersion);
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
                    SetBusy(false);
                }
            }
        }

        private async Task DownloadAndApplyLauncherUpdateAsync(LauncherUpdateManifest manifest)
        {
            StartProgress("Скачивание обновления лаунчера...");
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
                    Dispatcher.BeginInvoke(new Action(() => SetProgress(0.05 + (0.85 * fraction), status)));
                },
                CancellationToken.None);

            SetProgress(0.95, "Установка обновления...");
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
            lblStatus.Text = text;
        }

        private void ClearLog()
        {
            _logLines.Clear();
            txtLog.Clear();
            UpdateLogPlaceholder();
        }

        private void UpdateLogPlaceholder()
        {
            txtLogPlaceholder.Visibility = _logLines.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void AppendLog(string line)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_isClosing)
                {
                    return;
                }

                _logLines.Add(string.Format("[{0:HH:mm:ss}] {1}", DateTime.Now, line));
                if (_logLines.Count > MaxLogLines)
                {
                    _logLines.RemoveRange(0, _logLines.Count - MaxLogLines);
                }

                txtLog.Text = string.Join(Environment.NewLine, _logLines);
                txtLog.CaretIndex = txtLog.Text.Length;
                txtLog.ScrollToEnd();
                UpdateLogPlaceholder();
            }));
        }
    }
}
