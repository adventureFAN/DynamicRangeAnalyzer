using System.ComponentModel;
using System.IO;
using System.Text.Json;

namespace DRAnalyzer.App;

internal static class UserFacingErrorFormatter
{
    public static string ForFolderRead(Exception exception)
    {
        return exception switch
        {
            UnauthorizedAccessException =>
                "Access to one or more folders was denied.",

            DirectoryNotFoundException =>
                "The selected folder could not be found.",

            PathTooLongException =>
                "A path inside the selected folder is too long to read safely.",

            IOException =>
                "The folder could not be read completely because an I/O error occurred.",

            _ =>
                "The folder could not be read completely."
        };
    }

    public static string ForMetadata(Exception exception)
    {
        return exception switch
        {
            Win32Exception =>
                "FFprobe could not be started. Reinstall the application or make sure ffprobe is available on PATH.",

            TimeoutException or InvalidOperationException =>
                NormalizeOwnedMessage(
                    exception.Message,
                    "Metadata could not be read."),

            JsonException or FormatException or OverflowException =>
                "FFprobe returned invalid metadata information for this file.",

            FileNotFoundException =>
                "The file could not be found.",

            UnauthorizedAccessException =>
                "Access to the file was denied.",

            IOException =>
                "The file metadata could not be read because an I/O error occurred.",

            _ =>
                "The file metadata could not be read."
        };
    }

    public static string ForAnalysis(Exception exception)
    {
        return exception switch
        {
            Win32Exception =>
                "FFmpeg or ffprobe could not be started. Reinstall the application or make sure FFmpeg is available on PATH.",

            TimeoutException or InvalidOperationException =>
                NormalizeOwnedMessage(
                    exception.Message,
                    "The audio file could not be analyzed."),

            JsonException or FormatException or OverflowException =>
                "FFprobe returned invalid audio stream information for this file.",

            FileNotFoundException =>
                "The file could not be found.",

            UnauthorizedAccessException =>
                "Access to the file was denied.",

            IOException =>
                "The audio file could not be read because an I/O error occurred.",

            _ =>
                "The audio file could not be analyzed."
        };
    }

    public static string ForTagging(Exception exception)
    {
        return exception switch
        {
            FileNotFoundException =>
                "The file could not be found.",

            UnauthorizedAccessException =>
                "Access to the file was denied. The file was not intentionally modified.",

            NotSupportedException =>
                "This file uses a container or metadata variant that Dynamic Range Analyzer does not support for safe DR tag editing. No changes were made.",

            InvalidDataException =>
                "The file failed format-specific safety validation, so Dynamic Range Analyzer refused to modify it. No changes were made.",

            IOException =>
                "The file could not be modified. It may be in use, read-only, or unavailable.",

            ArgumentException =>
                "The DR tag operation received invalid data and was stopped before modifying the file.",

            InvalidOperationException =>
                "The DR tag operation could not be completed safely.",

            _ =>
                "The DR tag operation failed."
        };
    }

    public static string ForFatal(Exception exception)
    {
        return
            "An unexpected fatal error occurred. " +
            $"Error type: {exception.GetType().Name}.";
    }

    private static string NormalizeOwnedMessage(
        string message,
        string fallback)
    {
        return string.IsNullOrWhiteSpace(message)
            ? fallback
            : message.Trim();
    }
}
