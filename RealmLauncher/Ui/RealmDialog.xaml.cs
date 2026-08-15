using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace RealmLauncher.Ui
{
    public enum RealmDialogButtons
    {
        Ok,
        YesNo,

        Choice
    }

    public enum RealmDialogType
    {
        Info,
        Warning,
        Error,
        Question
    }

    public partial class RealmDialog : Window
    {
        private MessageBoxResult _result = MessageBoxResult.None;

        public static MessageBoxResult ShowChoice(
            Window owner, string title, string message, string primaryText, string secondaryText)
        {
            return Show(owner, title, message, RealmDialogButtons.Choice, RealmDialogType.Question,
                primaryText, secondaryText);
        }

        public static MessageBoxResult Show(
            Window owner,
            string title,
            string message,
            RealmDialogButtons buttons,
            RealmDialogType type,
            string primaryText = null,
            string secondaryText = null)
        {
            var dialog = new RealmDialog(title, message, buttons, type, primaryText, secondaryText);

            if (owner != null && owner.IsLoaded && owner.IsVisible)
            {
                dialog.Owner = owner;
            }
            else
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            dialog.ShowDialog();
            return dialog._result;
        }

        private readonly RealmDialogButtons _buttons;

        private RealmDialog(
            string title,
            string message,
            RealmDialogButtons buttons,
            RealmDialogType type,
            string primaryText,
            string secondaryText)
        {
            InitializeComponent();

            _buttons = buttons;
            txtTitle.Text = string.IsNullOrWhiteSpace(title) ? "REALM RolePlay Launcher" : title;
            txtMessage.Text = message ?? string.Empty;

            ApplyDialogType(type);
            ApplyButtons(buttons, primaryText, secondaryText);

            KeyDown += RealmDialog_KeyDown;
        }

        private MessageBoxResult DismissResult
        {
            get
            {
                if (_buttons == RealmDialogButtons.YesNo)
                {
                    return MessageBoxResult.No;
                }

                return MessageBoxResult.Cancel;
            }
        }

        private void RealmDialog_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                _result = DismissResult;
                Close();
            }
        }

        private void ApplyButtons(RealmDialogButtons buttons, string primaryText, string secondaryText)
        {
            var twoActions = buttons == RealmDialogButtons.YesNo || buttons == RealmDialogButtons.Choice;

            btnOk.Visibility = twoActions ? Visibility.Collapsed : Visibility.Visible;
            btnOk.IsDefault = !twoActions;

            btnYes.Visibility = twoActions ? Visibility.Visible : Visibility.Collapsed;
            btnYes.IsDefault = twoActions;

            btnNo.Visibility = twoActions ? Visibility.Visible : Visibility.Collapsed;
            btnNo.IsCancel = buttons == RealmDialogButtons.YesNo;

            if (!string.IsNullOrWhiteSpace(primaryText))
            {
                btnYes.Content = primaryText;
            }

            if (!string.IsNullOrWhiteSpace(secondaryText))
            {
                btnNo.Content = secondaryText;
            }

            Loaded += (s, e) =>
            {
                if (twoActions)
                {
                    btnYes.Focus();
                }
                else
                {
                    btnOk.Focus();
                }
            };
        }

        private void ApplyDialogType(RealmDialogType type)
        {
            string accentKey;
            string glyph;

            switch (type)
            {
                case RealmDialogType.Warning:
                    accentKey = "WarningBrush";
                    glyph = "!";
                    break;
                case RealmDialogType.Error:
                    accentKey = "DangerBrush";
                    glyph = "!";
                    break;
                case RealmDialogType.Question:
                    accentKey = "AccentBrush";
                    glyph = "?";
                    break;
                default:
                    accentKey = "AccentBrush";
                    glyph = "i";
                    break;
            }

            txtIcon.Text = glyph;

            var accent = TryFindResource(accentKey) as SolidColorBrush;
            if (accent == null)
            {
                return;
            }

            iconCircle.BorderBrush = accent;
            txtIcon.Foreground = accent;

            var tint = accent.Color;
            tint.A = 0x33;
            iconCircle.Background = new SolidColorBrush(tint);
        }

        private void BtnClose_OnClick(object sender, RoutedEventArgs e)
        {
            _result = DismissResult;
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
            catch (System.InvalidOperationException)
            {
            }
        }

        private void BtnOk_OnClick(object sender, RoutedEventArgs e)
        {
            _result = MessageBoxResult.OK;
            Close();
        }

        private void BtnYes_OnClick(object sender, RoutedEventArgs e)
        {
            _result = MessageBoxResult.Yes;
            Close();
        }

        private void BtnNo_OnClick(object sender, RoutedEventArgs e)
        {
            _result = MessageBoxResult.No;
            Close();
        }
    }
}
