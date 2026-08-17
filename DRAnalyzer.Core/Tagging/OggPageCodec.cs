using System.Buffers.Binary;

namespace DRAnalyzer.Core.Tagging;

public static class OggPageCodec
{
    private const uint CrcPolynomial =
        0x04C11DB7;

    private static readonly uint[] CrcTable =
        BuildCrcTable();

    public static byte[]? ReadRawPage(
        Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (stream.Position == stream.Length)
            return null;

        var header =
            new byte[27];

        try
        {
            stream.ReadExactly(header);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException(
                "Die Datei endet mitten in einem Ogg-Seitenheader.",
                ex);
        }

        if (!header
                .AsSpan(0, 4)
                .SequenceEqual("OggS"u8))
        {
            throw new InvalidDataException(
                "Ungültige Ogg-Capture-Signatur.");
        }

        if (header[4] != 0)
        {
            throw new InvalidDataException(
                $"Nicht unterstützte Ogg-Version: {header[4]}");
        }

        var segmentCount =
            header[26];

        var lacing =
            new byte[segmentCount];

        try
        {
            stream.ReadExactly(lacing);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException(
                "Die Datei endet mitten in der Ogg-Segmenttabelle.",
                ex);
        }

        var bodyLength =
            lacing.Sum(
                value => (int)value);

        var body =
            new byte[bodyLength];

        try
        {
            stream.ReadExactly(body);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException(
                "Die Datei endet mitten in den Ogg-Seitendaten.",
                ex);
        }

        var page =
            new byte[
                27 +
                lacing.Length +
                body.Length];

        header.CopyTo(
            page,
            0);

        lacing.CopyTo(
            page,
            27);

        body.CopyTo(
            page,
            27 + lacing.Length);

        return page;
    }

    public static uint GetStoredChecksum(
        byte[] page)
    {
        ValidatePageShape(page);

        return BinaryPrimitives
            .ReadUInt32LittleEndian(
                page.AsSpan(
                    22,
                    4));
    }

    public static uint CalculateChecksum(
        byte[] page)
    {
        ValidatePageShape(page);

        uint crc = 0;

        for (var index = 0;
             index < page.Length;
             index++)
        {
            var value =
                index is >= 22 and <= 25
                    ? (byte)0
                    : page[index];

            var tableIndex =
                (byte)(
                    (crc >> 24) ^
                    value);

            crc =
                (crc << 8) ^
                CrcTable[tableIndex];
        }

        return crc;
    }

    public static bool HasValidChecksum(
        byte[] page)
    {
        return
            GetStoredChecksum(page) ==
            CalculateChecksum(page);
    }

    public static byte[] WithRecalculatedChecksum(
        byte[] page)
    {
        ValidatePageShape(page);

        var result =
            page.ToArray();

        result.AsSpan(
                22,
                4)
            .Clear();

        var crc =
            CalculateChecksum(result);

        BinaryPrimitives
            .WriteUInt32LittleEndian(
                result.AsSpan(
                    22,
                    4),
                crc);

        return result;
    }

    private static void ValidatePageShape(
        byte[] page)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (page.Length < 27)
        {
            throw new InvalidDataException(
                "Ogg-Seite ist zu kurz.");
        }

        if (!page
                .AsSpan(0, 4)
                .SequenceEqual("OggS"u8))
        {
            throw new InvalidDataException(
                "Ungültige Ogg-Capture-Signatur.");
        }

        if (page[4] != 0)
        {
            throw new InvalidDataException(
                $"Nicht unterstützte Ogg-Version: {page[4]}");
        }

        var segmentCount =
            page[26];

        var headerLength =
            27 + segmentCount;

        if (page.Length < headerLength)
        {
            throw new InvalidDataException(
                "Unvollständige Ogg-Segmenttabelle.");
        }

        var bodyLength = 0;

        for (var index = 0;
             index < segmentCount;
             index++)
        {
            bodyLength +=
                page[27 + index];
        }

        var expectedLength =
            headerLength +
            bodyLength;

        if (page.Length != expectedLength)
        {
            throw new InvalidDataException(
                "Ogg-Seitenlänge stimmt nicht mit " +
                "der Segmenttabelle überein.");
        }
    }

    private static uint[] BuildCrcTable()
    {
        var table =
            new uint[256];

        for (uint index = 0;
             index < table.Length;
             index++)
        {
            var value =
                index << 24;

            for (var bit = 0;
                 bit < 8;
                 bit++)
            {
                value =
                    (value & 0x80000000) != 0
                        ? (value << 1) ^
                          CrcPolynomial
                        : value << 1;
            }

            table[index] =
                value;
        }

        return table;
    }
}
