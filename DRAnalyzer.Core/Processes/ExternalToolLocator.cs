namespace DRAnalyzer.Core.Processes;

public static class ExternalToolLocator
{
    private const string RuntimeFolder = "runtime";
    private const string FfmpegFolder = "ffmpeg";

    public static string ResolveFfmpeg(
        string? baseDirectory = null)
    {
        return Resolve(
            "ffmpeg.exe",
            "ffmpeg",
            baseDirectory);
    }

    public static string ResolveFfprobe(
        string? baseDirectory = null)
    {
        return Resolve(
            "ffprobe.exe",
            "ffprobe",
            baseDirectory);
    }

    private static string Resolve(
        string bundledExecutableName,
        string pathFallback,
        string? baseDirectory)
    {
        var root =
            string.IsNullOrWhiteSpace(baseDirectory)
                ? AppContext.BaseDirectory
                : baseDirectory;

        var bundledPath =
            Path.GetFullPath(
                Path.Combine(
                    root,
                    RuntimeFolder,
                    FfmpegFolder,
                    bundledExecutableName));

        return File.Exists(bundledPath)
            ? bundledPath
            : pathFallback;
    }
}
