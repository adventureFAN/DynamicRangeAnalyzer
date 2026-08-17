using System.IO;

namespace DRAnalyzer.App.Settings;

public enum AppThemePreference
{
    System,
    Light,
    Dark
}

public static class AppThemeSettings
{
    private static readonly string SettingsDirectory =
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "DRAnalyzer");

    private static readonly string ThemeFilePath =
        Path.Combine(
            SettingsDirectory,
            "theme.txt");

    public static AppThemePreference Load()
    {
        try
        {
            if (!File.Exists(ThemeFilePath))
                return AppThemePreference.System;

            var value =
                File.ReadAllText(
                    ThemeFilePath)
                    .Trim();

            return Enum.TryParse<AppThemePreference>(
                value,
                ignoreCase: true,
                out var preference)
                ? preference
                : AppThemePreference.System;
        }
        catch
        {
            return AppThemePreference.System;
        }
    }

    public static void Save(
        AppThemePreference preference)
    {
        try
        {
            Directory.CreateDirectory(
                SettingsDirectory);

            File.WriteAllText(
                ThemeFilePath,
                preference.ToString());
        }
        catch
        {
            // Theme persistence must never prevent
            // the application from starting.
        }
    }
}
