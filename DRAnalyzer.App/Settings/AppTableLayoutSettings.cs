using System.IO;
using System.Text.Json;

namespace DRAnalyzer.App.Settings;

public sealed record AppTableColumnLayout(
    string Key,
    int DisplayIndex,
    double WidthValue,
    string WidthUnit);

public sealed record AppTableLayoutPreference(
    List<AppTableColumnLayout> Columns);

public static class AppTableLayoutSettings
{
    private static readonly string SettingsDirectory =
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "DRAnalyzer");

    private static readonly string LayoutFilePath =
        Path.Combine(
            SettingsDirectory,
            "table-layout.json");

    public static AppTableLayoutPreference? Load()
    {
        try
        {
            if (!File.Exists(LayoutFilePath))
                return null;

            var json =
                File.ReadAllText(
                    LayoutFilePath);

            var preference =
                JsonSerializer.Deserialize<AppTableLayoutPreference>(
                    json);

            return preference?.Columns is { Count: > 0 }
                ? preference
                : null;
        }
        catch
        {
            return null;
        }
    }

    public static void Save(
        AppTableLayoutPreference preference)
    {
        try
        {
            Directory.CreateDirectory(
                SettingsDirectory);

            var json =
                JsonSerializer.Serialize(
                    preference,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

            var temporaryPath =
                LayoutFilePath + ".tmp";

            File.WriteAllText(
                temporaryPath,
                json);

            File.Move(
                temporaryPath,
                LayoutFilePath,
                overwrite: true);
        }
        catch
        {
            // Table layout persistence must never prevent the app from working.
        }
    }

    public static void Reset()
    {
        try
        {
            if (File.Exists(LayoutFilePath))
            {
                File.Delete(
                    LayoutFilePath);
            }

            var temporaryPath =
                LayoutFilePath + ".tmp";

            if (File.Exists(temporaryPath))
            {
                File.Delete(
                    temporaryPath);
            }
        }
        catch
        {
            // Reset failure must never prevent the app from working.
        }
    }
}
