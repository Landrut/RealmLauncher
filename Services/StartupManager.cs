using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace RealmLauncher.Services
{
    public static class StartupManager
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "RealmLauncher";
        public const string MinimizedArgument = "--minimized";

        private static string GetExecutablePath()
        {
            return Process.GetCurrentProcess().MainModule.FileName;
        }

        public static bool IsEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                {
                    return key?.GetValue(ValueName) is string;
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool TrySet(bool enabled, bool startMinimized, out string error)
        {
            error = null;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key == null)
                    {
                        error = "Не удалось открыть ветку автозапуска в реестре.";
                        return false;
                    }

                    if (!enabled)
                    {
                        if (key.GetValue(ValueName) != null)
                        {
                            key.DeleteValue(ValueName, false);
                        }
                        return true;
                    }

                    var exe = GetExecutablePath();
                    if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
                    {
                        error = "Не удалось определить путь к лаунчеру.";
                        return false;
                    }

                    var command = "\"" + exe + "\"";
                    if (startMinimized)
                    {
                        command += " " + MinimizedArgument;
                    }

                    key.SetValue(ValueName, command, RegistryValueKind.String);
                    return true;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
