using Microsoft.Win32;
using RealmLauncher.Models;
using RealmLauncher.Services;
using RealmLauncher.Ui;
using RealmLauncher.Views;
using System;
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
        private static readonly System.Collections.Generic.Dictionary<string, string> EmojiIconUrls = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal)
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
        private LauncherSettings _settings;
        private CancellationTokenSource _cts;
        private readonly DispatcherTimer _serverStatusTimer;
        private readonly DispatcherTimer _modSyncAnimationTimer;
        private bool _isRefreshingServerStatus;
        private bool _isApplyingTheme;
        private string _serverStatusText = "проверка...";
        private string _serverPlayersText = "Игроки: --/--";
        private Brush _serverStatusBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F4C542"));
        private ThemePalette _themePalette;
        private bool _isModSyncStatusActive;
        private int _modSyncDone;
        private int _modSyncTotal;
        private string _modSyncCurrentModName = "мод";
        private int _modSyncDotPhase;

        private TextBox txtConfigUrl => txtConfigUrlInput;
        private PasswordBox txtServerPassword => txtServerPasswordInput;
        private RichTextBox txtNews => rtbNewsBox;
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
        private CheckBox chkBoostLoading => SettingsPage.chkBoostLoading;
        private Button btnCheckUpdates => SettingsPage.btnCheckUpdates;
        private Button btnCheckSteamCmd => SettingsPage.btnCheckSteamCmd;
        private Button btnBrowseConanExe => SettingsPage.btnBrowseConanExe;
        private ComboBox cmbTheme => SettingsPage.cmbTheme;

        public MainWindow()
        {
            InitializeComponent();
            WirePageEvents();
            ApplyThemeAssets();
            LoadSettings();
            ShowMainPage();
            _serverStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _serverStatusTimer.Tick += async (_, __) => await RefreshServerStatusAsync();
            _modSyncAnimationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
            _modSyncAnimationTimer.Tick += (_, __) => RefreshModSyncAnimatedStatus();
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
            SettingsPage.cmbTheme.SelectionChanged += CmbTheme_OnSelectionChanged;
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

            var assemblyVersion = typeof(MainWindow).Assembly.GetName().Version;
            if (assemblyVersion != null)
            {
                txtLauncherVersion.Text = string.Format(
                    "Launcher v{0}.{1}.{2}.{3}",
                    assemblyVersion.Major < 0 ? 0 : assemblyVersion.Major,
                    assemblyVersion.Minor < 0 ? 0 : assemblyVersion.Minor,
                    assemblyVersion.Build < 0 ? 0 : assemblyVersion.Build,
                    assemblyVersion.Revision < 0 ? 0 : assemblyVersion.Revision);
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
            chkBoostLoading.IsChecked = _settings.BoostIngameLoading;
            SelectThemeInUi(string.IsNullOrWhiteSpace(_settings.UiTheme) ? "Blue" : _settings.UiTheme);
            ApplyThemePalette(GetSelectedThemeKey());
            UpdateSteamCmdStatus();
        }

        private void SaveSettings()
        {
            _settings.ConfigUrl = txtConfigUrl.Text.Trim();
            _settings.ConanExePath = txtConanExe.Text.Trim();
            _settings.SetServerPassword(txtServerPassword.Password);
            _settings.DisableCinematicIntro = chkDisableIntro.IsChecked == true;
            _settings.AutomaticallySubscribeToWorkshopMods = chkAutoSubscribe.IsChecked == true;
            _settings.BoostIngameLoading = chkBoostLoading.IsChecked == true;
            _settings.UiTheme = GetSelectedThemeKey();
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

        private void CmbTheme_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isApplyingTheme || _settings == null)
            {
                return;
            }

            ApplyThemePalette(GetSelectedThemeKey());
            SaveSettings();
            _ = LoadNewsAsync();
        }

        private void SelectThemeInUi(string themeKey)
        {
            if (cmbTheme == null)
            {
                return;
            }

            var key = NormalizeThemeKey(themeKey);
            _isApplyingTheme = true;
            try
            {
                for (var i = 0; i < cmbTheme.Items.Count; i++)
                {
                    if (cmbTheme.Items[i] is ComboBoxItem item &&
                        string.Equals(item.Tag as string, key, StringComparison.OrdinalIgnoreCase))
                    {
                        cmbTheme.SelectedIndex = i;
                        return;
                    }
                }

                cmbTheme.SelectedIndex = 0;
            }
            finally
            {
                _isApplyingTheme = false;
            }
        }

        private string GetSelectedThemeKey()
        {
            if (cmbTheme?.SelectedItem is ComboBoxItem item)
            {
                return NormalizeThemeKey(item.Tag as string);
            }

            return "Blue";
        }

        private static string NormalizeThemeKey(string themeKey)
        {
            return string.Equals(themeKey, "Bronze", StringComparison.OrdinalIgnoreCase) ? "Bronze" : "Blue";
        }

        private static SolidColorBrush Brush(string hex)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }

        private static ThemePalette BuildThemePalette(string themeKey)
        {
            if (string.Equals(themeKey, "Bronze", StringComparison.OrdinalIgnoreCase))
            {
                return new ThemePalette
                {
                    TextMain = Brush("#F3E6CC"),
                    TextMuted = Brush("#BDAA86"),
                    PanelFill = Brush("#7A20160F"),
                    InputBg = Brush("#8A140F0C"),
                    InputStroke = Brush("#8E6B3E"),
                    WindowShellBg = Brush("#120E0B"),
                    WindowOuterBorderBrush = Brush("#6F4D2E"),
                    HeaderFillBrush = Brush("#50312118"),
                    HeaderSeparatorBrush = Brush("#120C09"),
                    FooterFillBrush = Brush("#7A1A120C"),
                    VersionBgBrush = Brush("#3A2A1D12"),
                    VersionBorderBrush = Brush("#8E6B3E"),
                    VersionTextBrush = Brush("#D9BF95"),
                    SectionTitleBrush = Brush("#F3E6CC"),
                    SectionLineBrush = Brush("#C48A45"),
                    SectionLineSoftBrush = Brush("#E1B677"),
                    PanelBorderBrush = Brush("#8E6B3E"),
                    InnerPanelBorderBrush = Brush("#4A3623"),
                    InnerPanelFillBrush = Brush("#6B120F0C"),
                    WelcomePrimaryBrush = Brush("#F1E2C7"),
                    WelcomeSecondaryBrush = Brush("#E2CFAC"),
                    FooterDividerBrush = Brush("#9D7A4D"),
                    ServerTextBrush = Brush("#E6D2AF"),
                    PlayersTextBrush = Brush("#D8C2A0"),
                    StatusDotStrokeBrush = Brush("#2C1E11"),
                    ProgressForegroundBrush = Brush("#D69A49"),
                    ProgressBackgroundBrush = Brush("#2A1C12"),
                    ButtonTextBrush = Brush("#F7EBD5"),
                    ButtonBgBrush = Brush("#6B3E1B"),
                    ButtonBorderBrush = Brush("#DAA15A"),
                    ButtonBgHoverBrush = Brush("#8A5626"),
                    ButtonBorderHoverBrush = Brush("#F0C079"),
                    ButtonBgPressedBrush = Brush("#5A3517"),
                    ButtonSheenBrush = Brush("#E6B16B"),
                    CloseButtonBgBrush = Brush("#6D3D1C"),
                    CheckBorderBrush = Brush("#C48A45"),
                    CheckBackgroundBrush = Brush("#2F2417"),
                    CheckCheckedBgBrush = Brush("#8A5625"),
                    CheckHoverBorderBrush = Brush("#F0C079"),
                    CheckTickBrush = Brush("#F8E8D0"),
                    ComboPopupBgBrush = Brush("#1A120D"),
                    ComboItemHoverBgBrush = Brush("#4E3218"),
                    ComboItemSelectedBgBrush = Brush("#6B3E1B"),
                    ComboFocusBorderBrush = Brush("#A87946"),
                    ComboArrowBrush = Brush("#D7B98C"),
                    ButtonGlowColor = (Color)ColorConverter.ConvertFromString("#D28A39"),
                    CheckGlowColor = (Color)ColorConverter.ConvertFromString("#B6762C"),
                    CheckGlowStartColor = (Color)ColorConverter.ConvertFromString("#F1C887"),
                    CheckGlowEndColor = (Color)ColorConverter.ConvertFromString("#A6672D"),
                    BgGradTop = (Color)ColorConverter.ConvertFromString("#090A0D"),
                    BgGradMid = (Color)ColorConverter.ConvertFromString("#1B1511"),
                    BgGradBottom = (Color)ColorConverter.ConvertFromString("#0D0A08"),
                    BgGlowTop = (Color)ColorConverter.ConvertFromString("#C67D2D"),
                    BgGlowMid = (Color)ColorConverter.ConvertFromString("#5F3B1D"),
                    BgGlowBottom = (Color)ColorConverter.ConvertFromString("#090806"),
                    OverlayTintBrush = Brush("#55170E09"),
                    NewsTitleBrush = Brush("#F6E7CC"),
                    NewsLinkBrush = Brush("#E4B16D"),
                    NewsBodyBrush = Brush("#D9C3A0"),
                    NewsCardBackground = Brush("#66170F0C"),
                    NewsCardBorder = Brush("#A87946"),
                    NewsPlainTextBrush = Brush("#E7D6BA")
                };
            }

            return new ThemePalette
            {
                TextMain = Brush("#E6F0FF"),
                TextMuted = Brush("#9FB5D8"),
                PanelFill = Brush("#6A132B59"),
                InputBg = Brush("#6F091733"),
                InputStroke = Brush("#5E76B6"),
                WindowShellBg = Brush("#152D56"),
                WindowOuterBorderBrush = Brush("#436496"),
                HeaderFillBrush = Brush("#50183364"),
                HeaderSeparatorBrush = Brush("#020E27"),
                FooterFillBrush = Brush("#7A030D26"),
                VersionBgBrush = Brush("#3A132B59"),
                VersionBorderBrush = Brush("#5E7DB5"),
                VersionTextBrush = Brush("#BFD7FF"),
                SectionTitleBrush = Brush("#E5F0FF"),
                SectionLineBrush = Brush("#6DB2FF"),
                SectionLineSoftBrush = Brush("#8FBFF9"),
                PanelBorderBrush = Brush("#5E7DB5"),
                InnerPanelBorderBrush = Brush("#2B3D61"),
                InnerPanelFillBrush = Brush("#6B081B39"),
                WelcomePrimaryBrush = Brush("#ECF4FF"),
                WelcomeSecondaryBrush = Brush("#ECF4FF"),
                FooterDividerBrush = Brush("#7096D4"),
                ServerTextBrush = Brush("#D7E8FF"),
                PlayersTextBrush = Brush("#BBD2F9"),
                StatusDotStrokeBrush = Brush("#0A1328"),
                ProgressForegroundBrush = Brush("#4AAFFF"),
                ProgressBackgroundBrush = Brush("#2A406A"),
                ButtonTextBrush = Brush("#EAF5FF"),
                ButtonBgBrush = Brush("#3E72CF"),
                ButtonBorderBrush = Brush("#6EA8FF"),
                ButtonBgHoverBrush = Brush("#4A84EA"),
                ButtonBorderHoverBrush = Brush("#9BC8FF"),
                ButtonBgPressedBrush = Brush("#325FAF"),
                ButtonSheenBrush = Brush("#78C9FF"),
                CloseButtonBgBrush = Brush("#3D78D8"),
                CheckBorderBrush = Brush("#6FA9FF"),
                CheckBackgroundBrush = Brush("#1D3562"),
                CheckCheckedBgBrush = Brush("#2A67C2"),
                CheckHoverBorderBrush = Brush("#9ED0FF"),
                CheckTickBrush = Brush("#ECF4FF"),
                ComboPopupBgBrush = Brush("#132745"),
                ComboItemHoverBgBrush = Brush("#2A4776"),
                ComboItemSelectedBgBrush = Brush("#365E98"),
                ComboFocusBorderBrush = Brush("#5E76B6"),
                ComboArrowBrush = Brush("#9FB5D8"),
                ButtonGlowColor = (Color)ColorConverter.ConvertFromString("#55A8FF"),
                CheckGlowColor = (Color)ColorConverter.ConvertFromString("#3E86E5"),
                CheckGlowStartColor = (Color)ColorConverter.ConvertFromString("#82D2FF"),
                CheckGlowEndColor = (Color)ColorConverter.ConvertFromString("#2A7CE0"),
                BgGradTop = (Color)ColorConverter.ConvertFromString("#102A55"),
                BgGradMid = (Color)ColorConverter.ConvertFromString("#1B3D73"),
                BgGradBottom = (Color)ColorConverter.ConvertFromString("#0F2C5A"),
                BgGlowTop = (Color)ColorConverter.ConvertFromString("#3B7EDB"),
                BgGlowMid = (Color)ColorConverter.ConvertFromString("#1E4E93"),
                BgGlowBottom = (Color)ColorConverter.ConvertFromString("#001229"),
                OverlayTintBrush = Brush("#66040C23"),
                NewsTitleBrush = Brush("#EAF4FF"),
                NewsLinkBrush = Brush("#7EC1FF"),
                NewsBodyBrush = Brush("#D2E3FF"),
                NewsCardBackground = Brush("#5F0B2345"),
                NewsCardBorder = Brush("#4D7CB7"),
                NewsPlainTextBrush = Brush("#DCEBFF")
            };
        }

        private void ApplyThemePalette(string themeKey)
        {
            _isApplyingTheme = true;
            try
            {
                _themePalette = BuildThemePalette(themeKey);
                Resources["TextMain"] = _themePalette.TextMain;
                Resources["TextMuted"] = _themePalette.TextMuted;
                Resources["PanelFill"] = _themePalette.PanelFill;
                Resources["InputBg"] = _themePalette.InputBg;
                Resources["InputStroke"] = _themePalette.InputStroke;
                Resources["WindowShellBg"] = _themePalette.WindowShellBg;
                Resources["WindowOuterBorderBrush"] = _themePalette.WindowOuterBorderBrush;
                Resources["HeaderFillBrush"] = _themePalette.HeaderFillBrush;
                Resources["HeaderSeparatorBrush"] = _themePalette.HeaderSeparatorBrush;
                Resources["FooterFillBrush"] = _themePalette.FooterFillBrush;
                Resources["VersionBgBrush"] = _themePalette.VersionBgBrush;
                Resources["VersionBorderBrush"] = _themePalette.VersionBorderBrush;
                Resources["VersionTextBrush"] = _themePalette.VersionTextBrush;
                Resources["SectionTitleBrush"] = _themePalette.SectionTitleBrush;
                Resources["SectionLineBrush"] = _themePalette.SectionLineBrush;
                Resources["SectionLineSoftBrush"] = _themePalette.SectionLineSoftBrush;
                Resources["PanelBorderBrush"] = _themePalette.PanelBorderBrush;
                Resources["InnerPanelBorderBrush"] = _themePalette.InnerPanelBorderBrush;
                Resources["InnerPanelFillBrush"] = _themePalette.InnerPanelFillBrush;
                Resources["WelcomePrimaryBrush"] = _themePalette.WelcomePrimaryBrush;
                Resources["WelcomeSecondaryBrush"] = _themePalette.WelcomeSecondaryBrush;
                Resources["FooterDividerBrush"] = _themePalette.FooterDividerBrush;
                Resources["ServerTextBrush"] = _themePalette.ServerTextBrush;
                Resources["PlayersTextBrush"] = _themePalette.PlayersTextBrush;
                Resources["StatusDotStrokeBrush"] = _themePalette.StatusDotStrokeBrush;
                Resources["ProgressForegroundBrush"] = _themePalette.ProgressForegroundBrush;
                Resources["ProgressBackgroundBrush"] = _themePalette.ProgressBackgroundBrush;
                Resources["ButtonTextBrush"] = _themePalette.ButtonTextBrush;
                Resources["ButtonBgBrush"] = _themePalette.ButtonBgBrush;
                Resources["ButtonBorderBrush"] = _themePalette.ButtonBorderBrush;
                Resources["ButtonBgHoverBrush"] = _themePalette.ButtonBgHoverBrush;
                Resources["ButtonBorderHoverBrush"] = _themePalette.ButtonBorderHoverBrush;
                Resources["ButtonBgPressedBrush"] = _themePalette.ButtonBgPressedBrush;
                Resources["ButtonSheenBrush"] = _themePalette.ButtonSheenBrush;
                Resources["CloseButtonBgBrush"] = _themePalette.CloseButtonBgBrush;
                Resources["CheckBorderBrush"] = _themePalette.CheckBorderBrush;
                Resources["CheckBackgroundBrush"] = _themePalette.CheckBackgroundBrush;
                Resources["CheckCheckedBgBrush"] = _themePalette.CheckCheckedBgBrush;
                Resources["CheckHoverBorderBrush"] = _themePalette.CheckHoverBorderBrush;
                Resources["CheckTickBrush"] = _themePalette.CheckTickBrush;
                Resources["ComboPopupBgBrush"] = _themePalette.ComboPopupBgBrush;
                Resources["ComboItemHoverBgBrush"] = _themePalette.ComboItemHoverBgBrush;
                Resources["ComboItemSelectedBgBrush"] = _themePalette.ComboItemSelectedBgBrush;
                Resources["ComboFocusBorderBrush"] = _themePalette.ComboFocusBorderBrush;
                Resources["ComboArrowBrush"] = _themePalette.ComboArrowBrush;
                Resources["ButtonGlowColor"] = _themePalette.ButtonGlowColor;
                Resources["CheckGlowColor"] = _themePalette.CheckGlowColor;
                Resources["CheckGlowStartColor"] = _themePalette.CheckGlowStartColor;
                Resources["CheckGlowEndColor"] = _themePalette.CheckGlowEndColor;
                Resources["BgGradTop"] = _themePalette.BgGradTop;
                Resources["BgGradMid"] = _themePalette.BgGradMid;
                Resources["BgGradBottom"] = _themePalette.BgGradBottom;
                Resources["BgGlowTop"] = _themePalette.BgGlowTop;
                Resources["BgGlowMid"] = _themePalette.BgGlowMid;
                Resources["BgGlowBottom"] = _themePalette.BgGlowBottom;
                Resources["OverlayTintBrush"] = _themePalette.OverlayTintBrush;
            }
            finally
            {
                _isApplyingTheme = false;
            }
        }

        private async Task LoadNewsAsync()
        {
            var newsUrl = AppRuntimeConfig.NewsFeedUrl;
            if (string.IsNullOrWhiteSpace(newsUrl))
            {
                SetNewsPlainText("URL новостей не задан.\n\nДобавь ключ NewsFeedUrl в AppRuntimeConfig и укажи raw-ссылку на Gist.");
                return;
            }

            try
            {
                var newsUri = UrlSecurity.RequireAllowedHttpsUrl(newsUrl, _allowedHosts, "NewsFeedUrl");
                var raw = await _httpClient.GetStringAsync(newsUri);
                RenderNews(raw);
            }
            catch (Exception ex)
            {
                SetNewsPlainText("Не удалось загрузить новости.\n\n" + ex.Message);
            }
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
            foreach (var item in items.OfType<JObject>())
            {
                var title = item.Value<string>("title")?.Trim();
                var body = item.Value<string>("body")?.Trim();
                if (string.IsNullOrWhiteSpace(body))
                {
                    body = item.Value<string>("description")?.Trim() ?? item.Value<string>("summary")?.Trim();
                }

                var link = item.Value<string>("url")?.Trim();
                if (string.IsNullOrWhiteSpace(link))
                {
                    link = item.Value<string>("link")?.Trim();
                }

                if (TryExtractMarkdownLink(title, out var mdTitle, out var mdLink))
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
                        Foreground = _themePalette?.NewsTitleBrush ?? Brush("#EAF4FF"),
                        FontSize = 15.5,
                        FontWeight = FontWeights.SemiBold,
                        TextWrapping = TextWrapping.Wrap
                    };
                    if (!string.IsNullOrWhiteSpace(link) && Uri.TryCreate(link, UriKind.Absolute, out var uri))
                    {
                        var hyperlink = new Hyperlink(new Run(title))
                        {
                            NavigateUri = uri,
                            TextDecorations = TextDecorations.Underline,
                            Foreground = _themePalette?.NewsLinkBrush ?? Brush("#7EC1FF")
                        };
                        hyperlink.Inlines.Clear();
                        AddTextWithEmojiInlines(hyperlink.Inlines, title, 16);
                        hyperlink.RequestNavigate += NewsHyperlink_RequestNavigate;
                        titleBlock.Inlines.Add(hyperlink);
                    }
                    else
                    {
                        AddTextWithEmojiInlines(titleBlock.Inlines, title, 16);
                    }
                    panel.Children.Add(titleBlock);
                }

                if (!string.IsNullOrWhiteSpace(body))
                {
                    panel.Children.Add(new TextBlock
                    {
                        Margin = new Thickness(0, 6, 0, 0),
                        Foreground = _themePalette?.NewsBodyBrush ?? Brush("#D2E3FF"),
                        FontSize = 13.2,
                        TextWrapping = TextWrapping.Wrap
                    });
                    AddTextWithEmojiInlines(((TextBlock)panel.Children[panel.Children.Count - 1]).Inlines, body, 14);
                }
                var border = new Border
                {
                    Background = _themePalette?.NewsCardBackground ?? Brush("#5F0B2345"),
                    BorderBrush = _themePalette?.NewsCardBorder ?? Brush("#4D7CB7"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(10, 8, 10, 9),
                    Margin = new Thickness(0, 0, 0, 8),
                    Child = panel
                };
                doc.Blocks.Add(new BlockUIContainer(border));
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
                Foreground = _themePalette?.NewsPlainTextBrush ?? Brush("#DCEBFF"),
                FontSize = 13.5,
                Margin = new Thickness(0)
            });
            txtNews.Document = doc;
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

        private FlowDocument CreateNewsDocument()
        {
            return new FlowDocument
            {
                Background = Brushes.Transparent,
                PagePadding = new Thickness(0),
                TextAlignment = TextAlignment.Left,
                FontFamily = new FontFamily("TT Norms Pro, Segoe UI Emoji"),
                LineHeight = 20
            };
        }

        private void NewsHyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                var safeUri = UrlSecurity.RequireAllowedHttpsUrl(e.Uri.ToString(), _allowedHosts, "NewsLink");
                Process.Start(new ProcessStartInfo(safeUri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ShowError("Не удалось открыть ссылку новости:\n" + ex.Message);
            }
        }

        private void AddTextWithEmojiInlines(InlineCollection inlines, string text, double emojiSize)
        {
            if (inlines == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var buffer = new StringBuilder();
            var enumerator = StringInfo.GetTextElementEnumerator(text);
            while (enumerator.MoveNext())
            {
                var element = enumerator.GetTextElement();
                if (TryCreateEmojiInline(element, emojiSize, out var emojiInline))
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

        private static bool TryCreateEmojiInline(string element, double size, out InlineUIContainer inline)
        {
            inline = null;
            if (string.IsNullOrWhiteSpace(element))
            {
                return false;
            }

            if (!EmojiIconUrls.TryGetValue(element, out var url))
            {
                return false;
            }

            var image = new Image
            {
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(0, -2, 0, -2),
                Source = new BitmapImage(new Uri(url, UriKind.Absolute))
            };

            inline = new InlineUIContainer(image) { BaselineAlignment = BaselineAlignment.TextBottom };
            return true;
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

                SetStatus("Загрузка конфига сервера...");
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

                _launcherService.ApplyNetworkSpeedPreset(_settings.ConanExePath, chkBoostLoading.IsChecked == true, AppendLog);

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

                var modListSnapshot = _launcherService.CaptureModListSnapshot(_settings.ConanExePath);
                var modListWasReplaced = false;
                try
                {
                    SetStatus("Обновление modlist.txt...");
                    var modListPath = _launcherService.WriteModListFile(_settings.ConanExePath, config.Mods, AppendLog);
                    modListWasReplaced = true;
                    AppendLog("modlist.txt обновлён: " + modListPath);
                    SetProgress(StageModlistDone, "modlist.txt обновлён.");

                    SetStatus("Подключение к серверу...");
                    _launcherService.LaunchServerConnection(_settings.ConanExePath, config.Ip);
                    AppendLog("Игра запущена с авто-подключением.");
                    SetProgress(StageLaunched, "Готово. Игра запускается.");
                }
                finally
                {
                    if (modListWasReplaced)
                    {
                        try
                        {
                            await Task.Delay(3500, _cts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                        }

                        try
                        {
                            _launcherService.RestoreModListSnapshot(_settings.ConanExePath, modListSnapshot, AppendLog);
                        }
                        catch (Exception restoreEx)
                        {
                            AppendLog("ПРЕДУПРЕЖДЕНИЕ: не удалось восстановить исходный modlist.txt: " + restoreEx.Message);
                        }
                    }
                }
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
            chkBoostLoading.IsEnabled = enabled;
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

            AppendLog("Steam не запущен. Выполняется запуск Steam...");
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
            var allDone = current >= totalSafe - 0.0001d;
            var cleanModName = ExtractModDisplayName(modLabel);

            Dispatcher.Invoke(() =>
            {
                _modSyncDone = completed;
                _modSyncTotal = totalInt;
                _modSyncCurrentModName = cleanModName;
                SetProgress(overall, null);

                if (allDone)
                {
                    _isModSyncStatusActive = false;
                    _modSyncAnimationTimer.Stop();
                    SetStatus(string.Format("Обновление модов: {0}/{1} ({2} - завершено)", _modSyncDone, _modSyncTotal, _modSyncCurrentModName));
                }
                else
                {
                    if (!_isModSyncStatusActive)
                    {
                        _isModSyncStatusActive = true;
                        _modSyncDotPhase = 0;
                        _modSyncAnimationTimer.Start();
                    }

                    RefreshModSyncAnimatedStatus();
                }
            });
        }

        private void RefreshModSyncAnimatedStatus()
        {
            if (!_isModSyncStatusActive)
            {
                return;
            }

            _modSyncDotPhase = (_modSyncDotPhase % 5) + 1;
            var dots = new string('.', _modSyncDotPhase);
            SetStatus(string.Format("Обновление модов: {0}/{1} ({2}{3})", _modSyncDone, _modSyncTotal, _modSyncCurrentModName, dots));
        }

        private static string ExtractModDisplayName(string rawLabel)
        {
            if (string.IsNullOrWhiteSpace(rawLabel))
            {
                return "мод";
            }

            var value = rawLabel.Trim();

            // "123456789/SomeMod.pak" -> "SomeMod"
            var slash = value.LastIndexOf('/');
            if (slash >= 0 && slash < value.Length - 1)
            {
                value = value.Substring(slash + 1);
            }

            // "C:\...\SomeMod.pak" -> "SomeMod"
            try
            {
                value = Path.GetFileNameWithoutExtension(value);
            }
            catch
            {
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                return "мод";
            }

            return value;
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
                    Dispatcher.Invoke(() => SetProgress(0.05 + (0.85 * fraction), status));
                },
                CancellationToken.None);

            SetProgress(0.95, "Установка обновления...");
            _updateService.InstallAndRestart(packagePath);
            SetProgress(1.0, "Обновление установлено. Перезапуск...");
            Application.Current.Shutdown();
        }

        private sealed class ThemePalette
        {
            public Brush TextMain { get; set; }
            public Brush TextMuted { get; set; }
            public Brush PanelFill { get; set; }
            public Brush InputBg { get; set; }
            public Brush InputStroke { get; set; }
            public Brush WindowShellBg { get; set; }
            public Brush WindowOuterBorderBrush { get; set; }
            public Brush HeaderFillBrush { get; set; }
            public Brush HeaderSeparatorBrush { get; set; }
            public Brush FooterFillBrush { get; set; }
            public Brush VersionBgBrush { get; set; }
            public Brush VersionBorderBrush { get; set; }
            public Brush VersionTextBrush { get; set; }
            public Brush SectionTitleBrush { get; set; }
            public Brush SectionLineBrush { get; set; }
            public Brush SectionLineSoftBrush { get; set; }
            public Brush PanelBorderBrush { get; set; }
            public Brush InnerPanelBorderBrush { get; set; }
            public Brush InnerPanelFillBrush { get; set; }
            public Brush WelcomePrimaryBrush { get; set; }
            public Brush WelcomeSecondaryBrush { get; set; }
            public Brush FooterDividerBrush { get; set; }
            public Brush ServerTextBrush { get; set; }
            public Brush PlayersTextBrush { get; set; }
            public Brush StatusDotStrokeBrush { get; set; }
            public Brush ProgressForegroundBrush { get; set; }
            public Brush ProgressBackgroundBrush { get; set; }
            public Brush ButtonTextBrush { get; set; }
            public Brush ButtonBgBrush { get; set; }
            public Brush ButtonBorderBrush { get; set; }
            public Brush ButtonBgHoverBrush { get; set; }
            public Brush ButtonBorderHoverBrush { get; set; }
            public Brush ButtonBgPressedBrush { get; set; }
            public Brush ButtonSheenBrush { get; set; }
            public Brush CloseButtonBgBrush { get; set; }
            public Brush CheckBorderBrush { get; set; }
            public Brush CheckBackgroundBrush { get; set; }
            public Brush CheckCheckedBgBrush { get; set; }
            public Brush CheckHoverBorderBrush { get; set; }
            public Brush CheckTickBrush { get; set; }
            public Brush ComboPopupBgBrush { get; set; }
            public Brush ComboItemHoverBgBrush { get; set; }
            public Brush ComboItemSelectedBgBrush { get; set; }
            public Brush ComboFocusBorderBrush { get; set; }
            public Brush ComboArrowBrush { get; set; }
            public Color ButtonGlowColor { get; set; }
            public Color CheckGlowColor { get; set; }
            public Color CheckGlowStartColor { get; set; }
            public Color CheckGlowEndColor { get; set; }
            public Color BgGradTop { get; set; }
            public Color BgGradMid { get; set; }
            public Color BgGradBottom { get; set; }
            public Color BgGlowTop { get; set; }
            public Color BgGlowMid { get; set; }
            public Color BgGlowBottom { get; set; }
            public Brush OverlayTintBrush { get; set; }
            public Brush NewsTitleBrush { get; set; }
            public Brush NewsLinkBrush { get; set; }
            public Brush NewsBodyBrush { get; set; }
            public Brush NewsCardBackground { get; set; }
            public Brush NewsCardBorder { get; set; }
            public Brush NewsPlainTextBrush { get; set; }
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



