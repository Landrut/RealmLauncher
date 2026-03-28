using System;
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
        private readonly LauncherService _launcherService = new LauncherService();
        private LauncherSettings _settings;
        private CancellationTokenSource _cts;

        public Form1()
        {
            InitializeComponent();
            LoadSettings();
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

                SaveSettings();

                if (_cts != null)
                {
                    _cts.Dispose();
                }
                _cts = new CancellationTokenSource();

                lblStatus.Text = "Скачиваю конфиг сервера...";
                var config = await _launcherService.DownloadConfigAsync(_settings.ConfigUrl, _cts.Token);
                AppendLog(string.Format("Сервер: {0}", config.Name));
                AppendLog(string.Format("IP: {0}", config.Ip));
                AppendLog(string.Format("Модов в списке: {0}", config.Mods.Count));

                if (!ValidateServerPassword(config))
                {
                    lblStatus.Text = "Неверный пароль сервера.";
                    return;
                }

                if (chkDisableIntro.Checked)
                {
                    _launcherService.DisableCinematicIntro(_settings.ConanExePath, AppendLog);
                }

                if (chkAutoSubscribe.Checked)
                {
                    var steamReady = await EnsureSteamCmdInstalledAsync();
                    if (!steamReady)
                    {
                        lblStatus.Text = "SteamCMD не установлен.";
                        return;
                    }

                    var analysis = await _launcherService.AnalyzeModsAsync(_settings.ConanExePath, config.Mods, AppendLog, _cts.Token);
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

                        InitializeProgress(uniqueUpdates.Count);
                        lblStatus.Text = "Проверяю и обновляю моды...";
                        await _launcherService.SyncModsAsync(_settings.ConanExePath, uniqueUpdates, AppendLog, UpdateProgress, _cts.Token);
                        CompleteProgress();
                    }
                    else
                    {
                        AppendLog("Все моды актуальны, обновление не требуется.");
                        ResetProgress();
                    }
                }
                else
                {
                    AppendLog("Автоподписка и автообновление модов Workshop отключены в настройках.");
                    ResetProgress();
                }

                lblStatus.Text = "Обновляю modlist.txt...";
                var modListPath = _launcherService.WriteModListFile(_settings.ConanExePath, config.Mods, AppendLog);
                AppendLog(string.Format("modlist.txt обновлён: {0}", modListPath));

                lblStatus.Text = "Запускаю подключение к серверу...";
                _launcherService.LaunchServerConnection(_settings.ConanExePath, config.Ip);
                AppendLog("Игра запущена с авто-подключением.");

                lblStatus.Text = "Готово. Игра запускается.";
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
                string.Format("Оценочный размер загрузки: {0:0.0} MB\n\n", totalMb) +
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

        private void btnBrowseConanExe_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = "ConanSandbox.exe|ConanSandbox.exe|Исполняемые файлы (*.exe)|*.exe|Все файлы (*.*)|*.*";
                dialog.Title = "Выбери ConanSandbox.exe";

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
            btnCheckSteamCmd.Enabled = enabled;
            btnBrowseConanExe.Enabled = enabled;
            btnPlay.Enabled = enabled;
        }

        private void InitializeProgress(int max)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<int>(InitializeProgress), max);
                return;
            }

            progressMods.Minimum = 0;
            progressMods.Maximum = Math.Max(1, max);
            progressMods.Value = 0;
        }

        private void UpdateProgress(int current, int total, string modId)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<int, int, string>(UpdateProgress), current, total, modId);
                return;
            }

            progressMods.Maximum = Math.Max(1, total);
            progressMods.Value = Math.Max(progressMods.Minimum, Math.Min(progressMods.Maximum, current));
            lblStatus.Text = string.Format("Обновление модов: {0}/{1} ({2})", current, total, modId);
        }

        private void CompleteProgress()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(CompleteProgress));
                return;
            }

            progressMods.Value = progressMods.Maximum;
        }

        private void ResetProgress()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(ResetProgress));
                return;
            }

            progressMods.Value = 0;
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

            lblStatus.Text = "Устанавливаю SteamCMD...";
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
    }
}
