using DRAnalyzer.Core.Tagging;

namespace DRAnalyzer.Tests;

[Trait("Category", "ExternalReference")]
public sealed class ManualRealOpusTagWriterTests
{
    [Fact]
    public void WriteDrTags_ToExplicitRealOpusCopy()
    {
        var originalPath =
            Environment.GetEnvironmentVariable(
                "DRANALYZER_MANUAL_OPUS_ORIGINAL");

        var copyPath =
            Environment.GetEnvironmentVariable(
                "DRANALYZER_MANUAL_OPUS_COPY");

        Assert.False(
            string.IsNullOrWhiteSpace(originalPath),
            "DRANALYZER_MANUAL_OPUS_ORIGINAL ist nicht gesetzt.");

        Assert.False(
            string.IsNullOrWhiteSpace(copyPath),
            "DRANALYZER_MANUAL_OPUS_COPY ist nicht gesetzt.");

        Assert.True(
            File.Exists(originalPath),
            $"Originaldatei wurde nicht gefunden: {originalPath}");

        Assert.True(
            File.Exists(copyPath),
            $"Testkopie wurde nicht gefunden: {copyPath}");

        var originalFullPath =
            Path.GetFullPath(originalPath);

        var copyFullPath =
            Path.GetFullPath(copyPath);

        Assert.False(
            string.Equals(
                originalFullPath,
                copyFullPath,
                StringComparison.OrdinalIgnoreCase),
            "Original und Testkopie dürfen nicht dieselbe Datei sein.");

        Assert.Equal(
            ".opus",
            Path.GetExtension(copyFullPath),
            ignoreCase: true);

        OpusDynamicRangeTagWriter.Write(
            copyFullPath,
            trackDynamicRange: 20,
            albumDynamicRange: 21);
    }
}

