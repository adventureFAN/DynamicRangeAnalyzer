using DRAnalyzer.Core.Tagging;

namespace DRAnalyzer.Tests;

[Trait("Category", "ExternalReference")]
public sealed class OggPageCodecTests
{
    [Fact]
    public void RealOpus_AllPagesHaveValidChecksums()
    {
        var filePath =
            Environment.GetEnvironmentVariable(
                "DRANALYZER_MANUAL_OPUS_FILE");

        Assert.False(
            string.IsNullOrWhiteSpace(filePath),
            "DRANALYZER_MANUAL_OPUS_FILE ist nicht gesetzt.");

        Assert.True(
            File.Exists(filePath),
            $"Opus-Datei fehlt: {filePath}");

        var requiredFilePath = filePath!;

        using var stream =
            new FileStream(
                requiredFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

        var pageIndex = 0;

        while (true)
        {
            var page =
                OggPageCodec.ReadRawPage(
                    stream);

            if (page is null)
                break;

            Assert.True(
                OggPageCodec.HasValidChecksum(
                    page),
                $"Ogg-Seite {pageIndex} besitzt " +
                "keine gültige CRC.");

            var rebuilt =
                OggPageCodec
                    .WithRecalculatedChecksum(
                        page);

            Assert.True(
                page
                    .AsSpan()
                    .SequenceEqual(rebuilt),
                $"Ogg-Seite {pageIndex} ist nach " +
                "identischer CRC-Neuberechnung nicht bytegleich.");

            pageIndex++;
        }

        Assert.True(
            pageIndex >= 3,
            "Es wurden unerwartet wenige Ogg-Seiten gefunden.");

        Console.WriteLine(
            $"Geprüfte Ogg-Seiten: {pageIndex}");
    }

    [Fact]
    public void Checksum_DetectsMutation_AndCanBeRebuilt()
    {
        var filePath =
            Environment.GetEnvironmentVariable(
                "DRANALYZER_MANUAL_OPUS_FILE");

        Assert.False(
            string.IsNullOrWhiteSpace(filePath),
            "DRANALYZER_MANUAL_OPUS_FILE ist nicht gesetzt.");

        Assert.True(
            File.Exists(filePath),
            $"Opus-Datei fehlt: {filePath}");

        var requiredFilePath = filePath!;

        using var stream =
            new FileStream(
                requiredFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

        var original =
            OggPageCodec.ReadRawPage(
                stream);

        Assert.NotNull(original);

        Assert.True(
            OggPageCodec.HasValidChecksum(
                original));

        var corrupted =
            original.ToArray();

        corrupted[^1] ^=
            0x01;

        Assert.False(
            OggPageCodec.HasValidChecksum(
                corrupted),
            "Die absichtlich veränderte Ogg-Seite " +
            "wurde nicht als beschädigt erkannt.");

        var repaired =
            OggPageCodec
                .WithRecalculatedChecksum(
                    corrupted);

        Assert.True(
            OggPageCodec.HasValidChecksum(
                repaired));

        Assert.False(
            original
                .AsSpan()
                .SequenceEqual(repaired),
            "Die manipulierte Nutzlast wurde " +
            "unerwartet wieder zum Original.");
    }
}
