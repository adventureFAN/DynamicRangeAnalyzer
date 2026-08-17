using System.Diagnostics;
using System.Text;
using DRAnalyzer.Core.Metadata;

namespace DRAnalyzer.Tests;

public sealed class UnicodeMetadataRegressionTests
{
    [Fact]
    public void InternationalUnicodeMetadata_RoundTripsThroughFfmpegAndFfprobe()
    {
        var tempFile =
            Path.Combine(
                Path.GetTempPath(),
                $"dranalyzer-unicode-{Guid.NewGuid():N}.flac");

        const string artist =
            "Björk – Official髭男dism – Москва – العربية – 中文 – 🎵";

        const string album =
            "Café naïve – 日本語 – Ελληνικά";

        const string title =
            "Straße – ミックスナッツ – 한국어 – עברית";

        const string genre =
            "Électronique – 音楽";

        const string composer =
            "François – 山田太郎";

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("-hide_banner");
            startInfo.ArgumentList.Add("-loglevel");
            startInfo.ArgumentList.Add("error");

            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add("lavfi");

            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add("anullsrc=r=48000:cl=stereo");

            startInfo.ArgumentList.Add("-t");
            startInfo.ArgumentList.Add("0.1");

            startInfo.ArgumentList.Add("-c:a");
            startInfo.ArgumentList.Add("flac");

            AddMetadata(startInfo, "artist", artist);
            AddMetadata(startInfo, "album", album);
            AddMetadata(startInfo, "title", title);
            AddMetadata(startInfo, "genre", genre);
            AddMetadata(startInfo, "composer", composer);
            AddMetadata(startInfo, "track", "01/12");

            startInfo.ArgumentList.Add("-y");
            startInfo.ArgumentList.Add(tempFile);

            using var process =
                Process.Start(startInfo)
                ?? throw new InvalidOperationException(
                    "FFmpeg konnte nicht gestartet werden.");

            var error =
                process.StandardError.ReadToEnd();

            process.WaitForExit();

            Assert.True(
                process.ExitCode == 0,
                $"FFmpeg konnte die Testdatei nicht erzeugen: {error}");

            var metadata =
                AudioMetadataReader.Read(tempFile);

            Assert.Equal(artist, metadata.Artist);
            Assert.Equal(album, metadata.Album);
            Assert.Equal(title, metadata.Title);
            Assert.Equal(genre, metadata.Genre);
            Assert.Equal(composer, metadata.Composer);

            Assert.Equal("1", metadata.Track);
            Assert.Equal("12", metadata.TrackTotal);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    private static void AddMetadata(
        ProcessStartInfo startInfo,
        string name,
        string value)
    {
        startInfo.ArgumentList.Add("-metadata");
        startInfo.ArgumentList.Add($"{name}={value}");
    }
}
