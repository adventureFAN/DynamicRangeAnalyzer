using System.Buffers.Binary;
using DRAnalyzer.Core.Tagging;

namespace DRAnalyzer.Tests;

public sealed class OggOpusCommentPageBuilderTests
{
    [Theory]
    [InlineData(100)]
    [InlineData(255)]
    [InlineData(4096)]
    [InlineData(65025)]
    [InlineData(70000)]
    public void Build_RoundTripsPacket(
        int packetLength)
    {
        var packet =
            CreatePacket(
                packetLength);

        const uint serial =
            0x12345678;

        const uint firstSequence =
            17;

        var pages =
            OggOpusCommentPageBuilder.Build(
                packet,
                serial,
                firstSequence);

        Assert.NotEmpty(
            pages);

        var rebuilt =
            new List<byte>();

        for (var index = 0;
             index < pages.Count;
             index++)
        {
            var page =
                pages[index];

            Assert.True(
                OggPageCodec
                    .HasValidChecksum(page),
                $"Seite {index} hat eine ungültige CRC.");

            Assert.True(
                page.AsSpan(0, 4)
                    .SequenceEqual("OggS"u8));

            Assert.Equal(
                0,
                page[4]);

            var expectedFlags =
                index == 0
                    ? (byte)0x00
                    : (byte)0x01;

            Assert.Equal(
                expectedFlags,
                page[5]);

            var granule =
                BinaryPrimitives
                    .ReadInt64LittleEndian(
                        page.AsSpan(
                            6,
                            8));

            Assert.Equal(
                index == pages.Count - 1
                    ? 0L
                    : -1L,
                granule);

            Assert.Equal(
                serial,
                BinaryPrimitives
                    .ReadUInt32LittleEndian(
                        page.AsSpan(
                            14,
                            4)));

            Assert.Equal(
                firstSequence +
                (uint)index,
                BinaryPrimitives
                    .ReadUInt32LittleEndian(
                        page.AsSpan(
                            18,
                            4)));

            var segmentCount =
                page[26];

            Assert.InRange(
                segmentCount,
                (byte)1,
                (byte)255);

            var lacing =
                page.AsSpan(
                    27,
                    segmentCount);

            if (index <
                pages.Count - 1)
            {
                // Das Paket läuft weiter:
                // letzter Lacing-Wert muss 255 sein.
                Assert.Equal(
                    (byte)255,
                    lacing[^1]);
            }
            else
            {
                // Paket endet auf dieser Seite.
                Assert.True(
                    lacing[^1] < 255);
            }

            var bodyOffset =
                27 +
                segmentCount;

            var bodyLength =
                lacing.ToArray()
                    .Sum(
                        value => (int)value);

            rebuilt.AddRange(
                page.AsSpan(
                        bodyOffset,
                        bodyLength)
                    .ToArray());
        }

        Assert.True(
            packet
                .AsSpan()
                .SequenceEqual(
                    rebuilt.ToArray()),
            "Das Paket wurde nach dem Paging " +
            "nicht bytegenau rekonstruiert.");
    }

    [Fact]
    public void ExactFullPagePacket_GetsZeroLengthTerminatorPage()
    {
        // 255 Segmente * 255 Byte.
        //
        // Damit ist die erste Ogg-Seite komplett
        // mit 255er-Lacing-Werten gefüllt.
        // Zum Beenden des Pakets ist danach
        // noch ein 0-Lacing-Wert nötig.
        var packet =
            CreatePacket(
                255 * 255);

        var pages =
            OggOpusCommentPageBuilder.Build(
                packet,
                streamSerial: 1234,
                firstPageSequence: 8);

        Assert.Equal(
            2,
            pages.Count);

        var first =
            pages[0];

        Assert.Equal(
            (byte)255,
            first[26]);

        var firstLacing =
            first.AsSpan(
                27,
                255);

        Assert.All(
            firstLacing.ToArray(),
            value =>
                Assert.Equal(
                    (byte)255,
                    value));

        Assert.Equal(
            -1L,
            BinaryPrimitives
                .ReadInt64LittleEndian(
                    first.AsSpan(
                        6,
                        8)));

        var second =
            pages[1];

        Assert.Equal(
            (byte)0x01,
            second[5]);

        Assert.Equal(
            (byte)1,
            second[26]);

        Assert.Equal(
            (byte)0,
            second[27]);

        Assert.Equal(
            28,
            second.Length);

        Assert.Equal(
            0L,
            BinaryPrimitives
                .ReadInt64LittleEndian(
                    second.AsSpan(
                        6,
                        8)));

        Assert.True(
            OggPageCodec
                .HasValidChecksum(first));

        Assert.True(
            OggPageCodec
                .HasValidChecksum(second));
    }

    [Fact]
    public void Build_RejectsNonOpusTagsPacket()
    {
        var packet =
            new byte[100];

        Assert.Throws<InvalidDataException>(
            () =>
                OggOpusCommentPageBuilder.Build(
                    packet,
                    streamSerial: 1,
                    firstPageSequence: 1));
    }

    private static byte[] CreatePacket(
        int length)
    {
        Assert.True(
            length >= 8);

        var packet =
            new byte[length];

        "OpusTags"u8.CopyTo(
            packet.AsSpan(
                0,
                8));

        for (var index = 8;
             index < packet.Length;
             index++)
        {
            packet[index] =
                (byte)(
                    (index * 37 + 11) &
                    0xFF);
        }

        return packet;
    }
}
