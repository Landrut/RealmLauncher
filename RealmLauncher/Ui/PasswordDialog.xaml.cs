using System.Windows;
using System.Windows.Input;

namespace RealmLauncher.Ui
{
    public partial class PasswordDialog : Window
    {
        private string _password;

        private PasswordDialog(string error)
        {
            InitializeComponent();

            if (!string.IsNullOrWhiteSpace(error))
            {
                txtError.Text = error;
                txtError.Visibility = Visibility.Visible;
            }

            Loaded += (s, e) => txtPassword.Focus();
            KeyDown += PasswordDialog_KeyDown;
        }

        public static bool TryAsk(Window owner, string error, out string password)
        {
            var dialog = new PasswordDialog(error);

            if (owner != null && owner.IsLoaded && owner.IsVisible)
            {
                dialog.Owner = owner;
            }
            else
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            dialog.ShowDialog();

            password = dialog._password;
            return password != null;
        }

        private void PasswordDialog_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        }

        private void BtnOk_OnClick(object sender, RoutedEventArgs e)
        {
            _password = txtPassword.Password ?? string.Empty;
            Close();
        }

        private void BtnCancel_OnClick(object sender, RoutedEventArgs e)
        {
            _password = null;
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
    }
}
