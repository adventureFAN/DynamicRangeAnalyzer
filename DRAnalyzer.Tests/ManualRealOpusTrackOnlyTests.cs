using System.Buffers.Binary;
using System.Text;
using DRAnalyzer.Core.Tagging;

namespace DRAnalyzer.Tests;

[Trait("Category", "ExternalReference")]
public sealed class ManualRealOpusTrackOnlyTests
{
    [Fact]
    public void WriteTrackOnly_PreservesExistingAlbumDrByteExactly()
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
            File.Exists(originalPath));

        Assert.True(
            File.Exists(copyPath));

        var originalAlbumDr =
            GetSingleRawField(
                ReadOpusTagsPacket(originalPath),
                "ALBUM DYNAMIC RANGE");

        Assert.NotNull(
            originalAlbumDr);

        OpusDynamicRangeTagWriter.Write(
            copyPath,
            trackDynamicRange: 20,
            albumDynamicRange: null);

        var modifiedTags =
            ReadOpusTagsPacket(
                copyPath);

        var modifiedTrackDr =
            GetSingleRawField(
                modifiedTags,
                "DYNAMIC RANGE");

        var modifiedAlbumDr =
            GetSingleRawField(
                modifiedTags,
                "ALBUM DYNAMIC RANGE");

        Assert.NotNull(
            modifiedTrackDr);

        Assert.NotNull(
            modifiedAlbumDr);

        Assert.Equal(
            "20",
            GetValue(
                modifiedTrackDr));

        Assert.True(
            originalAlbumDr
                .AsSpan()
                .SequenceEqual(
                    modifiedAlbumDr),
            "Der vorhandene Album-DR-Tag wurde " +
            "beim Track-only-Schreiben verändert.");
    }

    private static byte[] ReadOpusTagsPacket(
        string filePath)
    {
        using var stream =
            new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

        // OpusHead
        var firstPage =
            OggPageCodec.ReadRawPage(
                stream);

        Assert.NotNull(
            firstPage);

        var packet =
            new List<byte>();

        var pageIndex = 0;

        while (true)
        {
            var page =
                OggPageCodec.ReadRawPage(
                    stream);

            Assert.NotNull(
                page);

            Assert.True(
                OggPageCodec.HasValidChecksum(
                    page));

            var segmentCount =
                page[26];

            var lacing =
                page.AsSpan(
                        27,
                        segmentCount)
                    .ToArray();

            var bodyOffset =
                27 +
                segmentCount;

            var body =
                page.AsSpan(
                        bodyOffset)
                    .ToArray();

            var offset = 0;

            foreach (var segmentLength
                     in lacing)
            {
                packet.AddRange(
                    body.AsSpan(
                            offset,
                            segmentLength)
                        .ToArray());

                offset +=
                    segmentLength;

                if (segmentLength < 255)
                {
                    var result =
                        packet.ToArray();

                    Assert.True(
                        result.AsSpan()
                            .StartsWith("OpusTags"u8));

                    return result;
                }
            }

            pageIndex++;

            Assert.True(
                pageIndex < 1000,
                "Unplausibel großer OpusTags-Header.");
        }
    }

    private static byte[]? GetSingleRawField(
        byte[] packet,
        string fieldName)
    {
        var comments =
            ParseComments(
                packet);

        var matches =
            comments
                .Where(
                    comment =>
                        IsField(
                            comment,
                            fieldName))
                .ToArray();

        if (matches.Length == 0)
            return null;

        Assert.Single(
            matches);

        return matches[0];
    }

    private static IReadOnlyList<byte[]> ParseComments(
        byte[] packet)
    {
        Assert.True(
            packet.AsSpan()
                .StartsWith("OpusTags"u8));

        var offset = 8;

        var vendorLength =
            ReadUInt32(
                packet,
                ref offset);

        offset +=
            checked((int)vendorLength);

        var commentCount =
            ReadUInt32(
                packet,
                ref offset);

        var comments =
            new List<byte[]>();

        for (uint index = 0;
             index < commentCount;
             index++)
        {
            var length =
                ReadUInt32(
                    packet,
                    ref offset);

            var intLength =
                checked((int)length);

            comments.Add(
                packet.AsSpan(
                        offset,
                        intLength)
                    .ToArray());

            offset +=
                intLength;
        }

        return comments;
    }

    private static bool IsField(
        byte[] comment,
        string fieldName)
    {
        var equalsIndex =
            Array.IndexOf(
                comment,
                (byte)'=');

        if (equalsIndex <= 0)
            return false;

        var actual =
            Encoding.ASCII.GetString(
                comment,
                0,
                equalsIndex);

        return string.Equals(
            actual,
            fieldName,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string GetValue(
        byte[] comment)
    {
        var equalsIndex =
            Array.IndexOf(
                comment,
                (byte)'=');

        Assert.True(
            equalsIndex >= 0);

        return Encoding.UTF8.GetString(
            comment,
            equalsIndex + 1,
            comment.Length - equalsIndex - 1);
    }

    private static uint ReadUInt32(
        byte[] data,
        ref int offset)
    {
        var value =
            BinaryPrimitives.ReadUInt32LittleEndian(
                data.AsSpan(
                    offset,
                    4));

        offset += 4;

        return value;
    }
}
