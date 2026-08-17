using System.Buffers.Binary;

namespace DRAnalyzer.Core.Tagging;

public static class OggVorbisHeaderPageBuilder
{
    private const int MaximumSegmentsPerPage =
        255;

    private const int MaximumHeaderPacketBytes =
        125_829_120;

    private static ReadOnlySpan<byte> CommentSignature =>
        new byte[] { 0x03, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s' };

    private static ReadOnlySpan<byte> SetupSignature =>
        new byte[] { 0x05, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s' };

    public static IReadOnlyList<byte[]> Build(
        byte[] commentPacket,
        byte[] setupPacket,
        uint streamSerial,
        uint firstPageSequence)
    {
        ArgumentNullException.ThrowIfNull(commentPacket);
        ArgumentNullException.ThrowIfNull(setupPacket);

        ValidatePacket(
            commentPacket,
            CommentSignature,
            "Vorbis-Comment");

        ValidatePacket(
            setupPacket,
            SetupSignature,
            "Vorbis-Setup");

        var segments =
            CreateSegments(commentPacket)
                .Concat(CreateSegments(setupPacket))
                .ToArray();

        var pages =
            new List<byte[]>();

        var segmentOffset = 0;
        var pageIndex = 0;

        while (segmentOffset <
               segments.Length)
        {
            var segmentCount =
                Math.Min(
                    MaximumSegmentsPerPage,
                    segments.Length -
                    segmentOffset);

            var pageSegments =
                segments
                    .Skip(segmentOffset)
                    .Take(segmentCount)
                    .ToArray();

            var startsWithContinuation =
                segmentOffset > 0 &&
                !segments[segmentOffset - 1]
                    .EndsPacket;

            var bodyLength =
                pageSegments.Sum(
                    segment =>
                        segment.Data.Length);

            var page =
                new byte[
                    27 +
                    pageSegments.Length +
                    bodyLength];

            "OggS"u8.CopyTo(
                page.AsSpan(0, 4));

            page[4] = 0;
            page[5] =
                startsWithContinuation
                    ? (byte)0x01
                    : (byte)0x00;

            var completesPacket =
                pageSegments.Any(
                    segment => segment.EndsPacket);

            // Header-Pakete selbst besitzen Granule Position 0.
            // Eine Ogg-Seite, auf der noch kein Paket endet,
            // trägt gemäß Ogg-Framing die spezielle Position -1.
            BinaryPrimitives.WriteInt64LittleEndian(
                page.AsSpan(6, 8),
                completesPacket
                    ? 0L
                    : -1L);

            BinaryPrimitives.WriteUInt32LittleEndian(
                page.AsSpan(14, 4),
                streamSerial);

            BinaryPrimitives.WriteUInt32LittleEndian(
                page.AsSpan(18, 4),
                checked(
                    firstPageSequence +
                    (uint)pageIndex));

            page.AsSpan(22, 4).Clear();

            page[26] =
                checked((byte)pageSegments.Length);

            var bodyOffset =
                27 + pageSegments.Length;

            for (var index = 0;
                 index < pageSegments.Length;
                 index++)
            {
                var segment =
                    pageSegments[index];

                page[27 + index] =
                    segment.LacingValue;

                segment.Data.CopyTo(
                    page,
                    bodyOffset);

                bodyOffset +=
                    segment.Data.Length;
            }

            pages.Add(
                OggPageCodec.WithRecalculatedChecksum(
                    page));

            segmentOffset +=
                segmentCount;

            pageIndex++;
        }

        if (pages.Count == 0)
        {
            throw new InvalidDataException(
                "Es konnten keine Vorbis-Headerseiten erzeugt werden.");
        }

        return pages;
    }

    private static void ValidatePacket(
        byte[] packet,
        ReadOnlySpan<byte> signature,
        string packetName)
    {
        if (!packet
                .AsSpan()
                .StartsWith(signature))
        {
            throw new InvalidDataException(
                $"Das Paket ist kein {packetName}-Header.");
        }

        if (packet.Length >
            MaximumHeaderPacketBytes)
        {
            throw new InvalidDataException(
                $"Der {packetName}-Header ist zu groß.");
        }
    }

    private static IEnumerable<PacketSegment>
        CreateSegments(
            byte[] packet)
    {
        var offset = 0;
        var remaining =
            packet.Length;

        while (remaining >= 255)
        {
            yield return new PacketSegment(
                255,
                packet
                    .AsSpan(offset, 255)
                    .ToArray(),
                EndsPacket: false);

            offset += 255;
            remaining -= 255;
        }

        // Auch bei exaktem Vielfachen von 255 ist der abschließende
        // Null-Lacing-Wert erforderlich und markiert das Paketende.
        yield return new PacketSegment(
            checked((byte)remaining),
            packet
                .AsSpan(offset, remaining)
                .ToArray(),
            EndsPacket: true);
    }

    private sealed record PacketSegment(
        byte LacingValue,
        byte[] Data,
        bool EndsPacket);
}
