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
            var dialog = new RealmDialog(title, message, buttons, type)
            {
                Owner = owner
            };

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
            ApplyCompactSize(message);

            KeyDown += RealmDialog_KeyDown;
        }

        private void ApplyCompactSize(string message)
        {
            var text = message ?? string.Empty;
            var lineCount = text.Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries).Length;
            if (lineCount <= 0) lineCount = 1;

            var estimated = 220 + (lineCount * 10);
            if (estimated < 220) estimated = 220;
            if (estimated > 340) estimated = 340;

            Height = estimated;
        }

        private void RealmDialog_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                _result = MessageBoxResult.Cancel;
                Close();
            }
        }

        private void ApplyButtons(RealmDialogButtons buttons)
        {
            if (buttons == RealmDialogButtons.YesNo)
            {
                btnOk.Visibility = Visibility.Collapsed;
                btnYes.Visibility = Visibility.Visible;
                btnNo.Visibility = Visibility.Visible;
                btnYes.Focus();
            }
            else
            {
                btnOk.Visibility = Visibility.Visible;
                btnYes.Visibility = Visibility.Collapsed;
                btnNo.Visibility = Visibility.Collapsed;
                btnOk.Focus();
            }
        }

        private void ApplyDialogType(RealmDialogType type)
        {
            switch (type)
            {
                case RealmDialogType.Warning:
                    iconCircle.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9A6A16"));
                    iconCircle.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD37A"));
                    txtIcon.Text = "!";
                    break;
                case RealmDialogType.Error:
                    iconCircle.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8C2C33"));
                    iconCircle.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF97A1"));
                    txtIcon.Text = "×";
                    break;
                case RealmDialogType.Question:
                    iconCircle.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2E66C4"));
                    iconCircle.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#88B6FF"));
                    txtIcon.Text = "?";
                    break;
                default:
                    iconCircle.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2E66C4"));
                    iconCircle.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#88B6FF"));
                    txtIcon.Text = "i";
                    break;
            }
        }

        private void BtnClose_OnClick(object sender, RoutedEventArgs e)
        {
            _result = MessageBoxResult.Cancel;
            Close();
        }

        private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
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
