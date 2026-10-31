using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace RealmLauncher.Services
{
    /// <summary>
    /// Writes the launcher journal to disk so a player can hand over something readable
    /// after the window is gone. Logging must never be the thing that breaks the app,
    /// so every call here swallows its own failures.
    /// </summary>
    public static class FileLog
    {
        private static readonly object Sync = new object();
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        private const int KeptDays = 7;
        private const long MaxFileBytes = 4 * 1024 * 1024;

        private static string _path;
        private static bool _failed;

        public static string Directory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RealmLauncher",
                    "logs");
            }
        }

        /// <summary>Path of the file being written, or null while nothing has been logged yet.</summary>
        public static string CurrentPath
        {
            get { lock (Sync) { return _path; } }
        }

        public static void Start()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                Write("=== Запуск лаунчера ===");
                Write("Версия: " + assembly.GetName().Version);
                Write("Папка: " + AppDomain.CurrentDomain.BaseDirectory);
                Write("Windows: " + Environment.OSVersion.VersionString + ", 64-bit: " + Environment.Is64BitOperatingSystem);
                Write(".NET: " + Environment.Version);
            }
            catch
            {
            }
        }

        public static void Write(string line)
        {
            if (_failed)
            {
                return;
            }

            try
            {
                lock (Sync)
                {
                    var path = EnsurePath();
                    if (path == null)
                    {
                        return;
                    }

                    var stamped = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:yyyy-MM-dd HH:mm:ss} {1}{2}",
                        DateTime.Now,
                        line,
                        Environment.NewLine);

                    File.AppendAllText(path, stamped, Utf8NoBom);
                }
            }
            catch
            {
                // A locked or full disk must not take the launcher down with it.
                _failed = true;
            }
        }

        public static void WriteException(string context, Exception ex)
        {
            if (ex == null)
            {
                Write(context);
                return;
            }

            var text = new StringBuilder();
            text.Append(context).Append(": ").Append(ex.GetType().FullName).Append(": ").Append(ex.Message);

            var inner = ex.InnerException;
            var depth = 0;
            while (inner != null && depth < 5)
            {
                text.AppendLine();
                text.Append("  ← ").Append(inner.GetType().FullName).Append(": ").Append(inner.Message);
                inner = inner.InnerException;
                depth++;
            }

            if (!string.IsNullOrEmpty(ex.StackTrace))
            {
                text.AppendLine();
                text.Append(ex.StackTrace);
            }

            Write(text.ToString());
        }

        private static string EnsurePath()
        {
            if (_path != null && File.Exists(_path))
            {
                RollIfTooLarge();
                return _path;
            }

            var directory = Directory;
            System.IO.Directory.CreateDirectory(directory);
            _path = Path.Combine(
                directory,
                "launcher-" + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".log");

            Prune(directory);
            return _path;
        }

        private static void RollIfTooLarge()
        {
            try
            {
                var file = new FileInfo(_path);
                if (file.Length < MaxFileBytes)
                {
                    return;
                }

                // Keep the tail: the end of a long session is what explains a problem.
                var kept = File.ReadAllLines(_path, Utf8NoBom);
                File.WriteAllLines(_path, kept.Skip(kept.Length / 2), Utf8NoBom);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static void Prune(string directory)
        {
            try
            {
                var old = new DirectoryInfo(directory)
                    .GetFiles("launcher-*.log")
                    .OrderByDescending(file => file.Name, StringComparer.Ordinal)
                    .Skip(KeptDays);

                foreach (var file in old)
                {
                    file.Delete();
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
