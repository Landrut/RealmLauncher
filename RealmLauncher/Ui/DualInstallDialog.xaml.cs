using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using RealmLauncher.Services;

namespace RealmLauncher.Ui
{
    /// <summary>
    /// Walks the player through putting Enhanced and Legacy on disk at the same time:
    /// the current build is moved into a folder of its own, Steam then fills the default
    /// folder with the other one, and from that point the launcher starts either without
    /// a download.
    /// </summary>
    public partial class DualInstallDialog : Window
    {
        private enum Step
        {
            Blocked,
            AlreadyReady,
            Intro,
            Rename,
            Done
        }

        private readonly GameEditionState _state;
        private readonly Action<string> _log;
        private readonly GameEdition _currentEdition;
        private readonly string _sourcePath;
        private readonly string _destinationPath;
        private readonly string _libraryAnchor;

        private Step _step;
        private bool _changed;

        /// <summary>Returns true when the folders on disk changed and detection has to run again.</summary>
        public static bool Show(Window owner, GameEditionState state, Action<string> log)
        {
            var dialog = new DualInstallDialog(state, log);

            if (owner != null && owner.IsLoaded && owner.IsVisible)
            {
                dialog.Owner = owner;
            }
            else
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            dialog.ShowDialog();
            return dialog._changed;
        }

        private DualInstallDialog(GameEditionState state, Action<string> log)
        {
            InitializeComponent();

            _state = state ?? new GameEditionState();
            _log = log;

            _sourcePath = _state.SteamManagedDirectory;

            // Without a Steam manifest there is no branch to read, so go by what is on disk.
            var onDisk = GameEditions.All(_state).FirstOrDefault();
            _currentEdition = !string.IsNullOrEmpty(_sourcePath) || onDisk == null
                ? _state.SteamBranchEdition
                : onDisk.Edition;

            _destinationPath = GameEditions.DestinationFor(_sourcePath, _currentEdition);

            // Survives the rename, so the last step can still name the library folder.
            _libraryAnchor = !string.IsNullOrEmpty(_sourcePath)
                ? _sourcePath
                : (onDisk != null ? onDisk.InstallDirectory : null);

            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    Close();
                }
            };

