using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace RealmLauncher.Models
{
    public sealed class LauncherSettings
    {
        [JsonIgnore]
        public string ConfigUrl { get; set; }
        public string ConanExePath { get; set; }

        [JsonProperty("ServerPassword")]
        public string LegacyServerPassword { get; set; }

        public string EncryptedServerPassword { get; set; }
        public bool DisableCinematicIntro { get; set; }
        public bool AutomaticallySubscribeToWorkshopMods { get; set; }
        public bool BoostIngameLoading { get; set; }
        public string UiTheme { get; set; }

        private static readonly byte[] PasswordEntropy = Encoding.UTF8.GetBytes("RealmLauncher.ServerPassword.v1");

        public LauncherSettings()
        {
            AutomaticallySubscribeToWorkshopMods = true;
            BoostIngameLoading = true;
            UiTheme = "Bronze";
        }

        public static string SettingsFilePath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "launcher.settings.json"); }
        }

        public static string FallbackSettingsFilePath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RealmLauncher",
                    "launcher.settings.json");
            }
        }

        public static LauncherSettings Load()
        {
            return TryLoadFrom(SettingsFilePath)
                   ?? TryLoadFrom(FallbackSettingsFilePath)
                   ?? new LauncherSettings();
        }

        private static LauncherSettings TryLoadFrom(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                var json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<LauncherSettings>(json);
            }
            catch
            {
                return null;
            }
        }

        public void Save()
        {
            string error;
            TrySave(out error);
        }

        public bool TrySave(out string error)
        {
            LegacyServerPassword = null;
            var json = JsonConvert.SerializeObject(
                this,
                Formatting.Indented,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

            if (TryWrite(SettingsFilePath, json, out error))
            {
                return true;
            }

            return TryWrite(FallbackSettingsFilePath, json, out error);
        }

        private static bool TryWrite(string path, string json, out string error)
        {
            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(path, json);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public string GetServerPassword()
        {
            if (!string.IsNullOrWhiteSpace(EncryptedServerPassword))
            {
                try
                {
                    var protectedBytes = Convert.FromBase64String(EncryptedServerPassword);
                    var plainBytes = ProtectedData.Unprotect(protectedBytes, PasswordEntropy, DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(plainBytes);
                }
                catch
                {
                    return string.Empty;
                }
            }

            return LegacyServerPassword ?? string.Empty;
        }

        public void SetServerPassword(string password)
        {
            var value = password ?? string.Empty;
            var plainBytes = Encoding.UTF8.GetBytes(value);
            var protectedBytes = ProtectedData.Protect(plainBytes, PasswordEntropy, DataProtectionScope.CurrentUser);
            EncryptedServerPassword = Convert.ToBase64String(protectedBytes);
            LegacyServerPassword = null;
        }
    }
}
