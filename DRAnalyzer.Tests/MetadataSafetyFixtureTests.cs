using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace DRAnalyzer.Tests;

public sealed class MetadataSafetyFixtureTests
{
    [Fact]
    public void ReferenceFile_ContainsMetadataAndEmbeddedArtwork()
    {
        var tempDir =
            Path.Combine(
                Path.GetTempPath(),
                $"dranalyzer-metadata-{Guid.NewGuid():N}");

        Directory.CreateDirectory(tempDir);

        var coverPath =
            Path.Combine(tempDir, "cover.png");

        var audioPath =
            Path.Combine(tempDir, "metadata-reference.flac");

        try
        {
            CreateCover(coverPath);
            CreateReferenceFlac(audioPath, coverPath);

            using var document =
                RunFfprobe(audioPath);

            var root = document.RootElement;

            Assert.True(
                root.TryGetProperty("format", out var format));

            Assert.True(
                format.TryGetProperty("tags", out var formatTags));

            var tags =
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (var property in formatTags.EnumerateObject())
            {
                tags[property.Name] =
                    property.Value.ToString();
            }

            AssertTag(tags, "artist",
                "Björk – Official髭男dism – Москва");

            AssertTag(tags, "album",
                "Café – 日本語 – العربية");

            AssertTag(tags, "title",
                "Straße – ミックスナッツ – 한국어");

            AssertTag(tags, "genre",
                "Électronique");

            AssertTag(tags, "comment",
                "DRAnalyzer metadata safety test");

            AssertTag(tags, "replaygain_track_gain",
                "-5.25 dB");

            AssertTag(tags, "custom_test",
                "DO NOT TOUCH");

            AssertTag(tags, "dynamic range",
                "7");

            AssertTag(tags, "album dynamic range",
                "8");

            Assert.True(
                root.TryGetProperty(
                    "streams",
                    out var streams));

            var hasArtwork = false;

            foreach (var stream in streams.EnumerateArray())
            {
                if (!stream.TryGetProperty(
                        "codec_type",
                        out var codecType))
                {
                    continue;
                }

                if (codecType.GetString() != "video")
                    continue;

                if (!stream.TryGetProperty(
                        "disposition",
                        out var disposition))
                {
                    continue;
                }

                if (disposition.TryGetProperty(
                        "attached_pic",
                        out var attachedPic) &&
                    attachedPic.GetInt32() == 1)
                {
                    hasArtwork = true;
                    break;
                }
            }

            Assert.True(
                hasArtwork,
                "Das eingebettete Cover wurde nicht gefunden.");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(
                    tempDir,
                    recursive: true);
            }
        }
    }

    private static void CreateCover(
        string outputPath)
    {
        RunFfmpeg(
            "-f", "lavfi",
            "-i", "testsrc=size=64x64:rate=1",
            "-frames:v", "1",
            "-y",
            outputPath);
    }

    private static void CreateReferenceFlac(
        string outputPath,
        string coverPath)
    {
        RunFfmpeg(
            "-f", "lavfi",
            "-i", "anullsrc=r=48000:cl=stereo",

            "-i", coverPath,

            "-t", "1",

            "-map", "0:a:0",
            "-map", "1:v:0",

            "-c:a", "flac",
            "-c:v", "png",

            "-disposition:v:0", "attached_pic",

            "-metadata",
            "artist=Björk – Official髭男dism – Москва",

            "-metadata",
            "album=Café – 日本語 – العربية",

            "-metadata",
            "title=Straße – ミックスナッツ – 한국어",

            "-metadata",
            "genre=Électronique",

            "-metadata",
            "comment=DRAnalyzer metadata safety test",

            "-metadata",
            "replaygain_track_gain=-5.25 dB",

            "-metadata",
            "custom_test=DO NOT TOUCH",

            "-metadata",
            "dynamic range=7",

            "-metadata",
            "album dynamic range=8",

            "-y",
            outputPath);
    }

    private static void AssertTag(
        IReadOnlyDictionary<string, string> tags,
        string name,
        string expected)
    {
        Assert.True(
            tags.TryGetValue(name, out var actual),
            $"Tag fehlt: {name}");

        Assert.Equal(expected, actual);
    }

    private static void RunFfmpeg(
        params string[] arguments)
    {
        var startInfo =
            CreateProcessInfo("ffmpeg");

        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-loglevel");
        startInfo.ArgumentList.Add("error");

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process =
            Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "FFmpeg konnte nicht gestartet werden.");

        var error =
            process.StandardError.ReadToEnd();

        process.WaitForExit();

        Assert.True(
            process.ExitCode == 0,
            $"FFmpeg-Fehler: {error}");
    }

    private static JsonDocument RunFfprobe(
        string filePath)
    {
        var startInfo =
            CreateProcessInfo("ffprobe");

        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("error");

        startInfo.ArgumentList.Add("-show_streams");
        startInfo.ArgumentList.Add("-show_format");

        startInfo.ArgumentList.Add("-of");
        startInfo.ArgumentList.Add("json");

        startInfo.ArgumentList.Add(filePath);

        using var process =
            Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "FFprobe konnte nicht gestartet werden.");

        var output =
            process.StandardOutput.ReadToEnd();

        var error =
            process.StandardError.ReadToEnd();

        process.WaitForExit();

        Assert.True(
            process.ExitCode == 0,
            $"FFprobe-Fehler: {error}");

        return JsonDocument.Parse(output);
    }

    private static ProcessStartInfo CreateProcessInfo(
        string fileName)
    {
        return new ProcessStartInfo
        {
            FileName = fileName,

            RedirectStandardOutput = true,
            RedirectStandardError = true,

            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,

            UseShellExecute = false,
            CreateNoWindow = true
        };
    }
}