            ShowOpeningStep();
        }

        private void ShowOpeningStep()
        {
            if (_state.DualInstallReady)
            {
                ShowAlreadyReady();
                return;
            }

            if (string.IsNullOrEmpty(_sourcePath))
            {
                var renamed = GameEditions.All(_state)
                    .Any(install => GameEditions.HasBackedUpAppManifest(install.InstallDirectory));

                if (renamed)
                {
                    // The rename already happened on an earlier run: Steam has no manifest
                    // for the game, so all that is left is downloading the other build.
                    ShowDone(null);
                }
                else
                {
                    ShowBlocked(
                        "Игра не установлена через Steam",
                        "Лаунчер не нашёл установку Conan Exiles, которой управляет Steam.\n\n" +
                        "Настройка освобождает стандартную папку Steam под вторую версию игры, " +
                        "поэтому игра должна быть установлена через Steam. Установите её и откройте это окно снова.");
                }

                return;
            }

            if (GameEditions.IsLauncherInside(_sourcePath))
            {
                ShowBlocked(
                    "Лаунчер лежит внутри папки игры",
                    "Чтобы переименовать папку игры, лаунчер не должен находиться внутри неё.\n\n" +
                    "Перенесите лаунчер в другое место — например в C:\\RealmLauncher — и запустите его снова.");
                return;
            }

            if (GameEditions.IsGameRunning())
            {
                ShowBlocked(
                    "Conan Exiles запущен",
                    "Игра открыта, и папку с ней переименовать нельзя.\n\n" +
                    "Закройте Conan Exiles и откройте это окно снова.");
                return;
            }

            if (GameEditions.IsGameDownloading(_sourcePath))
            {
                ShowBlocked(
                    "Steam ещё качает игру",
                    "Steam сейчас загружает или проверяет файлы Conan Exiles и пишет в ту самую папку.\n\n" +
                    "Дождитесь окончания загрузки и откройте это окно снова.");
                return;
            }

            ShowIntro();
        }

        private void ShowBlocked(string title, string body)
        {
            _step = Step.Blocked;

            txtTitle.Text = title;
            txtBody.Text = body;
            pathsGrid.Visibility = Visibility.Collapsed;
            noteBorder.Visibility = Visibility.Collapsed;
            txtError.Visibility = Visibility.Collapsed;

            btnPrimary.Visibility = Visibility.Collapsed;
            btnSecondary.Content = "Закрыть";
        }

        private void ShowAlreadyReady()
        {
            _step = Step.AlreadyReady;

            txtTitle.Text = "Две версии уже установлены";
            txtBody.Text =
                "Enhanced и Legacy лежат на диске одновременно. Переключение в настройках " +
                "срабатывает сразу, без скачивания — лаунчер просто запускает нужную папку.";

            ShowPaths(
                "Enhanced:", PathOrDash(_state.Enhanced),
                "Legacy:", PathOrDash(_state.Legacy));

            noteBorder.Visibility = Visibility.Collapsed;
            txtError.Visibility = Visibility.Collapsed;

            btnPrimary.Visibility = Visibility.Collapsed;
            btnSecondary.Content = "Закрыть";
        }

        private void ShowIntro()
        {
            _step = Step.Intro;

            var current = GameEditions.DisplayName(_currentEdition);
            var other = GameEditions.DisplayName(GameEditions.Other(_currentEdition));

            txtTitle.Text = "Что это даёт";
            txtBody.Text =
                "Steam хранит только ту версию игры, которая выбрана в бета-ветке, поэтому " +
                "каждое переключение между Legacy и Enhanced — это полная перекачка игры.\n\n" +
                "Если держать обе версии на диске, переключение перестаёт что-либо качать: " +
                "лаунчер запускает нужную папку сам.\n\n" +
                "Сейчас установлена версия " + current + ". Лаунчер переименует её папку, " +
                "после чего версию " + other + " нужно будет один раз скачать через Steam.";

            ShowPaths(
                "Сейчас:", _sourcePath,
                "Станет:", _destinationPath);

            if (_currentEdition == GameEdition.Enhanced)
            {
                // Enhanced is the branch that keeps getting patches, so it should end up in
                // the folder Steam looks after.
                txtNote.Text =
                    "Советуем сначала переключиться в Steam на ветку " + GameEditions.LegacyBranchKey +
                    " и только потом " +
                    "запускать эту настройку. Enhanced получает обновления, и ей лучше остаться в " +
                    "стандартной папке Steam — иначе позже придётся возиться с папками вручную.";
                noteBorder.Visibility = Visibility.Visible;
            }
            else
            {
                noteBorder.Visibility = Visibility.Collapsed;
            }

            txtError.Visibility = Visibility.Collapsed;

            btnPrimary.Visibility = Visibility.Visible;
            btnPrimary.Content = "Начать";
            btnSecondary.Content = "Отмена";
        }

        private void ShowRename()
        {
            _step = Step.Rename;

            txtTitle.Text = "Переименование папки";
            txtBody.Text =
                "Папка с версией " + GameEditions.DisplayName(_currentEdition) + " переедет под новое имя, " +
                "а файл appmanifest Steam будет убран в .bak — иначе Steam попробует обновлять игру " +
                "по старому пути.";

            ShowPaths("Откуда:", _sourcePath, "Куда:", _destinationPath);

            noteBorder.Visibility = Visibility.Collapsed;
            txtError.Visibility = Visibility.Collapsed;

            btnPrimary.Visibility = Visibility.Visible;
            btnPrimary.Content = "Переименовать";
            btnSecondary.Content = "Отмена";
        }

        private void ShowDone(string warning)
        {
            _step = Step.Done;

            var other = GameEditions.DisplayName(GameEditions.Other(_currentEdition));
            var otherBranch = GameEditions.Other(_currentEdition) == GameEdition.Legacy
                ? "«" + GameEditions.LegacyBranchKey + "»"
                : "«Нет» (это и есть Enhanced)";

            // The manifest is gone, so once Steam re-reads its library the game turns up as
            // not installed: no "Installed Files" tab, no verify, just an Install button.
            var library = GameEditions.FindLibraryRoot(_libraryAnchor);
            var libraryHint = string.IsNullOrEmpty(library)
                ? "ту же папку библиотеки, где лежала игра"
                : "папку библиотеки " + library;

            txtTitle.Text = "Почти готово";
            txtBody.Text =
                "Папка с версией " + GameEditions.DisplayName(_currentEdition) + " переименована. " +
                "Осталось один раз скачать версию " + other + " через Steam.\n\n" +
                "Если Steam всё ещё считает Conan Exiles установленной:\n" +
                "1. Свойства Conan Exiles → «Бета-версии» → выберите " + otherBranch + ".\n" +
                "2. На вкладке «Установленные файлы» нажмите «Проверить целостность файлов».\n\n" +
                "Если Steam показывает игру как неустановленную — так будет после его перезапуска, " +
                "потому что файл appmanifest убран:\n" +
                "1. Нажмите «Установить» и выберите " + libraryHint + ".\n" +
                "2. Убедитесь, что в «Бета-версиях» выбрано " + otherBranch + ".\n\n" +
                "Дождитесь загрузки. После этого обе версии будут на диске, и переключение " +
                "в настройках станет мгновенным.";

            pathsGrid.Visibility = Visibility.Collapsed;

            if (string.IsNullOrEmpty(warning))
            {
                noteBorder.Visibility = Visibility.Collapsed;
            }
            else
            {
                txtNote.Text = warning;
                noteBorder.Visibility = Visibility.Visible;
            }

            txtError.Visibility = Visibility.Collapsed;

            btnPrimary.Visibility = Visibility.Visible;
            btnPrimary.Content = "Открыть Steam";
            btnSecondary.Content = "Закрыть";
        }

        private void ShowPaths(string firstLabel, string firstPath, string secondLabel, string secondPath)
        {
            txtFirstLabel.Text = firstLabel;
            txtFirstPath.Text = firstPath ?? "—";
            txtFirstPath.ToolTip = firstPath;

            txtSecondLabel.Text = secondLabel;
            txtSecondPath.Text = secondPath ?? "—";
            txtSecondPath.ToolTip = secondPath;

            pathsGrid.Visibility = Visibility.Visible;
        }

        private static string PathOrDash(EditionInstall install)
        {
            return install != null ? install.InstallDirectory : "не найдена";
        }

        private void BtnPrimary_OnClick(object sender, RoutedEventArgs e)
        {
            switch (_step)
            {
                case Step.Intro:
                    if (!string.IsNullOrEmpty(_destinationPath) && Directory.Exists(_destinationPath))
                    {
                        ShowDone(null);
                    }
                    else
                    {
                        ShowRename();
                    }
                    break;

                case Step.Rename:
                    Rename();
                    break;

                case Step.Done:
                    OpenSteamProperties();
                    break;
            }
        }

        private void Rename()
        {
            if (GameEditions.IsGameRunning())
            {
                ShowError("Conan Exiles всё ещё запущен. Закройте игру и повторите.");
                return;
            }

            if (GameEditions.IsGameDownloading(_sourcePath))
            {
                ShowError("Steam ещё качает Conan Exiles. Дождитесь окончания загрузки и повторите.");
                return;
            }

            btnPrimary.IsEnabled = false;
            try
            {
                var result = GameEditions.PrepareDualInstall(_sourcePath, _currentEdition, _log);

                if (!result.Renamed)
                {
                    ShowError(result.Error);
                    return;
                }

                _changed = true;
                ShowDone(result.Warning);
            }
            finally
            {
                btnPrimary.IsEnabled = true;
            }
        }

        private void OpenSteamProperties()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "steam://gameproperties/" + SteamLocator.ConanAppId,
                    UseShellExecute = true
                });

                if (_log != null)
                {
                    _log("Открыты свойства Conan Exiles в Steam для смены версии.");
                }
            }
            catch (Exception ex)
            {
                ShowError("Не удалось открыть Steam: " + ex.Message);
            }
        }

        private void ShowError(string message)
        {
            txtError.Text = message ?? "Не удалось выполнить операцию.";
            txtError.Visibility = Visibility.Visible;
        }

        private void BtnClose_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
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
    }
}
