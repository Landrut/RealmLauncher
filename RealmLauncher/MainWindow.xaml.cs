using Microsoft.Win32;
using RealmLauncher.Models;
using RealmLauncher.Services;
using RealmLauncher.Theme;
using RealmLauncher.Ui;
using System;
using System.Collections.Generic;
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

        private const int MaxLogLines = 400;

        private static readonly TimeSpan ServerConfigCacheTtl = TimeSpan.FromMinutes(10);

        private readonly LauncherService _launcherService = new LauncherService();
        private readonly LauncherUpdateService _updateService = new LauncherUpdateService();
        private readonly HashSet<string> _allowedHosts = AppRuntimeConfig.BuildAllowedHosts();
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
        private readonly DispatcherTimer _modSyncAnimationTimer;
        private bool _isRefreshingServerStatus;
        private bool _isLoadingSettings;
        private bool _isClosing;

        private string _rawNews;
        private ServerConfig _cachedServerConfig;
        private DateTime _cachedServerConfigUtc = DateTime.MinValue;

        private bool _isModSyncStatusActive;
        private int _modSyncDone;
        private int _modSyncTotal;
        private string _modSyncCurrentModName = "мод";
        private int _modSyncDotPhase;

        private PasswordBox txtServerPassword { get { return txtServerPasswordInput; } }
        private RichTextBox txtNews { get { return rtbNewsBox; } }
        private TextBox txtLog { get { return txtLogBox; } }
        private Button btnPlay { get { return btnPlayMain; } }

        private TextBox txtConanExe { get { return SettingsPage.txtConanExe; } }
        private CheckBox chkDisableIntro { get { return SettingsPage.chkDisableIntro; } }
        private CheckBox chkAutoSubscribe { get { return SettingsPage.chkAutoSubscribe; } }
        private CheckBox chkBoostLoading { get { return SettingsPage.chkBoostLoading; } }
        private Button btnCheckUpdates { get { return SettingsPage.btnCheckUpdates; } }
        private Button btnCheckSteamCmd { get { return SettingsPage.btnCheckSteamCmd; } }
        private Button btnBrowseConanExe { get { return SettingsPage.btnBrowseConanExe; } }
        private Panel themeOptions { get { return SettingsPage.themeOptions; } }

        public MainWindow()
        {
            InitializeComponent();
            WirePageEvents();
            ApplyBrandingAssets();
            LoadSettings();
            ShowMainPage();

            _serverStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _serverStatusTimer.Tick += ServerStatusTimer_OnTick;
            _modSyncAnimationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
            _modSyncAnimationTimer.Tick += (s, e) => RefreshModSyncAnimatedStatus();

            Loaded += MainWindow_Loaded;
            Loaded += (s, e) => UpdateWindowClip();
            SizeChanged += (s, e) => UpdateWindowClip();
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

            SettingsPage.txtConanExe.TextChanged += SettingsControl_OnChanged;
            SettingsPage.chkDisableIntro.Checked += SettingsControl_OnChanged;
            SettingsPage.chkDisableIntro.Unchecked += SettingsControl_OnChanged;
            SettingsPage.chkAutoSubscribe.Checked += SettingsControl_OnChanged;
            SettingsPage.chkAutoSubscribe.Unchecked += SettingsControl_OnChanged;
            SettingsPage.chkBoostLoading.Checked += SettingsControl_OnChanged;
            SettingsPage.chkBoostLoading.Unchecked += SettingsControl_OnChanged;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var newsTask = LoadNewsAsync();
            var statusTask = RefreshServerStatusAsync();
            await Task.WhenAll(newsTask, statusTask);

            _serverStatusTimer.Start();
            await CheckLauncherUpdateAsync(false);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            _isClosing = true;
            _serverStatusTimer.Stop();
            _modSyncAnimationTimer.Stop();

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

            _httpClient.Dispose();
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
                chkDisableIntro.IsChecked = _settings.DisableCinematicIntro;
                chkAutoSubscribe.IsChecked = _settings.AutomaticallySubscribeToWorkshopMods;
                chkBoostLoading.IsChecked = _settings.BoostIngameLoading;

                var themeKey = ThemeManager.Normalize(_settings.UiTheme);
                BuildThemeOptions(themeKey);
                ThemeManager.Apply(themeKey);

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
            _settings.DisableCinematicIntro = chkDisableIntro.IsChecked == true;
            _settings.AutomaticallySubscribeToWorkshopMods = chkAutoSubscribe.IsChecked == true;
            _settings.BoostIngameLoading = chkBoostLoading.IsChecked == true;
            _settings.UiTheme = GetSelectedThemeKey();

            string error;
            if (!_settings.TrySave(out error) && !_isClosing)
            {
                AppendLog("Не удалось сохранить настройки: " + error);
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
            try
            {
                var discordUri = UrlSecurity.RequireAllowedHttpsUrl(
                    AppRuntimeConfig.DiscordInviteUrl, _allowedHosts, "DiscordInviteUrl");
                Process.Start(new ProcessStartInfo(discordUri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ShowError("Не удалось открыть ссылку Discord:\n" + ex.Message);
            }
        }

        private void BtnClearLog_OnClick(object sender, RoutedEventArgs e)
        {
            _logLines.Clear();
            txtLog.Clear();
            UpdateLogPlaceholder();
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

        private async void BtnRefreshNews_OnClick(object sender, RoutedEventArgs e)
        {
            btnRefreshNews.IsEnabled = false;
            try
            {
                await LoadNewsAsync();
            }
            finally
            {
                btnRefreshNews.IsEnabled = true;
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
                _rawNews = await _httpClient.GetStringAsync(newsUri);
                RenderNews(_rawNews);
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
            var titleBrush = ThemeBrush("TextPrimaryBrush", "#E9EFFA");
            var linkBrush = ThemeBrush("AccentBrush", "#3B82F6");
            var bodyBrush = ThemeBrush("TextSecondaryBrush", "#9BAEC9");
            var cardBackground = ThemeBrush("SurfaceBrush", "#B3101B2E");
            var cardBorder = ThemeBrush("StrokeBrush", "#242F49");

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
                Foreground = ThemeBrush("TextSecondaryBrush", "#9BAEC9"),
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
                ToggleUi(false);
                ClearLog();
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
                var config = await GetServerConfigAsync(true, _cts.Token);
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
            catch (OperationCanceledException)
            {
                SetStatus("Операция отменена.");
                AppendLog("Операция отменена.");
            }
            catch (Exception ex)
            {
                SetStatus("Ошибка.");
                AppendLog("ОШИБКА: " + ex.Message);
                ShowError(ex.Message);
            }
            finally
            {
                StopModSyncAnimation();
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
                ToggleUi(true);
            }
        }

        private async void BtnCheckUpdates_OnClick(object sender, RoutedEventArgs e)
        {
            await CheckLauncherUpdateAsync(true);
        }

        private void ToggleUi(bool enabled)
        {
            txtConanExe.IsEnabled = enabled;
            txtServerPassword.IsEnabled = enabled;
            chkDisableIntro.IsEnabled = enabled;
            chkAutoSubscribe.IsEnabled = enabled;
            chkBoostLoading.IsEnabled = enabled;
            themeOptions.IsEnabled = enabled;
            btnCheckUpdates.IsEnabled = enabled;
            btnCheckSteamCmd.IsEnabled = enabled;
            btnBrowseConanExe.IsEnabled = enabled;
            btnPlay.IsEnabled = enabled;
            btnDiscord.IsEnabled = enabled;
            btnRefreshNews.IsEnabled = enabled;
            btnClearLog.IsEnabled = enabled;
            btnNavMain.IsEnabled = enabled;
            btnNavSettings.IsEnabled = enabled;
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
                    return string.Format("• [{0}] {1}/{2} ({3})", x.Status, x.ModId, x.PakName, sizeText);
                })
                .ToList();

            if (analysis.Updates.Count > 20)
            {
                lines.Add(string.Format("• ... и ещё {0} мод(ов)", analysis.Updates.Count - 20));
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

        private void UpdateSteamStatusLabel()
        {
            var running = IsSteamClientRunning();
            lblSteamCmdStatus.Text = running ? "запущен" : "не запущен";
            lblSteamCmdStatus.Foreground = running
                ? ThemeBrush("SuccessBrush", "#3DDC97")
                : ThemeBrush("TextMutedBrush", "#6B7F9C");
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
                AppRuntimeConfig.ServerConfigUrl, _allowedHosts, cancellationToken);

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
                    var host = ExtractHost(config.Ip);
                    var queryPort = config.QueryPort ?? AppRuntimeConfig.DefaultQueryPort;
                    var serverInfo = await _launcherService.QueryServerInfoAsync(host, queryPort, cts.Token);

                    if (_isClosing)
                    {
                        return;
                    }

                    if (serverInfo.IsOnline)
                    {
                        SetServerStatusUi("Онлайн",
                            string.Format("{0}/{1}", serverInfo.Players, serverInfo.MaxPlayers),
                            ThemeBrush("SuccessBrush", "#3DDC97"));
                    }
                    else
                    {
                        SetServerStatusUi("Офлайн", "0/0", ThemeBrush("DangerBrush", "#FF6B6B"));
                    }
                }
            }
            catch
            {
                if (!_isClosing)
                {
                    SetServerStatusUi("Недоступен", "--/--", ThemeBrush("DangerBrush", "#FF6B6B"));
                }
            }
            finally
            {
                if (!_isClosing)
                {
                    UpdateSteamStatusLabel();
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
            progressMods.Minimum = 0;
            progressMods.Maximum = ProgressScale;
            progressMods.Value = 0;
            lblProgressPercent.Text = "0%";
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

        private void UpdateModSyncProgress(double current, double total, string modLabel)
        {
            var totalSafe = Math.Max(1d, total);
            var modFraction = Math.Max(0d, Math.Min(1d, current / totalSafe));
            var overall = StageModsStart + ((StageModsEnd - StageModsStart) * modFraction);

            var totalInt = (int)Math.Round(totalSafe);
            var completed = (int)Math.Floor(Math.Max(0d, Math.Min(totalSafe, current)));
            var allDone = current >= totalSafe - 0.0001d;
            var cleanModName = ExtractModDisplayName(modLabel);

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_isClosing)
                {
                    return;
                }

                _modSyncDone = completed;
                _modSyncTotal = totalInt;
                _modSyncCurrentModName = cleanModName;
                SetProgress(overall, null);

                if (allDone)
                {
                    StopModSyncAnimation();
                    SetStatus(string.Format("Обновление модов: {0}/{1} ({2} — завершено)", _modSyncDone, _modSyncTotal, _modSyncCurrentModName));
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
            }));
        }

        private void StopModSyncAnimation()
        {
            _isModSyncStatusActive = false;
            _modSyncAnimationTimer.Stop();
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

            return string.IsNullOrWhiteSpace(value) ? "мод" : value;
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
