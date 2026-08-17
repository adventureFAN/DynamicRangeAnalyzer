using System.Globalization;
using System.IO;

namespace DRAnalyzer.App.Settings;

public sealed record AppFontPreference(
    string FontFamilyName,
    double FontSizeDip);

public static class AppFontSettings
{
    private const double MinimumFontSizeDip = 12.0;
    private const double MaximumFontSizeDip = 24.0;

    private static readonly string SettingsDirectory =
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "DRAnalyzer");

    private static readonly string FontFilePath =
        Path.Combine(
            SettingsDirectory,
            "font.txt");

    public static AppFontPreference? Load()
    {
        try
        {
            if (!File.Exists(FontFilePath))
                return null;

            var lines =
                File.ReadAllLines(
                    FontFilePath);

            if (lines.Length < 2 ||
                string.IsNullOrWhiteSpace(lines[0]))
            {
                return null;
            }

            if (!double.TryParse(
                    lines[1],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var fontSizeDip))
            {
                return null;
            }

            if (fontSizeDip < MinimumFontSizeDip ||
                fontSizeDip > MaximumFontSizeDip)
            {
                return null;
            }

            return new AppFontPreference(
                lines[0].Trim(),
                fontSizeDip);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(
        string fontFamilyName,
        double fontSizeDip)
    {
        if (string.IsNullOrWhiteSpace(fontFamilyName))
            return;

        if (fontSizeDip < MinimumFontSizeDip ||
            fontSizeDip > MaximumFontSizeDip)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(
                SettingsDirectory);

            File.WriteAllLines(
                FontFilePath,
                new[]
                {
                    fontFamilyName,
                    fontSizeDip.ToString(
                        "R",
                        CultureInfo.InvariantCulture)
                });
        }
        catch
        {
            // Font persistence must never prevent the app from working.
        }
    }

    public static void Reset()
    {
        try
        {
            if (File.Exists(FontFilePath))
            {
                File.Delete(
                    FontFilePath);
            }
        }
        catch
        {
            // Reset failure must never prevent the app from working.
        }
    }
}
