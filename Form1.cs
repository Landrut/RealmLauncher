using System;
using System.Configuration;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using RealmLauncher.Models;
using RealmLauncher.Services;

namespace RealmLauncher
{
    public partial class Form1 : Form
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
        private LauncherSettings _settings;
        private CancellationTokenSource _cts;

        public Form1()
        {
            InitializeComponent();
            LoadSettings();
            Shown += Form1_Shown;
        }

        private void LoadSettings()
        {
            _settings = LauncherSettings.Load();

            txtConfigUrl.Text = _settings.ConfigUrl ?? string.Empty;
            txtConanExe.Text = _settings.ConanExePath ?? string.Empty;
            txtServerPassword.Text = _settings.ServerPassword ?? string.Empty;
            chkDisableIntro.Checked = _settings.DisableCinematicIntro;
            chkAutoSubscribe.Checked = _settings.AutomaticallySubscribeToWorkshopMods;
            UpdateSteamCmdStatus();
        }

        private void SaveSettings()
        {
            _settings.ConfigUrl = txtConfigUrl.Text.Trim();
            _settings.ConanExePath = txtConanExe.Text.Trim();
            _settings.ServerPassword = txtServerPassword.Text;
            _settings.DisableCinematicIntro = chkDisableIntro.Checked;
            _settings.AutomaticallySubscribeToWorkshopMods = chkAutoSubscribe.Checked;
            _settings.Save();
        }

        private async void btnPlay_Click(object sender, EventArgs e)
        {
            try
            {
                ToggleUi(false);
                txtLog.Clear();
                AppendLog("Старт REALM RolePlay Launcher...");
                StartProgress("Инициализация...");

                SaveSettings();

                if (_cts != null)
                {
                    _cts.Dispose();
                }
                _cts = new CancellationTokenSource();

                lblStatus.Text = "Скачивание конфига сервера...";
                var config = await _launcherService.DownloadConfigAsync(_settings.ConfigUrl, _cts.Token);
                SetProgress(StageConfigLoaded, "Конфиг сервера загружен.");
                AppendLog(string.Format("Сервер: {0}", config.Name));
                AppendLog(string.Format("IP: {0}", config.Ip));
                AppendLog(string.Format("Модов в списке: {0}", config.Mods.Count));

                if (!ValidateServerPassword(config))
                {
                    lblStatus.Text = "Неверный пароль сервера.";
                    return;
                }
                SetProgress(StagePasswordValidated, "Пароль сервера проверен.");

                if (chkDisableIntro.Checked)
                {
                    _launcherService.DisableCinematicIntro(_settings.ConanExePath, AppendLog);
                }

                var steamReady = await EnsureSteamCmdInstalledAsync();
                if (!steamReady)
                {
                    lblStatus.Text = "SteamCMD не установлен.";
                    return;
                }
                SetProgress(StageSteamReady, "SteamCMD готов.");

                lblStatus.Text = "Проверка актуальности модов...";
                var analysis = await _launcherService.AnalyzeModsAsync(_settings.ConanExePath, config.Mods, AppendLog, _cts.Token);
                SetProgress(StageAnalysisDone, "Проверка модов завершена.");
                if (chkAutoSubscribe.Checked)
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
                    var confirmed = ConfirmUpdates(analysis);
                    if (!confirmed)
                    {
                        lblStatus.Text = "Обновление модов отменено пользователем.";
                        return;
                    }

                    var uniqueUpdates = analysis.Updates
                        .GroupBy(x => x.ModId)
                        .Select(x => x.First())
                        .ToList();

                    SetProgress(StageModsStart, "Проверка и обновление модов...");
                    await _launcherService.SyncModsAsync(_settings.ConanExePath, uniqueUpdates, AppendLog, UpdateModSyncProgress, _cts.Token);
                    SetProgress(StageModsEnd, "Моды синхронизированы.");
                }
                else
                {
                    AppendLog("Все моды актуальны, обновление не требуется.");
                    SetProgress(StageModsEnd, "Обновление модов не требуется.");
                }

                lblStatus.Text = "Обновление modlist.txt...";
                var modListPath = _launcherService.WriteModListFile(_settings.ConanExePath, config.Mods, AppendLog);
                AppendLog(string.Format("modlist.txt обновлён: {0}", modListPath));
                SetProgress(StageModlistDone, "modlist.txt обновлён.");

                lblStatus.Text = "Запуск подключения к серверу...";
                _launcherService.LaunchServerConnection(_settings.ConanExePath, config.Ip);
                AppendLog("Игра запущена с авто-подключением.");
                SetProgress(StageLaunched, "Готово. Игра запускается.");
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Ошибка.";
                AppendLog("ОШИБКА: " + ex.Message);
                MessageBox.Show(this, ex.Message, "REALM RolePlay Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                ToggleUi(true);
            }
        }

