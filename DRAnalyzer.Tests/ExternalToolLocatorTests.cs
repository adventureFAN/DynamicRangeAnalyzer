using DRAnalyzer.Core.Processes;

namespace DRAnalyzer.Tests;

public sealed class ExternalToolLocatorTests
{
    [Fact]
    public void ResolveFfmpeg_PrefersBundledRuntime()
    {
        var root = CreateTemporaryRoot();

        try
        {
            var executable =
                CreateBundledExecutable(
                    root,
                    "ffmpeg.exe");

            Assert.Equal(
                Path.GetFullPath(executable),
                ExternalToolLocator.ResolveFfmpeg(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveFfprobe_PrefersBundledRuntime()
    {
        var root = CreateTemporaryRoot();

        try
        {
            var executable =
                CreateBundledExecutable(
                    root,
                    "ffprobe.exe");

            Assert.Equal(
                Path.GetFullPath(executable),
                ExternalToolLocator.ResolveFfprobe(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveTools_FallBackToPathNamesWhenBundleIsMissing()
    {
        var root = CreateTemporaryRoot();

        try
        {
            Assert.Equal(
                "ffmpeg",
                ExternalToolLocator.ResolveFfmpeg(root));

            Assert.Equal(
                "ffprobe",
                ExternalToolLocator.ResolveFfprobe(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryRoot()
    {
        var root =
            Path.Combine(
                Path.GetTempPath(),
                "DRAnalyzer-ExternalToolLocatorTests-" +
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(root);
        return root;
    }

    private static string CreateBundledExecutable(
        string root,
        string fileName)
    {
        var directory =
            Path.Combine(
                root,
                "runtime",
                "ffmpeg");

        Directory.CreateDirectory(directory);

        var path =
            Path.Combine(
                directory,
                fileName);

        File.WriteAllBytes(path, [0]);
        return path;
    }
}
