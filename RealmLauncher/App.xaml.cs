using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using RealmLauncher.Services;
using RealmLauncher.Ui;

namespace RealmLauncher
{
    public partial class App : Application
    {
        private bool _crashReported;
        private bool _started;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            if (!SingleInstance.TryClaim(ShowExistingWindow))
            {
                // Another copy of this install is already up and has been asked to show itself.
                FileLog.Write("Копия лаунчера из этой папки уже запущена — показываю её окно и выхожу.");
                Shutdown();
                return;
            }

            _started = true;
            FileLog.Start();

            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            new MainWindow().Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_started)
            {
                FileLog.Write("=== Лаунчер закрыт (код " + e.ApplicationExitCode + ") ===");
            }

            SingleInstance.Release();
            base.OnExit(e);
        }

        private void ShowExistingWindow()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var window = MainWindow;
                if (window == null)
                {
                    return;
                }

                if (window.WindowState == WindowState.Minimized)
                {
                    window.WindowState = WindowState.Normal;
                }

                window.Show();
                window.Activate();
                window.Topmost = true;
                window.Topmost = false;
            }));
        }

        // A failed image decode or a null in an event handler should not end the session,
        // so the launcher keeps running and the details go to the log either way.
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            FileLog.WriteException("НЕОБРАБОТАННОЕ ИСКЛЮЧЕНИЕ (интерфейс)", e.Exception);
            e.Handled = true;
            ReportCrashOnce(e.Exception);
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            FileLog.WriteException("НЕОБРАБОТАННОЕ ИСКЛЮЧЕНИЕ (процесс)", e.ExceptionObject as Exception);
            FileLog.Write(e.IsTerminating
                ? "Процесс завершается после этой ошибки."
                : "Процесс продолжает работу.");
        }

        private void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            FileLog.WriteException("НЕОБРАБОТАННОЕ ИСКЛЮЧЕНИЕ (фоновая задача)", e.Exception);
            e.SetObserved();
        }

        /// <summary>
        /// One dialog per session: a loop that throws on every redraw must not bury the
        /// player under identical windows.
        /// </summary>
        private void ReportCrashOnce(Exception ex)
        {
            if (_crashReported)
            {
                return;
            }

            _crashReported = true;

            try
            {
                var path = FileLog.CurrentPath;
                RealmDialog.Show(
                    MainWindow,
                    "Ошибка лаунчера",
                    "В лаунчере произошла ошибка:\n\n" + ex.Message +
                    "\n\nЛаунчер продолжит работу, но что-то могло не сработать. " +
                    "Подробности записаны в журнал:\n" + (path ?? FileLog.Directory) +
                    "\n\nЕсли проблема повторяется, пришлите этот файл в Discord.",
                    RealmDialogButtons.Ok,
                    RealmDialogType.Error);
            }
            catch (Exception)
            {
            }
        }
    }
}
