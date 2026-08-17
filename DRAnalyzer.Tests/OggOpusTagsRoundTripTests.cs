using System.Buffers.Binary;
using System.Text;

namespace DRAnalyzer.Tests;

[Trait("Category", "ExternalReference")]
public sealed class OggOpusTagsRoundTripTests
{
    [Fact]
    public void RealOpus_OpusTags_RoundTripsByteExactly()
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

        var packets =
            ReadPackets(requiredFilePath);

        Assert.True(
            packets.Count >= 2);

        var opusTagsPacket =
            packets[1];

        Assert.True(
            opusTagsPacket
                .AsSpan()
                .StartsWith("OpusTags"u8),
            "Das zweite Ogg-Paket ist kein OpusTags-Paket.");

        var parsed =
            ParseOpusTags(
                opusTagsPacket);

        Assert.NotEmpty(
            parsed.Vendor);

        Assert.NotEmpty(
            parsed.Comments);

        Assert.Contains(
            parsed.Comments,
            comment =>
                IsField(
                    comment,
                    "DYNAMIC RANGE"));

        Assert.Contains(
            parsed.Comments,
            comment =>
                IsField(
                    comment,
                    "ALBUM DYNAMIC RANGE"));

        var rebuilt =
            BuildOpusTags(
                parsed);

        Assert.True(
            opusTagsPacket
                .AsSpan()
                .SequenceEqual(rebuilt),
            "Das unveränderte OpusTags-Paket " +
            "konnte nicht bytegenau rekonstruiert werden.");

        var trackDr =
            GetSingleFieldValue(
                parsed.Comments,
                "DYNAMIC RANGE");

        var albumDr =
            GetSingleFieldValue(
                parsed.Comments,
                "ALBUM DYNAMIC RANGE");

        Assert.False(
            string.IsNullOrWhiteSpace(trackDr));

        Assert.False(
            string.IsNullOrWhiteSpace(albumDr));

        Console.WriteLine(
            $"Vendor: {Encoding.UTF8.GetString(parsed.Vendor)}");

        Console.WriteLine(
            $"Comments: {parsed.Comments.Count}");

        Console.WriteLine(
            $"Trailing bytes: {parsed.TrailingData.Length}");

        Console.WriteLine(
            $"Track DR: {trackDr}");

