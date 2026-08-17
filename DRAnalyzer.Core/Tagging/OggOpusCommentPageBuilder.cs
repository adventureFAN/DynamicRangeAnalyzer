using System.Buffers.Binary;

namespace DRAnalyzer.Core.Tagging;

public static class OggOpusCommentPageBuilder
{
    private const int MaximumSegmentsPerPage = 255;

    // RFC 7845 erlaubt Implementierungen,
    // Comment-Header über 120 MiB als ungültig zu behandeln.
    private const int MaximumCommentHeaderBytes =
        125_829_120;

    public static IReadOnlyList<byte[]> Build(
        byte[] opusTagsPacket,
        uint streamSerial,
        uint firstPageSequence)
    {
        ArgumentNullException.ThrowIfNull(
            opusTagsPacket);

        if (!opusTagsPacket
                .AsSpan()
                .StartsWith("OpusTags"u8))
        {
            throw new InvalidDataException(
                "Das Paket ist kein OpusTags-Paket.");
        }

        if (opusTagsPacket.Length >
            MaximumCommentHeaderBytes)
        {
            throw new InvalidDataException(
                "Der OpusTags-Header ist zu groß.");
        }

        var lacingValues =
            CreateLacingValues(
                opusTagsPacket.Length);

        var pages =
            new List<byte[]>();

        var lacingOffset = 0;
        var bodyOffset = 0;
        var pageIndex = 0;

        while (lacingOffset <
               lacingValues.Count)
        {
            var segmentCount =
                Math.Min(
                    MaximumSegmentsPerPage,
                    lacingValues.Count -
                    lacingOffset);

            var pageLacing =
                lacingValues
                    .Skip(lacingOffset)
                    .Take(segmentCount)
                    .ToArray();

            var bodyLength =
                pageLacing.Sum(
                    value => (int)value);

            var isFinalPage =
                lacingOffset +
                segmentCount ==
                lacingValues.Count;

            var headerType =
                pageIndex == 0
                    ? (byte)0x00
                    : (byte)0x01;

            var granulePosition =
                isFinalPage
                    ? 0L
                    : -1L;

            var sequence =
                checked(
                    firstPageSequence +
                    (uint)pageIndex);

            var page =
                new byte[
                    27 +
                    pageLacing.Length +
                    bodyLength];

            "OggS"u8.CopyTo(
                page.AsSpan(0, 4));

            // Ogg stream structure version
            page[4] = 0;

            // Kein BOS/EOS.
            // Nur Folgeseiten eines gespannten Pakets
            // erhalten das continued-packet-Flag.
            page[5] =
                headerType;

            BinaryPrimitives
                .WriteInt64LittleEndian(
                    page.AsSpan(
                        6,
                        8),
                    granulePosition);

            BinaryPrimitives
                .WriteUInt32LittleEndian(
                    page.AsSpan(
                        14,
                        4),
                    streamSerial);

            BinaryPrimitives
                .WriteUInt32LittleEndian(
                    page.AsSpan(
                        18,
                        4),
                    sequence);

            // CRC bleibt hier zunächst 0.
            page.AsSpan(
                    22,
                    4)
                .Clear();

            page[26] =
                checked(
                    (byte)pageLacing.Length);

            pageLacing.CopyTo(
                page,
                27);

            opusTagsPacket
                .AsSpan(
                    bodyOffset,
                    bodyLength)
                .CopyTo(
                    page.AsSpan(
                        27 +
                        pageLacing.Length,
                        bodyLength));

            bodyOffset +=
                bodyLength;

            pages.Add(
                OggPageCodec
                    .WithRecalculatedChecksum(
                        page));

            lacingOffset +=
                segmentCount;

            pageIndex++;
        }

        if (bodyOffset !=
            opusTagsPacket.Length)
        {
            throw new InvalidDataException(
                "Das OpusTags-Paket wurde nicht " +
                "vollständig in Ogg-Seiten verpackt.");
        }

        return pages;
    }

    private static List<byte>
        CreateLacingValues(
            int packetLength)
    {
        var result =
            new List<byte>();

        var remaining =
            packetLength;

        while (remaining >= 255)
        {
            result.Add(255);
            remaining -= 255;
        }

        // Wichtig:
        // Auch bei einem exakten Vielfachen von 255
        // muss ein abschließender Wert < 255 folgen.
        // Dann ist dieser Wert 0.
        result.Add(
            checked((byte)remaining));

        return result;
    }
}
