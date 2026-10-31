using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace RealmLauncher.Services
{
    /// <summary>
    /// Keeps one launcher per install folder. Two copies of the same install race each
    /// other over launcher.settings.json, modlist.txt and the TotCustom presets, and the
    /// loser silently overwrites the winner. A copy in another folder still runs, so a
    /// test build next to the real one is not blocked.
    /// </summary>
    public static class SingleInstance
    {
        private static Mutex _mutex;
        private static EventWaitHandle _wakeUp;

        /// <summary>
        /// Returns false when another copy of this install is already running; that copy
        /// is asked to show itself and this one should quit.
        /// </summary>
        public static bool TryClaim(Action showExistingWindow)
        {
            var key = KeyForInstallFolder();

            try
            {
                bool created;
                _mutex = new Mutex(true, @"Local\RealmLauncher-" + key, out created);

                if (!created)
                {
                    // We asked for ownership and did not get it, so let go of the handle
                    // rather than releasing a mutex we never owned on the way out.
                    _mutex.Dispose();
                    _mutex = null;
                    SignalExisting(key);
                    return false;
                }

                if (showExistingWindow != null)
                {
                    ListenForSecondInstance(key, showExistingWindow);
                }

                return true;
            }
            catch (Exception)
            {
                // Never let the guard itself keep the launcher from starting.
                return true;
            }
        }

        public static void Release()
        {
            try
            {
                if (_mutex != null)
                {
                    _mutex.ReleaseMutex();
                    _mutex.Dispose();
                    _mutex = null;
                }
            }
            catch (ApplicationException)
            {
            }
            catch (ObjectDisposedException)
            {
            }

            try
            {
                if (_wakeUp != null)
                {
                    _wakeUp.Dispose();
                    _wakeUp = null;
                }
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private static void SignalExisting(string key)
        {
            try
            {
                EventWaitHandle handle;
                if (EventWaitHandle.TryOpenExisting(WakeName(key), out handle))
                {
                    using (handle)
                    {
                        handle.Set();
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        private static void ListenForSecondInstance(string key, Action showExistingWindow)
        {
            _wakeUp = new EventWaitHandle(false, EventResetMode.AutoReset, WakeName(key));

            var listener = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        _wakeUp.WaitOne();
                    }
                    catch (Exception)
                    {
                        return;
                    }

                    showExistingWindow();
                }
            });

            listener.IsBackground = true;
            listener.Name = "RealmLauncher.SingleInstance";
            listener.Start();
        }

        private static string WakeName(string key)
        {
            return @"Local\RealmLauncher-wake-" + key;
        }

        private static string KeyForInstallFolder()
        {
            var folder = (AppDomain.CurrentDomain.BaseDirectory ?? string.Empty)
                .TrimEnd('\\')
                .ToLowerInvariant();

            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(folder));
                var text = new StringBuilder(16);
                for (var i = 0; i < 8; i++)
                {
                    text.Append(hash[i].ToString("x2"));
                }

                return text.ToString();
            }
        }
    }
}