        Console.WriteLine(
            $"Album DR: {albumDr}");
    }

    private static ParsedOpusTags ParseOpusTags(
        byte[] packet)
    {
        if (!packet
                .AsSpan()
                .StartsWith("OpusTags"u8))
        {
            throw new InvalidDataException(
                "Kein OpusTags-Paket.");
        }

        var offset = 8;

        var vendorLength =
            ReadUInt32(
                packet,
                ref offset);

        var vendor =
            ReadBytes(
                packet,
                ref offset,
                vendorLength);

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

            comments.Add(
                ReadBytes(
                    packet,
                    ref offset,
                    length));
        }

        var trailingData =
            packet
                .AsSpan(offset)
                .ToArray();

        return new ParsedOpusTags(
            vendor,
            comments,
            trailingData);
    }

    private static byte[] BuildOpusTags(
        ParsedOpusTags tags)
    {
        using var stream =
            new MemoryStream();

        stream.Write(
            "OpusTags"u8);

        WriteUInt32(
            stream,
            checked((uint)tags.Vendor.Length));

        stream.Write(
            tags.Vendor);

        WriteUInt32(
            stream,
            checked((uint)tags.Comments.Count));

        foreach (var comment in tags.Comments)
        {
            WriteUInt32(
                stream,
                checked((uint)comment.Length));

            stream.Write(
                comment);
        }

        stream.Write(
            tags.TrailingData);

        return stream.ToArray();
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

        var actualName =
            Encoding.ASCII.GetString(
                comment,
                0,
                equalsIndex);

        return string.Equals(
            actualName,
            fieldName,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string GetSingleFieldValue(
        IReadOnlyList<byte[]> comments,
        string fieldName)
    {
        var comment =
            Assert.Single(
                comments,
                value =>
                    IsField(
                        value,
                        fieldName));

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
        if (offset > data.Length - 4)
        {
            throw new InvalidDataException(
                "Ungültiges OpusTags-Paket.");
        }

        var value =
            BinaryPrimitives.ReadUInt32LittleEndian(
                data.AsSpan(
                    offset,
                    4));

        offset += 4;

        return value;
    }

    private static byte[] ReadBytes(
        byte[] data,
        ref int offset,
        uint length)
    {
        if (length > int.MaxValue)
        {
            throw new InvalidDataException(
                "Ungültige OpusTags-Länge.");
        }

        var intLength =
            (int)length;

        if (offset >
            data.Length - intLength)
        {
            throw new InvalidDataException(
                "Beschädigtes OpusTags-Paket.");
        }

        var result =
            data
                .AsSpan(
                    offset,
                    intLength)
                .ToArray();

        offset +=
            intLength;

        return result;
    }

    private static void WriteUInt32(
        Stream stream,
        uint value)
    {
        Span<byte> buffer =
            stackalloc byte[4];

        BinaryPrimitives.WriteUInt32LittleEndian(
            buffer,
            value);

        stream.Write(
            buffer);
    }

    private static List<byte[]> ReadPackets(
        string filePath)
    {
        using var stream =
            new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

        var packets =
            new List<byte[]>();

        var currentPacket =
            new List<byte>();

        var packetOpen =
            false;

        uint? serial =
            null;

        var header =
            new byte[27];

        while (stream.Position <
               stream.Length)
        {
            stream.ReadExactly(
                header);

            if (!header
                    .AsSpan(0, 4)
                    .SequenceEqual("OggS"u8))
            {
                throw new InvalidDataException(
                    "Ungültige Ogg-Seite.");
            }

            var currentSerial =
                BinaryPrimitives
                    .ReadUInt32LittleEndian(
                        header.AsSpan(
                            14,
                            4));

            if (!serial.HasValue)
            {
                serial =
                    currentSerial;
            }
            else if (serial.Value !=
                     currentSerial)
            {
                throw new InvalidDataException(
                    "Verkettete oder mehrere Ogg-Streams " +
                    "werden in diesem Test nicht unterstützt.");
            }

            var continued =
                (header[5] & 0x01) != 0;

            if (continued &&
                !packetOpen)
            {
                throw new InvalidDataException(
                    "Fortgesetztes Paket ohne Anfang.");
            }

            if (!continued &&
                packetOpen)
            {
                throw new InvalidDataException(
                    "Fortsetzung eines Pakets fehlt.");
            }

            var segmentCount =
                header[26];

            var lacing =
                new byte[segmentCount];

            stream.ReadExactly(
                lacing);

            var bodyLength =
                lacing.Sum(
                    value =>
                        (int)value);

            var body =
                new byte[bodyLength];

            stream.ReadExactly(
                body);

            var bodyOffset =
                0;

            foreach (var segmentLength
                     in lacing)
            {
                if (!packetOpen)
                {
                    currentPacket.Clear();
                    packetOpen = true;
                }

                currentPacket.AddRange(
                    body
                        .AsSpan(
                            bodyOffset,
                            segmentLength)
                        .ToArray());

                bodyOffset +=
                    segmentLength;

                if (segmentLength < 255)
                {
                    packets.Add(
                        currentPacket.ToArray());

                    packetOpen =
                        false;
                }
            }

            if (bodyOffset !=
                body.Length)
            {
                throw new InvalidDataException(
                    "Ungültige Ogg-Segmentierung.");
            }
        }

        if (packetOpen)
        {
            throw new InvalidDataException(
                "Datei endet mitten in einem Paket.");
        }

        return packets;
    }

    private sealed record ParsedOpusTags(
        byte[] Vendor,
        IReadOnlyList<byte[]> Comments,
        byte[] TrailingData);
}
