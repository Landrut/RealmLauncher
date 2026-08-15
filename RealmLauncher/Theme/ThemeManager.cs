using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace RealmLauncher.Theme
{
    public static class ThemeManager
    {
        public const string DefaultThemeKey = "Bronze";

        private static readonly ThemeDefinition[] Themes =
        {
            new ThemeDefinition("Bronze", "Бронзовая", "Theme/Palette.Bronze.xaml", "#D9903F"),
            new ThemeDefinition("Blue", "Синяя", "Theme/Palette.Blue.xaml", "#3B82F6"),
            new ThemeDefinition("Amethyst", "Аметист", "Theme/Palette.Amethyst.xaml", "#8B5CF6")
        };

        public static IReadOnlyList<ThemeDefinition> Available
        {
            get { return Themes; }
        }

        public static string CurrentKey { get; private set; }

        public static string Normalize(string themeKey)
        {
            if (!string.IsNullOrWhiteSpace(themeKey))
            {
                var match = Themes.FirstOrDefault(
                    t => string.Equals(t.Key, themeKey.Trim(), StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    return match.Key;
                }
            }

            return DefaultThemeKey;
        }

        public static void Apply(string themeKey)
        {
            var key = Normalize(themeKey);
            if (string.Equals(key, CurrentKey, StringComparison.Ordinal))
            {
                return;
            }

            var app = Application.Current;
            if (app == null)
            {
                return;
            }

            var definition = Themes.First(t => string.Equals(t.Key, key, StringComparison.Ordinal));
            var palette = new ResourceDictionary
            {
                Source = new Uri(definition.SourcePath, UriKind.Relative)
            };

            var merged = app.Resources.MergedDictionaries;
            if (merged.Count == 0)
            {
                merged.Add(palette);
            }
            else
            {
                merged[0] = palette;
            }

            CurrentKey = key;
        }

        public sealed class ThemeDefinition
        {
            public ThemeDefinition(string key, string displayName, string sourcePath, string previewHex)
            {
                Key = key;
                DisplayName = displayName;
                SourcePath = sourcePath;
                PreviewBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(previewHex));
                PreviewBrush.Freeze();
            }

            public string Key { get; private set; }
            public string DisplayName { get; private set; }
            public string SourcePath { get; private set; }

            public SolidColorBrush PreviewBrush { get; private set; }

            public override string ToString()
            {
                return DisplayName;
            }
        }
    }
}
