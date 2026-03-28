using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace RealmLauncher.Models
{
    public sealed class LauncherSettings
    {
        public string ConfigUrl { get; set; }
        public string ConanExePath { get; set; }

        [JsonProperty("ServerPassword")]
        public string LegacyServerPassword { get; set; }

        public string EncryptedServerPassword { get; set; }
        public bool DisableCinematicIntro { get; set; }
        public bool AutomaticallySubscribeToWorkshopMods { get; set; }

        private static readonly byte[] PasswordEntropy = Encoding.UTF8.GetBytes("RealmLauncher.ServerPassword.v1");

        public LauncherSettings()
        {
            AutomaticallySubscribeToWorkshopMods = true;
        }

        public static string SettingsFilePath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "launcher.settings.json"); }
        }

        public static LauncherSettings Load()
        {
            if (!File.Exists(SettingsFilePath))
            {
                return new LauncherSettings();
            }

            var json = File.ReadAllText(SettingsFilePath);
            return JsonConvert.DeserializeObject<LauncherSettings>(json) ?? new LauncherSettings();
        }

        public void Save()
        {
            LegacyServerPassword = null;
            var json = JsonConvert.SerializeObject(
                this,
                Formatting.Indented,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            File.WriteAllText(SettingsFilePath, json);
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
