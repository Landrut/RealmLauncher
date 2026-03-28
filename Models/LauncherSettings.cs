using System;
using System.IO;
using Newtonsoft.Json;

namespace RealmLauncher.Models
{
    public sealed class LauncherSettings
    {
        public string ConfigUrl { get; set; }
        public string ConanExePath { get; set; }
        public string ServerPassword { get; set; }
        public bool DisableCinematicIntro { get; set; }
        public bool AutomaticallySubscribeToWorkshopMods { get; set; }

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
            var json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(SettingsFilePath, json);
        }
    }
}
