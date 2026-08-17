using System.Security.Cryptography;
using DRAnalyzer.Core.Analysis;
using DRAnalyzer.Core.Metadata;
using DRAnalyzer.Core.Tagging;

namespace DRAnalyzer.Tests;

[Trait("Category", "ExternalReference")]
public sealed class ManualRealOpusAlbumBatchTests
{
    private static readonly IReadOnlyDictionary<string, int> ExpectedDr =
        new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase)
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
    public void LedZeppelinIV_CopyBatch_AnalyzesWritesAndReadsBackSafely()
    {
        var originalDirectory =
            Environment.GetEnvironmentVariable(
                "DRANALYZER_MANUAL_OPUS_ALBUM_ORIGINAL");

        var copyDirectory =
            Environment.GetEnvironmentVariable(
                "DRANALYZER_MANUAL_OPUS_ALBUM_COPY");

        Assert.False(
            string.IsNullOrWhiteSpace(
                originalDirectory),
            "DRANALYZER_MANUAL_OPUS_ALBUM_ORIGINAL ist nicht gesetzt.");

        Assert.False(
            string.IsNullOrWhiteSpace(
                copyDirectory),
            "DRANALYZER_MANUAL_OPUS_ALBUM_COPY ist nicht gesetzt.");

        Assert.True(
            Directory.Exists(
                originalDirectory),
            $"Originalalbum fehlt: {originalDirectory}");

        Assert.True(
            Directory.Exists(
                copyDirectory),
            $"Testkopie fehlt: {copyDirectory}");

        Assert.False(
            string.Equals(
                Path.GetFullPath(
                    originalDirectory),
                Path.GetFullPath(
                    copyDirectory),
                StringComparison.OrdinalIgnoreCase),
            "Original- und Testordner dürfen nicht identisch sein.");

        var originalFiles =
            Directory
                .EnumerateFiles(
                    originalDirectory,
                    "*.opus",
                    SearchOption.TopDirectoryOnly)
                .OrderBy(
                    path =>
                        Path.GetFileName(path),
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        var copyFiles =
            Directory
                .EnumerateFiles(
                    copyDirectory,
                    "*.opus",
                    SearchOption.TopDirectoryOnly)
                .OrderBy(
                    path =>
                        Path.GetFileName(path),
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        Assert.Equal(
            ExpectedDr.Count,
            originalFiles.Length);

        Assert.Equal(
            ExpectedDr.Count,
            copyFiles.Length);

        var originalsByName =
            originalFiles.ToDictionary(
                path =>
                    Path.GetFileName(path)
                    ?? throw new InvalidDataException(
                        $"Dateiname konnte nicht ermittelt werden: {path}"),
                StringComparer.OrdinalIgnoreCase);

        var copiesByName =
            copyFiles.ToDictionary(
                path =>
                    Path.GetFileName(path)
                    ?? throw new InvalidDataException(
                        $"Dateiname konnte nicht ermittelt werden: {path}"),
                StringComparer.OrdinalIgnoreCase);

        Assert.Equal(
            originalsByName.Count,
            copiesByName.Count);

        var originalHashes =
            originalsByName.ToDictionary(
                pair =>
                    pair.Key,
                pair =>
                    CalculateSha256(
                        pair.Value),
                StringComparer.OrdinalIgnoreCase);

        foreach (var pair in originalsByName)
        {
            Assert.True(
                copiesByName.TryGetValue(
                    pair.Key,
                    out var copyPath),
                $"Testkopie fehlt: {pair.Key}");

            Assert.Equal(
                originalHashes[pair.Key],
                CalculateSha256(copyPath));
        }

        var tracks =
            new List<TrackState>();

        var trackDrValues =
            new List<int>();

        // ----------------------------------------------------
        // 1. Alle acht Testkopien analysieren.
        // ----------------------------------------------------

        foreach (var copyPath in copyFiles)
        {
            var metadata =
                AudioMetadataReader.Read(
                    copyPath);

            Assert.True(
                ExpectedDr.TryGetValue(
                    metadata.Title,
                    out var expectedDr),
                $"Unerwarteter Track: {metadata.Title}");

            var result =
                DynamicRangeAnalyzer.Analyze(
                    copyPath);

            Assert.Equal(
                expectedDr,
                result.RoundedDynamicRange);

            trackDrValues.Add(
                result.RoundedDynamicRange);

            tracks.Add(
                new TrackState(
                    copyPath,
                    metadata.Title,
                    expectedDr,
                    GetProtectedTags(
                        metadata.Tags)));
        }

        Assert.Equal(
            ExpectedDr.Count,
            tracks.Count);

        var albumDr =
            AlbumDynamicRangeCalculator.Calculate(
                trackDrValues);

        Assert.Equal(
            12,
            albumDr);

        Console.WriteLine(
            $"Berechneter Album DR: {albumDr}");

        // ----------------------------------------------------
        // 2. Alle acht Kopien beschreiben.
        // ----------------------------------------------------

        foreach (var track in tracks)
        {
            OpusDynamicRangeTagWriter.Write(
                track.FilePath,
                track.ExpectedDr,
                albumDr);
        }

        // ----------------------------------------------------
        // 3. Tags erneut einlesen UND erneut analysieren.
        //
        // Damit muss jede geschriebene Datei anschließend
        // wieder vollständig durch ffprobe + FFmpeg laufen.
        // ----------------------------------------------------

        foreach (var track in tracks)
        {
            var metadataAfter =
                AudioMetadataReader.Read(
                    track.FilePath);

            Assert.Equal(
                track.ExpectedDr.ToString(),
                metadataAfter.DynamicRange);

            Assert.Equal(
                albumDr.ToString(),
                metadataAfter.AlbumDynamicRange);

            AssertProtectedTagsEqual(
                track.ProtectedTagsBefore,
                GetProtectedTags(
                    metadataAfter.Tags),
                track.Title);

            var resultAfter =
                DynamicRangeAnalyzer.Analyze(
                    track.FilePath);

            Assert.Equal(
                track.ExpectedDr,
                resultAfter.RoundedDynamicRange);

            Console.WriteLine(
                $"{track.Title}: " +
                $"DR{track.ExpectedDr} / " +
                $"Album DR{albumDr} OK");
        }

        // ----------------------------------------------------
        // 4. Originalalbum muss vollständig unangetastet sein.
        // ----------------------------------------------------

        foreach (var pair in originalsByName)
        {
            var hashAfter =
                CalculateSha256(
                    pair.Value);

            Assert.Equal(
                originalHashes[pair.Key],
                hashAfter);
        }

        Console.WriteLine(
            $"Album-Batch: {tracks.Count}/{tracks.Count} Dateien OK");

        Console.WriteLine(
            "Alle Originaldateien SHA-256-identisch.");

        Console.WriteLine(
            "Alle geschriebenen Kopien erneut erfolgreich analysiert.");
    }

    private static IReadOnlyDictionary<string, string>
        GetProtectedTags(
            IReadOnlyDictionary<string, string> tags)
    {
        return tags
            .Where(
                pair =>
                    !IsOwnedField(
                        pair.Key))
            .ToDictionary(
                pair =>
                    pair.Key,
                pair =>
                    pair.Value,
                StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsOwnedField(
        string fieldName)
    {
        return
            string.Equals(
                fieldName,
                "DYNAMIC RANGE",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                fieldName,
                "ALBUM DYNAMIC RANGE",
                StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertProtectedTagsEqual(
        IReadOnlyDictionary<string, string> before,
        IReadOnlyDictionary<string, string> after,
        string title)
    {
        Assert.Equal(
            before.Count,
            after.Count);

        foreach (var pair in before)
        {
            Assert.True(
                after.TryGetValue(
                    pair.Key,
                    out var afterValue),
                $"{title}: geschützter Tag fehlt: {pair.Key}");

            Assert.Equal(
                pair.Value,
                afterValue);
        }
    }

    private static string CalculateSha256(
        string filePath)
    {
        using var stream =
            File.OpenRead(
                filePath);

        return Convert.ToHexString(
            SHA256.HashData(
                stream));
    }

    private sealed record TrackState(
        string FilePath,
        string Title,
        int ExpectedDr,
        IReadOnlyDictionary<string, string> ProtectedTagsBefore);
}

