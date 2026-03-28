using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RealmLauncher.Models;

namespace RealmLauncher.Services
{
    public sealed class LauncherUpdateService
    {
        private static readonly HttpClient HttpClient = new HttpClient();

        public LauncherUpdateService()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        public async Task<LauncherUpdateCheckResult> CheckForUpdatesAsync(
            string manifestUrl,
            Version currentVersion,
            ISet<string> allowedHosts,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(manifestUrl))
            {
                return new LauncherUpdateCheckResult
                {
                    IsUpdateAvailable = false,
                    CurrentVersion = currentVersion
                };
            }

            var manifestUri = UrlSecurity.RequireAllowedHttpsUrl(manifestUrl, allowedHosts, "URL манифеста обновлений");
            using (var response = await HttpClient.GetAsync(manifestUri, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var manifest = JsonConvert.DeserializeObject<LauncherUpdateManifest>(json);
                if (manifest == null)
                {
                    throw new InvalidOperationException("Не удалось разобрать JSON манифеста обновлений лаунчера.");
                }

                if (string.IsNullOrWhiteSpace(manifest.Version))
                {
                    throw new InvalidOperationException("В манифесте обновлений отсутствует поле version.");
                }

                if (string.IsNullOrWhiteSpace(manifest.DownloadUrl))
                {
                    throw new InvalidOperationException("В манифесте обновлений отсутствует поле downloadUrl.");
                }

                UrlSecurity.RequireAllowedHttpsUrl(manifest.DownloadUrl, allowedHosts, "URL пакета обновления");

                var latestVersion = ParseVersionLoose(manifest.Version);
                return new LauncherUpdateCheckResult
                {
                    IsUpdateAvailable = latestVersion > currentVersion,
                    CurrentVersion = currentVersion,
                    LatestVersion = latestVersion,
                    Manifest = manifest
                };
            }
        }

        public async Task<string> DownloadPackageAsync(
            LauncherUpdateManifest manifest,
            ISet<string> allowedHosts,
            Action<long, long?> progressBytes,
            CancellationToken cancellationToken)
        {
            if (manifest == null || string.IsNullOrWhiteSpace(manifest.DownloadUrl))
            {
                throw new InvalidOperationException("Некорректный манифест обновления.");
            }

            var packageUri = UrlSecurity.RequireAllowedHttpsUrl(manifest.DownloadUrl, allowedHosts, "URL пакета обновления");
            var tempRoot = Path.Combine(Path.GetTempPath(), "RealmLauncherUpdater");
            Directory.CreateDirectory(tempRoot);
            var packagePath = Path.Combine(tempRoot, "update.zip");

            using (var response = await HttpClient.GetAsync(packageUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength;
                using (var src = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var dst = new FileStream(packagePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[64 * 1024];
                    long readTotal = 0;
                    while (true)
                    {
                        var read = await src.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                        if (read <= 0)
                        {
                            break;
                        }

                        await dst.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                        readTotal += read;
                        if (progressBytes != null)
                        {
                            progressBytes(readTotal, totalBytes);
                        }
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(manifest.Sha256))
            {
                var downloadedHash = ComputeSha256ForFile(packagePath);
                if (!string.Equals(downloadedHash, manifest.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Хэш скачанного обновления не совпал с манифестом (SHA-256).");
                }
            }

            return packagePath;
        }

        public void InstallAndRestart(string packagePath)
        {
            if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
            {
                throw new InvalidOperationException("Файл пакета обновления не найден.");
            }

            var tempRoot = Path.Combine(Path.GetTempPath(), "RealmLauncherUpdater");
            var extractDir = Path.Combine(tempRoot, "extracted");
            if (Directory.Exists(extractDir))
            {
                Directory.Delete(extractDir, true);
            }
            Directory.CreateDirectory(extractDir);
            ZipFile.ExtractToDirectory(packagePath, extractDir);

            var currentExe = Process.GetCurrentProcess().MainModule != null
                ? Process.GetCurrentProcess().MainModule.FileName
                : null;
            if (string.IsNullOrWhiteSpace(currentExe) || !File.Exists(currentExe))
            {
                throw new InvalidOperationException("Не удалось определить текущий exe лаунчера.");
            }

            var appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            var scriptPath = Path.Combine(tempRoot, "apply_update.cmd");
            var pid = Process.GetCurrentProcess().Id;

            var script = new StringBuilder();
            script.AppendLine("@echo off");
            script.AppendLine("setlocal");
            script.AppendLine(":waitloop");
            script.AppendLine(string.Format("tasklist /FI \"PID eq {0}\" | find \"{0}\" >nul", pid));
            script.AppendLine("if not errorlevel 1 (");
            script.AppendLine("  timeout /t 1 /nobreak >nul");
            script.AppendLine("  goto waitloop");
            script.AppendLine(")");
            script.AppendLine(string.Format("xcopy /E /Y /I \"{0}\\*\" \"{1}\\\" >nul", extractDir, appDir));
            script.AppendLine(string.Format("start \"\" \"{0}\"", currentExe));
            script.AppendLine("endlocal");
            File.WriteAllText(scriptPath, script.ToString(), Encoding.ASCII);

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c \"" + scriptPath + "\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }

        private static Version ParseVersionLoose(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new Version(0, 0, 0, 0);
            }

            var clean = new string(raw.Trim().TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
            Version version;
            if (!Version.TryParse(clean, out version))
            {
                throw new InvalidOperationException("Некорректная версия в манифесте обновлений: " + raw);
            }

            return version;
        }

        private static string ComputeSha256ForFile(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                var hash = sha.ComputeHash(stream);
                var sb = new StringBuilder(hash.Length * 2);
                for (var i = 0; i < hash.Length; i++)
                {
                    sb.Append(hash[i].ToString("x2"));
                }
                return sb.ToString();
            }
        }
    }
}
