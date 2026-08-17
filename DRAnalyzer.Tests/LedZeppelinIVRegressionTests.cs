using DRAnalyzer.Core.Analysis;
using DRAnalyzer.Core.Metadata;

namespace DRAnalyzer.Tests;

[Trait("Category", "ExternalReference")]
public sealed class LedZeppelinIVRegressionTests
{
    private static readonly IReadOnlyDictionary<string, int> ExpectedDr =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Black Dog"] = 13,
            ["Rock And Roll"] = 11,
            ["The Battle Of Evermore"] = 12,
            ["Stairway To Heaven"] = 13,
            ["Misty Mountain Hop"] = 11,
            ["Four Sticks"] = 12,
            ["Going To California"] = 11,
            ["When The Levee Breaks"] = 11
        };

    [Fact]
    public void OpusReferenceAlbum_MatchesFooDrMeter108()
    {
        var albumDirectory =
            Environment.GetEnvironmentVariable(
                "DRANALYZER_REFERENCE_LEDZEPPELIN4_DIR");

        Assert.False(
            string.IsNullOrWhiteSpace(albumDirectory),
            "DRANALYZER_REFERENCE_LEDZEPPELIN4_DIR ist nicht gesetzt.");

        Assert.True(
            Directory.Exists(albumDirectory),
            $"Led-Zeppelin-IV-Referenzordner fehlt: {albumDirectory}");

        var requiredAlbumDirectory = albumDirectory!;

        var files =
            Directory
                .EnumerateFiles(
                    requiredAlbumDirectory,
                    "*.opus",
                    SearchOption.AllDirectories)
                .ToArray();

        Assert.Equal(
            ExpectedDr.Count,
            files.Length);

        var actualTrackDr =
            new List<int>();

        var seenTitles =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var filePath in files)
        {
            var metadata =
                AudioMetadataReader.Read(filePath);

            Assert.True(
                ExpectedDr.TryGetValue(
                    metadata.Title,
                    out var expected),
                $"Unerwarteter Track: {metadata.Title}");

            var result =
                DynamicRangeAnalyzer.Analyze(filePath);

            Assert.Equal(
                expected,
                result.RoundedDynamicRange);

            actualTrackDr.Add(
                result.RoundedDynamicRange);

            seenTitles.Add(
                metadata.Title);
        }

        Assert.Equal(
            ExpectedDr.Count,
            seenTitles.Count);

        foreach (var title in ExpectedDr.Keys)
        {
            Assert.Contains(
                title,
                seenTitles);
        }

        var albumDr =
            AlbumDynamicRangeCalculator.Calculate(
                actualTrackDr);

        Assert.Equal(12, albumDr);
    }
}

