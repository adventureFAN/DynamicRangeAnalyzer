using DRAnalyzer.Core.Analysis;
using DRAnalyzer.Core.Metadata;

namespace DRAnalyzer.Tests;

[Trait("Category", "ExternalReference")]
public sealed class Tool10000DaysRegressionTests
{
    private static readonly IReadOnlyDictionary<string, int> ExpectedDr =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Vicarious"] = 7,
            ["Right In Two"] = 7,
            ["Viginti Tres"] = 12,
            ["Jambi"] = 7,
            ["Wings For Marie (Part 1)"] = 8,
            ["10,000 Days (Wings Part 2)"] = 8,
            ["The Pot"] = 7,
            ["Lipan Conjuring"] = 10,
            ["Lost Keys (Blame Hofmann)"] = 11,
            ["Rosetta Stoned"] = 7,
            ["Intension"] = 8
        };

    [Fact]
    public void OpusReferenceAlbum_MatchesFooDrMeter108()
    {
        var albumDirectory =
            Environment.GetEnvironmentVariable(
                "DRANALYZER_REFERENCE_TOOL_DIR");

        Assert.False(
            string.IsNullOrWhiteSpace(albumDirectory),
            "DRANALYZER_REFERENCE_TOOL_DIR ist nicht gesetzt.");

        Assert.True(
            Directory.Exists(albumDirectory),
            $"Tool-Referenzordner fehlt: {albumDirectory}");

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

        Assert.Equal(8, albumDr);
    }
}

