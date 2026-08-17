using System.Buffers.Binary;
using DRAnalyzer.Core.Tagging;

namespace DRAnalyzer.Tests;

public sealed class OggVorbisHeaderPageBuilderTests
{
    [Fact]
    public void Build_ReconstructsCommentAndSetupPacketsExactly()
    {
        var comment =
            CreatePacket(
                0x03,
                payloadLength: 600);

        var setup =
            CreatePacket(
                0x05,
                payloadLength: 90_000);

        var pages =
            OggVorbisHeaderPageBuilder.Build(
                comment,
                setup,
                streamSerial: 0x12345678,
                firstPageSequence: 7);

        Assert.NotEmpty(pages);

        for (var index = 0;
             index < pages.Count;
             index++)
        {
            var page =
                pages[index];

            Assert.True(
                OggPageCodec.HasValidChecksum(page));

            Assert.Equal(
                0x12345678u,
                ReadUInt32(page, 14));

            Assert.Equal(
                checked(7u + (uint)index),
                ReadUInt32(page, 18));

            var completesPacket =
                PageCompletesPacket(page);

            Assert.Equal(
                completesPacket
                    ? 0L
                    : -1L,
                BinaryPrimitives.ReadInt64LittleEndian(
                    page.AsSpan(6, 8)));

            var expectedContinued =
                index > 0 &&
                LastLacing(pages[index - 1]) == 255;

            Assert.Equal(
                expectedContinued,
                (page[5] & 0x01) != 0);

            Assert.Equal(
                0,
                page[5] & 0x06);
        }

        var packets =
            ReadPackets(pages);

        Assert.Equal(2, packets.Count);

        Assert.True(
            comment.AsSpan().SequenceEqual(packets[0]));

        Assert.True(
            setup.AsSpan().SequenceEqual(packets[1]));
    }

    [Fact]
    public void Build_HeaderPageWithoutCompletedPacket_UsesGranulePositionMinusOne()
    {
        var comment =
            CreatePacket(
                0x03,
                payloadLength: 100_000);

        var setup =
            CreatePacket(
                0x05,
                payloadLength: 10);

        var pages =
            OggVorbisHeaderPageBuilder.Build(
                comment,
                setup,
                streamSerial: 0x10203040,
                firstPageSequence: 1);

        Assert.True(pages.Count >= 2);
        Assert.False(PageCompletesPacket(pages[0]));
        Assert.Equal(
            -1L,
            BinaryPrimitives.ReadInt64LittleEndian(
                pages[0].AsSpan(6, 8)));

        Assert.Contains(
            pages.Skip(1),
            page =>
                PageCompletesPacket(page) &&
                BinaryPrimitives.ReadInt64LittleEndian(
                    page.AsSpan(6, 8)) == 0L);
    }

    [Fact]
    public void Build_ExactMultipleOf255PacketLength_RoundTrips()
    {
        var comment =
            CreateExactLengthPacket(
                0x03,
                totalLength: 510);

        var setup =
            CreatePacket(
                0x05,
                payloadLength: 10);

        var pages =
            OggVorbisHeaderPageBuilder.Build(
                comment,
                setup,
                streamSerial: 1,
                firstPageSequence: 1);

        var packets =
            ReadPackets(pages);

        Assert.True(
            comment.AsSpan().SequenceEqual(packets[0]));

        Assert.True(
            setup.AsSpan().SequenceEqual(packets[1]));
    }

    private static byte[] CreatePacket(
        byte type,
        int payloadLength)
    {
        var packet =
            new byte[7 + payloadLength];

        packet[0] = type;
        "vorbis"u8.CopyTo(
            packet.AsSpan(1, 6));

        for (var index = 7;
             index < packet.Length;
             index++)
        {
            packet[index] =
                checked((byte)(index % 251));
        }

        return packet;
    }

    private static byte[] CreateExactLengthPacket(
        byte type,
        int totalLength)
    {
        Assert.True(totalLength >= 7);

        return CreatePacket(
            type,
            totalLength - 7);
    }

    private static List<byte[]> ReadPackets(
        IReadOnlyList<byte[]> pages)
    {
        var packets =
            new List<byte[]>();

        using var current =
            new MemoryStream();

        foreach (var page in pages)
        {
            var segmentCount =
                page[26];

            var bodyOffset =
                27 + segmentCount;

            var bodyCursor = 0;

            for (var index = 0;
                 index < segmentCount;
                 index++)
            {
                var length =
                    page[27 + index];

                current.Write(
                    page,
                    bodyOffset + bodyCursor,
                    length);

                bodyCursor += length;

                if (length < 255)
                {
                    packets.Add(
                        current.ToArray());

                    current.SetLength(0);
                }
            }
        }

        Assert.Equal(0, current.Length);

        return packets;
    }

    private static bool PageCompletesPacket(
        byte[] page)
    {
        var count =
            page[26];

        for (var index = 0;
             index < count;
             index++)
        {
            if (page[27 + index] < 255)
            {
                return true;
            }
        }

        return false;
    }

    private static byte LastLacing(
        byte[] page)
    {
        var count =
            page[26];

        Assert.True(count > 0);

        return page[27 + count - 1];
    }

    private static uint ReadUInt32(
        byte[] page,
        int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(
            page.AsSpan(offset, 4));
    }
}
