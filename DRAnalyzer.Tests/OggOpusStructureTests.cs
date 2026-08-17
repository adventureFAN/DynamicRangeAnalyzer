using System.Buffers.Binary;

namespace DRAnalyzer.Tests;

[Trait("Category", "ExternalReference")]
public sealed class OggOpusStructureTests
{
    [Fact]
    public void RealOpus_HasExpectedPacketStructure()
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
            packets.Count >= 3,
            "Die Datei enthält nicht genügend Ogg-Pakete.");

        Assert.True(
            packets[0].Data
                .AsSpan()
                .StartsWith("OpusHead"u8),
            "Das erste Paket ist kein OpusHead.");

        Assert.Equal(
            packets[0].StartPageSequence,
            packets[0].EndPageSequence);

        Assert.True(
            packets[0].EndsAtPageBoundary);

        Assert.True(
            packets[1].Data
                .AsSpan()
                .StartsWith("OpusTags"u8),
            "Das zweite Paket ist kein OpusTags.");

        Assert.True(
            packets[1].EndsAtPageBoundary);

        Assert.True(
            packets.Skip(2)
                .Sum(x => (long)x.Data.Length) > 0,
            "Es wurden keine Audio-Pakete gefunden.");
    }

    private static List<OggPacket> ReadPackets(
        string filePath)
    {
        using var stream =
            new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

        var packets =
            new List<OggPacket>();

        var currentPacket =
            new List<byte>();

        var packetOpen = false;
        uint packetStartPage = 0;

        uint? streamSerial = null;
        uint? previousSequence = null;

        var firstPage = true;

        var header =
            new byte[27];

        while (stream.Position < stream.Length)
        {
            stream.ReadExactly(header);

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

            var headerType =
                header[5];

            var serial =
                BinaryPrimitives.ReadUInt32LittleEndian(
                    header.AsSpan(14, 4));

            var sequence =
                BinaryPrimitives.ReadUInt32LittleEndian(
                    header.AsSpan(18, 4));

            if (firstPage)
            {
                Assert.True(
                    (headerType & 0x02) != 0,
                    "Erste Ogg-Seite besitzt kein BOS-Flag.");

                streamSerial = serial;
                firstPage = false;
            }
            else
            {
                Assert.Equal(
                    streamSerial,
                    serial);

                if (previousSequence.HasValue)
                {
                    Assert.Equal(
                        previousSequence.Value + 1,
                        sequence);
                }
            }

            previousSequence = sequence;

            var segmentCount =
                header[26];

            var lacingValues =
                new byte[segmentCount];

            stream.ReadExactly(
                lacingValues);

            var bodyLength =
                lacingValues.Sum(
                    value => (int)value);

            var body =
                new byte[bodyLength];

            stream.ReadExactly(body);

            var continued =
                (headerType & 0x01) != 0;

            if (continued && !packetOpen)
            {
                throw new InvalidDataException(
                    "Fortgesetztes Paket ohne bekannten Anfang.");
            }

            if (!continued && packetOpen)
            {
                throw new InvalidDataException(
                    "Fortgesetztes Paket wurde unerwartet beendet.");
            }

            var bodyOffset = 0;

            for (var segmentIndex = 0;
                 segmentIndex < lacingValues.Length;
                 segmentIndex++)
            {
                if (!packetOpen)
                {
                    currentPacket.Clear();
                    packetStartPage = sequence;
                    packetOpen = true;
                }

                var segmentLength =
                    lacingValues[segmentIndex];

                currentPacket.AddRange(
                    body.AsSpan(
                            bodyOffset,
                            segmentLength)
                        .ToArray());

                bodyOffset += segmentLength;

                if (segmentLength < 255)
                {
                    packets.Add(
                        new OggPacket(
                            currentPacket.ToArray(),
                            packetStartPage,
                            sequence,
                            segmentIndex ==
                            lacingValues.Length - 1));

                    packetOpen = false;
                }
            }

            Assert.Equal(
                body.Length,
                bodyOffset);
        }

        Assert.False(
            packetOpen,
            "Datei endet mitten in einem Ogg-Paket.");

        return packets;
    }

    private sealed record OggPacket(
        byte[] Data,
        uint StartPageSequence,
        uint EndPageSequence,
        bool EndsAtPageBoundary);
}
