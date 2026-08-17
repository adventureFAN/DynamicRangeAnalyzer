using DRAnalyzer.Core.Tagging;

namespace DRAnalyzer.Tests;

[Trait("Category", "ExternalReference")]
public sealed class ManualRealFlacTagWriterTests
{
    [Fact]
    public void WriteDrTags_ToExplicitRealFileCopy()
    {
        var filePath =
            Environment.GetEnvironmentVariable(
                "DRANALYZER_MANUAL_FLAC_COPY");

        Assert.False(
            string.IsNullOrWhiteSpace(filePath),
            "DRANALYZER_MANUAL_FLAC_COPY ist nicht gesetzt.");

        Assert.True(
            File.Exists(filePath),
            $"Testdatei wurde nicht gefunden: {filePath}");

        var requiredFilePath = filePath!;

        FlacDynamicRangeTagWriter.Write(
            requiredFilePath,
            trackDynamicRange: 20,
            albumDynamicRange: 21);
    }
}