        private async void Form1_Shown(object sender, EventArgs e)
        {
            try
            {
                await CheckLauncherUpdateAsync(false).ConfigureAwait(true);
            }
            catch
            {
                // На старте не прерываем работу лаунчера, если проверка обновлений недоступна.
            }
        }

        private bool ValidateServerPassword(ServerConfig config)
        {
            var entered = txtServerPassword.Text ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(config.PasswordSha256))
            {
                var enteredHash = ComputeSha256(entered);
                if (!string.Equals(enteredHash, config.PasswordSha256.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(
                        this,
                        "Неверный пароль сервера. Проверь ввод и попробуй снова.",
                        "REALM RolePlay Launcher",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return false;
                }
            }
            else if (!string.IsNullOrWhiteSpace(config.Password))
            {
                if (!string.Equals(entered, config.Password, StringComparison.Ordinal))
                {
                    MessageBox.Show(
                        this,
                        "Неверный пароль сервера. Проверь ввод и попробуй снова.",
                        "REALM RolePlay Launcher",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
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
                string.Format("Примерный размер загрузки: {0:0.0} MB\n\n", totalMb) +
                string.Join("\n", lines) +
                "\n\nПродолжить?";

            var result = MessageBox.Show(
                this,
                message,
                "Подтверждение обновления модов",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            return result == DialogResult.Yes;
        }

        private async void btnCheckSteamCmd_Click(object sender, EventArgs e)
        {
            try
            {
                ToggleUi(false);

                if (_launcherService.IsSteamCmdInstalled())
                {
                    UpdateSteamCmdStatus();
                    AppendLog("SteamCMD уже установлен.");
                    MessageBox.Show(
                        this,
                        "SteamCMD уже установлен и готов к работе.\n\nПуть:\n" + _launcherService.GetSteamCmdPath(),
                        "REALM RolePlay Launcher",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                var installed = await AskAndInstallSteamCmdAsync();
                if (!installed)
                {
                    lblStatus.Text = "Установка SteamCMD отменена.";
                    return;
                }

                lblStatus.Text = "SteamCMD установлен и готов.";
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Ошибка.";
                AppendLog("ОШИБКА: " + ex.Message);
                MessageBox.Show(this, ex.Message, "REALM RolePlay Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                ToggleUi(true);
            }
        }

        private async void btnCheckUpdates_Click(object sender, EventArgs e)
        {
            await CheckLauncherUpdateAsync(true);
        }

        private void btnBrowseConanExe_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = "ConanSandbox.exe|ConanSandbox.exe|Исполняемые файлы (*.exe)|*.exe|Все файлы (*.*)|*.*";
                dialog.Title = "Выберите ConanSandbox.exe";

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    txtConanExe.Text = dialog.FileName;
                }
            }
        }

        private void ToggleUi(bool enabled)
        {
            txtConfigUrl.Enabled = enabled;
            txtConanExe.Enabled = enabled;
            txtServerPassword.Enabled = enabled;
            chkDisableIntro.Enabled = enabled;
            chkAutoSubscribe.Enabled = enabled;
            btnCheckUpdates.Enabled = enabled;
            btnCheckSteamCmd.Enabled = enabled;
            btnBrowseConanExe.Enabled = enabled;
            btnPlay.Enabled = enabled;
        }

        private void StartProgress(string status)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(StartProgress), status);
                return;
            }

            progressMods.Minimum = 0;
            progressMods.Maximum = ProgressScale;
            progressMods.Value = 0;
            if (!string.IsNullOrWhiteSpace(status))
            {
                lblStatus.Text = status;
            }
        }

        private void SetProgress(double fraction, string status = null)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<double, string>(SetProgress), fraction, status);
                return;
            }

            var clamped = Math.Max(0d, Math.Min(1d, fraction));
            var value = (int)Math.Round(clamped * ProgressScale);
            value = Math.Max(progressMods.Minimum, Math.Min(progressMods.Maximum, value));
            progressMods.Value = value;

