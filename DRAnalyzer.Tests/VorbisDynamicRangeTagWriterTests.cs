using System.Buffers.Binary;
using System.Text;
using DRAnalyzer.Core.Tagging;

namespace DRAnalyzer.Tests;

public sealed class VorbisDynamicRangeTagWriterTests
{
    [Fact]
    public void WriteAndRemove_PreserveForeignMetadataSetupAndAudioPackets()
    {
        var tempDirectory =
            CreateTempDirectory();

        try
        {
            var filePath =
                Path.Combine(
                    tempDirectory,
                    "preservation.ogg");

            var foreignComments =
                new[]
                {
                    Utf8("ARTIST=Daft Punk"),
                    Utf8("TITLE=Something About Us"),
                    Utf8("REPLAYGAIN_TRACK_GAIN=-6.42 dB"),
                    Utf8("METADATA_BLOCK_PICTURE=TEST-COVER-BYTES"),
                    Utf8("CUSTOM=keep-me")
                };

            var originalComment =
                BuildCommentPacket(
                    "Synthetic Vorbis Vendor"u8.ToArray(),
                    foreignComments
                        .Concat(
                            new[]
                            {
                                Utf8("dynamic range=3"),
                                Utf8("DYNAMIC RANGE=4"),
                                Utf8("album dynamic range=5")
                            })
                        .ToArray(),
                    new byte[] { 0x01, 0xA0 });

            CreateSyntheticVorbisFile(
                filePath,
                originalComment,
                setupPayloadLength: 12_000,
                audioPacketCount: 3);

            var before =
                ReadOggFile(filePath);

            Assert.True(
                before.Packets[0].Data.AsSpan().StartsWith(
                    new byte[] { 0x01, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s' }));

            Assert.True(
                before.Packets[1].Data.AsSpan().StartsWith(
                    new byte[] { 0x03, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s' }));

            Assert.True(
                before.Packets[2].Data.AsSpan().StartsWith(
                    new byte[] { 0x05, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s' }));

            var beforeComment =
                ParseCommentPacket(
                    before.Packets[1].Data);

            var beforeSetup =
                before.Packets[2].Data.ToArray();

            var beforeAudio =
                before.Packets
                    .Skip(3)
                    .Select(packet => packet.Data.ToArray())
                    .ToArray();

            VorbisDynamicRangeTagWriter.Write(
                filePath,
                trackDynamicRange: 12,
                albumDynamicRange: 13);

            var afterWrite =
                ReadOggFile(filePath);

            var writtenComment =
                ParseCommentPacket(
                    afterWrite.Packets[1].Data);

            Assert.True(
                beforeComment.Vendor
                    .AsSpan()
                    .SequenceEqual(
                        writtenComment.Vendor));

            Assert.True(
                beforeComment.TrailingData
                    .AsSpan()
                    .SequenceEqual(
                        writtenComment.TrailingData));

            var writtenForeign =
                writtenComment.Comments
                    .Where(comment => !IsOwned(comment))
                    .ToArray();

            Assert.Equal(
                foreignComments.Length,
                writtenForeign.Length);

            for (var index = 0;
                 index < foreignComments.Length;
                 index++)
            {
                Assert.True(
                    foreignComments[index]
                        .AsSpan()
                        .SequenceEqual(
                            writtenForeign[index]));
            }

            Assert.Equal(
                "12",
                GetSingleValue(
                    writtenComment.Comments,
                    "DYNAMIC RANGE"));

            Assert.Equal(
                "13",
                GetSingleValue(
                    writtenComment.Comments,
                    "ALBUM DYNAMIC RANGE"));

            Assert.True(
                beforeSetup
                    .AsSpan()
                    .SequenceEqual(
                        afterWrite.Packets[2].Data));

            AssertPacketsEqual(
                beforeAudio,
                afterWrite.Packets
                    .Skip(3)
                    .Select(packet => packet.Data)
                    .ToArray());

            VorbisDynamicRangeTagWriter.Remove(
                filePath);

            var afterRemove =
                ReadOggFile(filePath);

            var removedComment =
                ParseCommentPacket(
                    afterRemove.Packets[1].Data);

            Assert.DoesNotContain(
                removedComment.Comments,
                IsOwned);

            Assert.True(
                beforeComment.Vendor
                    .AsSpan()
                    .SequenceEqual(
                        removedComment.Vendor));

            Assert.True(
                beforeComment.TrailingData
                    .AsSpan()
                    .SequenceEqual(
                        removedComment.TrailingData));

            var removedForeign =
                removedComment.Comments
                    .Where(comment => !IsOwned(comment))
                    .ToArray();

            Assert.Equal(
                foreignComments.Length,
                removedForeign.Length);

            for (var index = 0;
                 index < foreignComments.Length;
                 index++)
            {
                Assert.True(
                    foreignComments[index]
                        .AsSpan()
                        .SequenceEqual(
                            removedForeign[index]));
            }

            Assert.True(
                beforeSetup
                    .AsSpan()
                    .SequenceEqual(
                        afterRemove.Packets[2].Data));

            AssertPacketsEqual(
                beforeAudio,
                afterRemove.Packets
                    .Skip(3)
                    .Select(packet => packet.Data)
                    .ToArray());
        }
        finally
        {
            Directory.Delete(
                tempDirectory,
                recursive: true);
        }
    }

    [Fact]
    public void CommentHeaderPageCountChange_RenumbersOnlyFollowingPages()
    {
        var tempDirectory =
            CreateTempDirectory();

        try
        {
            var filePath =
                Path.Combine(
                    tempDirectory,
                    "sequence-delta.ogg");

            var hugeOldDr =
                Utf8(
                    "DYNAMIC RANGE=" +
                    new string('9', 100_000));

            var comment =
                BuildCommentPacket(
                    "Vendor"u8.ToArray(),
                    new[]
                    {
                        Utf8("TITLE=Sequence Delta"),
                        hugeOldDr,
                        Utf8("ALBUM DYNAMIC RANGE=8")
                    },
                    new byte[] { 0x01 });

            CreateSyntheticVorbisFile(
                filePath,
                comment,
                setupPayloadLength: 8_000,
                audioPacketCount: 2);

            var before =
                ReadOggFile(filePath);

            var beforeFirstAudioPage =
                before.Packets[3].StartPageIndex;

            Assert.True(
                beforeFirstAudioPage > 2);

            var beforeAudioPages =
                before.Pages
                    .Skip(beforeFirstAudioPage)
                    .Select(page => page.ToArray())
                    .ToArray();

            VorbisDynamicRangeTagWriter.Write(
                filePath,
                trackDynamicRange: 10,
                albumDynamicRange: 11);

            var after =
                ReadOggFile(filePath);

            var afterFirstAudioPage =
                after.Packets[3].StartPageIndex;

            Assert.True(
                afterFirstAudioPage <
                beforeFirstAudioPage);

            var afterAudioPages =
                after.Pages
                    .Skip(afterFirstAudioPage)
                    .ToArray();

            Assert.Equal(
                beforeAudioPages.Length,
                afterAudioPages.Length);

            for (var index = 0;
                 index < beforeAudioPages.Length;
                 index++)
            {
                var beforePage =
                    beforeAudioPages[index];

                var afterPage =
                    afterAudioPages[index];

                Assert.Equal(
                    beforePage.Length,
                    afterPage.Length);

                Assert.True(
                    beforePage
                        .AsSpan(0, 18)
                        .SequenceEqual(
                            afterPage.AsSpan(0, 18)));

                Assert.True(
                    beforePage
                        .AsSpan(26)
                        .SequenceEqual(
                            afterPage.AsSpan(26)));

                Assert.True(
                    OggPageCodec.HasValidChecksum(
                        afterPage));
            }
        }
        finally
        {
            Directory.Delete(
                tempDirectory,
                recursive: true);
        }
    }

    [Fact]
    public void Remove_WithoutOwnedTags_IsByteExactNoOp()
    {
        var tempDirectory =
            CreateTempDirectory();

        try
        {
            var filePath =
                Path.Combine(
                    tempDirectory,
                    "no-owned-tags.ogg");

            var comment =
                BuildCommentPacket(
                    "Vendor"u8.ToArray(),
                    new[]
                    {
                        Utf8("ARTIST=Artist"),
                        Utf8("TITLE=Track")
                    },
                    new byte[] { 0x01 });

            CreateSyntheticVorbisFile(
                filePath,
                comment,
                setupPayloadLength: 2_000,
                audioPacketCount: 2);

            var before =
                File.ReadAllBytes(filePath);

            VorbisDynamicRangeTagWriter.Remove(
                filePath);

            var after =
                File.ReadAllBytes(filePath);

            Assert.True(
                before
                    .AsSpan()
                    .SequenceEqual(after));
        }
        finally
        {
            Directory.Delete(
                tempDirectory,
                recursive: true);
        }
    }

    [Fact]
    public void CorruptHeaderCrc_IsRejected_AndOriginalRemainsUnchanged()
    {
        var tempDirectory =
            CreateTempDirectory();

        try
        {
            var filePath =
                Path.Combine(
                    tempDirectory,
                    "corrupt-crc.ogg");

            var comment =
                BuildCommentPacket(
                    "Vendor"u8.ToArray(),
                    new[] { Utf8("TITLE=Track") },
                    new byte[] { 0x01 });

            CreateSyntheticVorbisFile(
                filePath,
                comment,
                setupPayloadLength: 2_000,
                audioPacketCount: 1);

            var bytes =
                File.ReadAllBytes(filePath);

            // CRC der ersten Seite absichtlich beschädigen.
            bytes[22] ^= 0x5A;
            File.WriteAllBytes(filePath, bytes);

            var corruptBefore =
                File.ReadAllBytes(filePath);

            Assert.Throws<InvalidDataException>(
                () =>
                    VorbisDynamicRangeTagWriter.Write(
                        filePath,
                        trackDynamicRange: 10,
                        albumDynamicRange: 11));

            var corruptAfter =
                File.ReadAllBytes(filePath);

            Assert.True(
                corruptBefore
                    .AsSpan()
                    .SequenceEqual(
                        corruptAfter));
        }
        finally
        {
            Directory.Delete(
                tempDirectory,
                recursive: true);
        }
    }

    [Fact]
    public void NonVorbisOgg_IsRejected_AndOriginalRemainsUnchanged()
    {
        var tempDirectory =
            CreateTempDirectory();

        try
        {
            var filePath =
                Path.Combine(
                    tempDirectory,
                    "not-vorbis.ogg");

            const uint serial =
                0x12344321;

            var opusLikePacket =
                "OpusHead-Synthetic"u8.ToArray();

            var page =
                BuildSinglePacketPage(
                    opusLikePacket,
                    serial,
                    sequence: 0,
                    headerType: 0x02,
                    granulePosition: 0);

            File.WriteAllBytes(
                filePath,
                page);

            var before =
                File.ReadAllBytes(filePath);

            Assert.Throws<InvalidDataException>(
                () =>
                    VorbisDynamicRangeTagWriter.Write(
                        filePath,
                        trackDynamicRange: 10,
                        albumDynamicRange: 11));

            var after =
                File.ReadAllBytes(filePath);

            Assert.True(
                before
                    .AsSpan()
                    .SequenceEqual(after));
        }
        finally
        {
            Directory.Delete(
                tempDirectory,
                recursive: true);
        }
    }

    [Fact]
    public void InvalidCommentFramingBit_IsRejected_AndOriginalRemainsUnchanged()
    {
        var tempDirectory =
            CreateTempDirectory();

        try
        {
            var filePath =
                Path.Combine(
                    tempDirectory,
                    "bad-comment-framing.ogg");

            var comment =
                BuildCommentPacket(
                    "Vendor"u8.ToArray(),
                    new[] { Utf8("TITLE=Track") },
                    new byte[] { 0x00 });

            CreateSyntheticVorbisFile(
                filePath,
                comment,
                setupPayloadLength: 2_000,
                audioPacketCount: 1);

            var before =
                File.ReadAllBytes(filePath);

            Assert.Throws<InvalidDataException>(
                () =>
                    VorbisDynamicRangeTagWriter.Write(
                        filePath,
                        trackDynamicRange: 10,
                        albumDynamicRange: 11));

            var after =
                File.ReadAllBytes(filePath);

            Assert.True(
                before
                    .AsSpan()
                    .SequenceEqual(after));
        }
        finally
        {
            Directory.Delete(
                tempDirectory,
                recursive: true);
        }
    }

    private static void CreateSyntheticVorbisFile(
        string filePath,
        byte[] commentPacket,
        int setupPayloadLength,
        int audioPacketCount)
    {
        const uint serial =
            0x4A524456;

        var identification =
            CreateHeaderPacket(
                0x01,
                payloadLength: 23);

        var setup =
            CreateHeaderPacket(
                0x05,
                setupPayloadLength);

        var identificationPage =
            BuildSinglePacketPage(
                identification,
                serial,
                sequence: 0,
                headerType: 0x02,
                granulePosition: 0);

        var headerPages =
            OggVorbisHeaderPageBuilder.Build(
                commentPacket,
                setup,
                serial,
                firstPageSequence: 1);

        using var output =
            new FileStream(
                filePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);

        output.Write(identificationPage);

        foreach (var page in headerPages)
        {
            output.Write(page);
        }

        var sequence =
            checked(1u + (uint)headerPages.Count);

        for (var index = 0;
             index < audioPacketCount;
             index++)
        {
            var audio =
                new byte[100 + index * 17];

            audio[0] = 0x00;

            for (var byteIndex = 1;
                 byteIndex < audio.Length;
                 byteIndex++)
            {
                audio[byteIndex] =
                    checked((byte)((byteIndex + index * 13) % 251));
            }

            var isLast =
                index == audioPacketCount - 1;

            var audioPage =
                BuildSinglePacketPage(
                    audio,
                    serial,
                    sequence,
                    headerType:
                        isLast
                            ? (byte)0x04
                            : (byte)0x00,
                    granulePosition:
                        1024L * (index + 1));

            output.Write(audioPage);
            sequence++;
        }
    }

    private static byte[] CreateHeaderPacket(
        byte type,
        int payloadLength)
    {
        var result =
            new byte[7 + payloadLength];

        result[0] = type;
        "vorbis"u8.CopyTo(
            result.AsSpan(1, 6));

        for (var index = 7;
             index < result.Length;
             index++)
        {
            result[index] =
                checked((byte)(index % 251));
        }

        return result;
    }

    private static byte[] BuildSinglePacketPage(
        byte[] packet,
        uint serial,
        uint sequence,
        byte headerType,
        long granulePosition)
    {
        Assert.True(packet.Length < 255);

        var page =
            new byte[28 + packet.Length];

        "OggS"u8.CopyTo(
            page.AsSpan(0, 4));

        page[4] = 0;
        page[5] = headerType;

        BinaryPrimitives.WriteInt64LittleEndian(
            page.AsSpan(6, 8),
            granulePosition);

        BinaryPrimitives.WriteUInt32LittleEndian(
            page.AsSpan(14, 4),
            serial);

        BinaryPrimitives.WriteUInt32LittleEndian(
            page.AsSpan(18, 4),
            sequence);

        page.AsSpan(22, 4).Clear();
        page[26] = 1;
        page[27] =
            checked((byte)packet.Length);

        packet.CopyTo(
            page,
            28);

        return
            OggPageCodec.WithRecalculatedChecksum(
                page);
    }

    private static byte[] BuildCommentPacket(
        byte[] vendor,
        IReadOnlyList<byte[]> comments,
        byte[] trailingData)
    {
        using var stream =
            new MemoryStream();

        stream.WriteByte(0x03);
        stream.Write("vorbis"u8);

        WriteUInt32(
            stream,
            checked((uint)vendor.Length));

        stream.Write(vendor);

        WriteUInt32(
            stream,
            checked((uint)comments.Count));

        foreach (var comment in comments)
        {
            WriteUInt32(
                stream,
                checked((uint)comment.Length));

            stream.Write(comment);
        }

        stream.Write(trailingData);

        return stream.ToArray();
    }

    private static OggFileSnapshot ReadOggFile(
        string filePath)
    {
        using var input =
            new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

        var pages =
            new List<byte[]>();

        var packets =
            new List<OggPacketSnapshot>();

        using var currentPacket =
            new MemoryStream();

        var packetStartPage = -1;
        var pageIndex = 0;

        while (true)
        {
            var page =
                OggPageCodec.ReadRawPage(input);

            if (page is null)
                break;

            Assert.True(
                OggPageCodec.HasValidChecksum(page));

            pages.Add(page);

            var segmentCount =
                page[26];

            var bodyOffset =
                27 + segmentCount;

            var bodyCursor = 0;

            for (var segmentIndex = 0;
                 segmentIndex < segmentCount;
                 segmentIndex++)
            {
                if (currentPacket.Length == 0)
                {
                    packetStartPage =
                        pageIndex;
                }

                var length =
                    page[27 + segmentIndex];

                currentPacket.Write(
                    page,
                    bodyOffset + bodyCursor,
                    length);

                bodyCursor += length;

                if (length < 255)
                {
                    packets.Add(
                        new OggPacketSnapshot(
                            currentPacket.ToArray(),
                            packetStartPage,
                            pageIndex));

                    currentPacket.SetLength(0);
                    packetStartPage = -1;
                }
            }

            pageIndex++;
        }

        Assert.Equal(
            0,
            currentPacket.Length);

        return new OggFileSnapshot(
            pages,
            packets);
    }

    private static ParsedComment ParseCommentPacket(
        byte[] packet)
    {
        Assert.True(
            packet.AsSpan().StartsWith(
                new byte[] { 0x03, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s' }));

        var offset = 7;

        var vendorLength =
            ReadUInt32(packet, ref offset);

        var vendor =
            ReadBytes(packet, ref offset, vendorLength);

        var count =
            ReadUInt32(packet, ref offset);

        var comments =
            new List<byte[]>();

        for (uint index = 0;
             index < count;
             index++)
        {
            var length =
                ReadUInt32(packet, ref offset);

            comments.Add(
                ReadBytes(packet, ref offset, length));
        }

        return new ParsedComment(
            vendor,
            comments,
            packet.AsSpan(offset).ToArray());
    }

    private static void AssertPacketsEqual(
        IReadOnlyList<byte[]> expected,
        IReadOnlyList<byte[]> actual)
    {
        Assert.Equal(
            expected.Count,
            actual.Count);

        for (var index = 0;
             index < expected.Count;
             index++)
        {
            Assert.True(
                expected[index]
                    .AsSpan()
                    .SequenceEqual(
                        actual[index]));
        }
    }

    private static bool IsOwned(
        byte[] comment)
    {
        return
            IsField(comment, "DYNAMIC RANGE") ||
            IsField(comment, "ALBUM DYNAMIC RANGE");
    }

    private static bool IsField(
        byte[] comment,
        string fieldName)
    {
        var equalsIndex =
            Array.IndexOf(comment, (byte)'=');

        if (equalsIndex <= 0)
            return false;

        return string.Equals(
            Encoding.ASCII.GetString(
                comment,
                0,
                equalsIndex),
            fieldName,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string GetSingleValue(
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
            Array.IndexOf(comment, (byte)'=');

        return Encoding.UTF8.GetString(
            comment,
            equalsIndex + 1,
            comment.Length - equalsIndex - 1);
    }

    private static byte[] Utf8(
        string value)
    {
        return Encoding.UTF8.GetBytes(value);
    }

    private static string CreateTempDirectory()
    {
        var path =
            Path.Combine(
                Path.GetTempPath(),
                "DRAnalyzer-Vorbis-" +
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(path);
        return path;
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

        stream.Write(buffer);
    }

    private static uint ReadUInt32(
        byte[] data,
        ref int offset)
    {
        var value =
            BinaryPrimitives.ReadUInt32LittleEndian(
                data.AsSpan(offset, 4));

        offset += 4;
        return value;
    }

    private static byte[] ReadBytes(
        byte[] data,
        ref int offset,
        uint length)
    {
        var intLength =
            checked((int)length);

        var result =
            data
                .AsSpan(
                    offset,
                    intLength)
                .ToArray();

        offset += intLength;
        return result;
    }

    private sealed record OggPacketSnapshot(
        byte[] Data,
        int StartPageIndex,
        int EndPageIndex);

    private sealed record OggFileSnapshot(
        IReadOnlyList<byte[]> Pages,
        IReadOnlyList<OggPacketSnapshot> Packets);

    private sealed record ParsedComment(
        byte[] Vendor,
        IReadOnlyList<byte[]> Comments,
        byte[] TrailingData);
}
