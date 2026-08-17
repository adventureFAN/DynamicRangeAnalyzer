using DRAnalyzer.Core.Analysis;
using DRAnalyzer.Core.Metadata;

namespace DRAnalyzer.Tests;

[Trait("Category", "ExternalReference")]
public sealed class InvincibleMp3RegressionTests
{
    private static readonly IReadOnlyDictionary<int, int> ExpectedDr =
        new Dictionary<int, int>
        {
            [1] = 13,  // Unbreakable
            [2] = 13,  // Heartbreaker
            [3] = 13,  // Invicible / Invincible
            [4] = 12,  // Break of Dawn
            [5] = 12,  // Heaven Can Wait
            [6] = 14,  // You Rock My World
            [7] = 12,  // Butterflies
            [8] = 10,  // Speechless
            [9] = 12,  // 2000 Watts
            [10] = 12, // You Are My Life
            [11] = 14, // Privacy
            [12] = 13, // Don't Walk Away
            [13] = 11, // Cry
            [14] = 11, // The Lost Children
            [15] = 12, // Whatever Happens
            [16] = 13  // Threatened
        };

    [Fact]
    public void Mp3ReferenceAlbum_MatchesFooDrMeter108()
    {
        var albumDirectory =
            Environment.GetEnvironmentVariable(
                "DRANALYZER_REFERENCE_INVINCIBLE_MP3_DIR");

        Assert.False(
            string.IsNullOrWhiteSpace(albumDirectory),
            "DRANALYZER_REFERENCE_INVINCIBLE_MP3_DIR ist nicht gesetzt.");

        Assert.True(
            Directory.Exists(albumDirectory),
            $"Invincible-MP3-Referenzordner fehlt: {albumDirectory}");

        var requiredAlbumDirectory = albumDirectory!;

        var files =
            Directory
                .EnumerateFiles(
                    requiredAlbumDirectory,
                    "*.mp3",
                    SearchOption.AllDirectories)
                .ToArray();

        Assert.Equal(
            ExpectedDr.Count,
            files.Length);

        var actualTrackDr = new List<int>();
        var seenTracks = new HashSet<int>();

        foreach (var filePath in files)
        {
            var metadata =
                AudioMetadataReader.Read(filePath);

            Assert.True(
                int.TryParse(metadata.Track, out var trackNumber),
                $"Ungültige Tracknummer bei: {metadata.Title}");

            Assert.True(
                ExpectedDr.TryGetValue(
                    trackNumber,
                    out var expected),
                $"Unerwartete Tracknummer {trackNumber}: {metadata.Title}");

            var result =
                DynamicRangeAnalyzer.Analyze(filePath);

            Assert.Equal(
                expected,
                result.RoundedDynamicRange);

            actualTrackDr.Add(
                result.RoundedDynamicRange);

            seenTracks.Add(
                trackNumber);
        }

        Assert.Equal(
            ExpectedDr.Count,
            seenTracks.Count);

        foreach (var trackNumber in ExpectedDr.Keys)
        {
            Assert.Contains(
                trackNumber,
                seenTracks);
        }

        var albumDr =
            AlbumDynamicRangeCalculator.Calculate(
                actualTrackDr);

        Assert.Equal(12, albumDr);
    }
}