            if (!string.IsNullOrWhiteSpace(status))
            {
                lblStatus.Text = status;
            }
        }

        private void UpdateModSyncProgress(double current, double total, string modLabel)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<double, double, string>(UpdateModSyncProgress), current, total, modLabel);
                return;
            }

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
            SetProgress(overall, status);
        }

        private async System.Threading.Tasks.Task<bool> EnsureSteamCmdInstalledAsync()
        {
            if (_launcherService.IsSteamCmdInstalled())
            {
                UpdateSteamCmdStatus();
                return true;
            }

            AppendLog("SteamCMD не найден.");
            return await AskAndInstallSteamCmdAsync();
        }

        private async System.Threading.Tasks.Task<bool> AskAndInstallSteamCmdAsync()
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

            var result = MessageBox.Show(
                this,
                message,
                "Установка SteamCMD",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
            {
                UpdateSteamCmdStatus();
                return false;
            }

            lblStatus.Text = "Устанавливка SteamCMD...";
            await _launcherService.InstallSteamCmdAsync(AppendLog, _cts != null ? _cts.Token : CancellationToken.None);
            UpdateSteamCmdStatus();
            return true;
        }

        private void UpdateSteamCmdStatus()
        {
            if (_launcherService.IsSteamCmdInstalled())
            {
                lblSteamCmdStatus.Text = "SteamCMD: установлен";
            }
            else
            {
                lblSteamCmdStatus.Text = "SteamCMD: не установлен";
            }
        }

        private void AppendLog(string line)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(AppendLog), line);
                return;
            }

            var message = string.Format("[{0:HH:mm:ss}] {1}", DateTime.Now, line);
            if (txtLog.TextLength == 0)
            {
                txtLog.Text = message;
            }
            else
            {
                txtLog.AppendText(Environment.NewLine + message);
            }

            txtLog.SelectionStart = txtLog.TextLength;
            txtLog.ScrollToCaret();
        }

        private async System.Threading.Tasks.Task CheckLauncherUpdateAsync(bool userInitiated)
        {
            var manifestUrl = GetUpdateManifestUrl();
            if (string.IsNullOrWhiteSpace(manifestUrl))
            {
                if (userInitiated)
                {
                    MessageBox.Show(
                        this,
                        "URL манифеста обновлений не задан.\n\nДобавь ключ UpdateManifestUrl в App.config и укажи ссылку на JSON манифест.",
                        "Проверка обновлений",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                return;
            }

            try
            {
                if (userInitiated)
                {
                    ToggleUi(false);
                    StartProgress("Проверяю обновления лаунчера...");
                }

                var currentVersion = GetCurrentLauncherVersion();
                var result = await _updateService.CheckForUpdatesAsync(manifestUrl, currentVersion, CancellationToken.None);
                if (!result.IsUpdateAvailable || result.Manifest == null)
                {
                    if (userInitiated)
                    {
                        SetProgress(1.0, "Обновлений не найдено.");
                        MessageBox.Show(
                            this,
                            "У вас уже установлена последняя версия лаунчера (" + currentVersion + ").",
                            "Проверка обновлений",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
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
                    "Размер: " + sizeText +
                    changelog + "\n\n" +
                    "Скачать и установить сейчас?";

                var ask = MessageBox.Show(
                    this,
                    message,
                    "Обновление лаунчера",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (ask != DialogResult.Yes)
                {
                    if (userInitiated)
                    {
                        SetProgress(0, "Обновление отменено.");
                    }
                    return;
                }

                ToggleUi(false);
                await DownloadAndApplyLauncherUpdateAsync(result.Manifest).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                if (userInitiated)
                {
                    MessageBox.Show(
                        this,
                        "Не удалось проверить/установить обновление:\n" + ex.Message,
                        "Обновление лаунчера",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }

                AppendLog("ОШИБКА обновления лаунчера: " + ex.Message);
            }
            finally
            {
                if (!IsDisposed)
                {
                    ToggleUi(true);
                }
            }
        }

        private async System.Threading.Tasks.Task DownloadAndApplyLauncherUpdateAsync(LauncherUpdateManifest manifest)
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
                    SetProgress(0.05 + (0.85 * fraction), status);
                },
                CancellationToken.None).ConfigureAwait(true);

            SetProgress(0.95, "Устанавливаю обновление...");
            _updateService.InstallAndRestart(packagePath);
            SetProgress(1.0, "Обновление установлено. Перезапуск лаунчера...");
            Application.Exit();
        }

        private static Version GetCurrentLauncherVersion()
        {
            return typeof(Form1).Assembly.GetName().Version ?? new Version(1, 0, 0, 0);
        }

        private static string GetUpdateManifestUrl()
        {
            return ConfigurationManager.AppSettings["UpdateManifestUrl"] ?? string.Empty;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes <= 0)
            {
                return "0 B";
            }

            string[] units = { "B", "KB", "MB", "GB" };
            var size = (double)bytes;
            var unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }

            return string.Format("{0:0.##} {1}", size, units[unit]);
        }
    }
}


