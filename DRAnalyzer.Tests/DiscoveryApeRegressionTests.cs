using DRAnalyzer.Core.Analysis;
using DRAnalyzer.Core.Metadata;

namespace DRAnalyzer.Tests;

[Trait("Category", "ExternalReference")]
public sealed class DiscoveryApeRegressionTests
{
    private static readonly IReadOnlyDictionary<string, int> ExpectedDr =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["One More Time"] = 14,
            ["Aerodynamic"] = 13,
            ["Digital Love"] = 12,
            ["Harder, Better, Faster, Stronger"] = 14,
            ["Crescendolls"] = 14,
            ["Nightvision"] = 13,
            ["Superheroes"] = 11,
            ["High Life"] = 13,
            ["Something About Us"] = 13,
            ["Voyager"] = 14,
            ["Veridis Quo"] = 14,
            ["Short Circuit"] = 14,
            ["Face To Face"] = 13,
            ["Too Long"] = 14
        };

    [Fact]
    public void ApeReferenceAlbum_MatchesFooDrMeter108()
    {
        var albumDirectory =
            Environment.GetEnvironmentVariable(
                "DRANALYZER_REFERENCE_DISCOVERY_APE_DIR");

        Assert.False(
            string.IsNullOrWhiteSpace(albumDirectory),
            "DRANALYZER_REFERENCE_DISCOVERY_APE_DIR ist nicht gesetzt.");

        Assert.True(
            Directory.Exists(albumDirectory),
            $"Discovery-APE-Referenzordner fehlt: {albumDirectory}");

        var requiredAlbumDirectory = albumDirectory!;

        var files =
            Directory
                .EnumerateFiles(
                    requiredAlbumDirectory,
                    "*.ape",
                    SearchOption.AllDirectories)
                .ToArray();

        Assert.Equal(ExpectedDr.Count, files.Length);

        var actualTrackDr = new List<int>();
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
            Assert.Contains(title, seenTitles);
        }

        var albumDr =
            AlbumDynamicRangeCalculator.Calculate(
                actualTrackDr);

        Assert.Equal(13, albumDr);
    }
}
