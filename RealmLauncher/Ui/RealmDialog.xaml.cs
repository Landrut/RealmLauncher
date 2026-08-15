using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace RealmLauncher.Ui
{
    public enum RealmDialogButtons
    {
        Ok,
        YesNo
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

        public static MessageBoxResult Show(Window owner, string title, string message, RealmDialogButtons buttons, RealmDialogType type)
        {
            var dialog = new RealmDialog(title, message, buttons, type);

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

        private RealmDialog(string title, string message, RealmDialogButtons buttons, RealmDialogType type)
        {
            InitializeComponent();

            txtTitle.Text = string.IsNullOrWhiteSpace(title) ? "REALM RolePlay Launcher" : title;
            txtMessage.Text = message ?? string.Empty;

            ApplyDialogType(type);
            ApplyButtons(buttons);

            KeyDown += RealmDialog_KeyDown;
        }

        private void RealmDialog_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                _result = btnYes.Visibility == Visibility.Visible ? MessageBoxResult.No : MessageBoxResult.Cancel;
                Close();
            }
        }

        private void ApplyButtons(RealmDialogButtons buttons)
        {
            var yesNo = buttons == RealmDialogButtons.YesNo;

            btnOk.Visibility = yesNo ? Visibility.Collapsed : Visibility.Visible;
            btnOk.IsDefault = !yesNo;

            btnYes.Visibility = yesNo ? Visibility.Visible : Visibility.Collapsed;
            btnYes.IsDefault = yesNo;

            btnNo.Visibility = yesNo ? Visibility.Visible : Visibility.Collapsed;
            btnNo.IsCancel = yesNo;

            Loaded += (s, e) =>
            {
                if (yesNo)
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
            _result = btnYes.Visibility == Visibility.Visible ? MessageBoxResult.No : MessageBoxResult.Cancel;
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
