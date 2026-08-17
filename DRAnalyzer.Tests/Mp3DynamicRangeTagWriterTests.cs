using System.Buffers.Binary;
using System.Text;
using DRAnalyzer.Core.Tagging;

namespace DRAnalyzer.Tests;

public sealed class Mp3DynamicRangeTagWriterTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void Write_Id3V23AndV24_PreservesForeignFramesAndPayload(
        int version)
    {
        var titleFrame =
            BuildTextFrame(
                version,
                "TIT2",
                "Synthetic Title");

        var pictureFrame =
            BuildBinaryFrame(
                version,
                "APIC",
                new byte[]
                {
                    0x00, 0x69, 0x6D, 0x61,
                    0x67, 0x65, 0x2F, 0x6A,
                    0x70, 0x65, 0x67, 0x00,
                    0x03, 0x00, 0xFF, 0xD8,
                    0x11, 0x22, 0x33, 0xFF,
                    0xD9
                });

        var replayGainFrame =
            BuildTxxxFrame(
                version,
                "REPLAYGAIN_TRACK_GAIN",
                "-6.25 dB");

        var oldTrackDr =
            BuildTxxxFrame(
                version,
                "dynamic range",
                "7");

        var oldAlbumDr =
            BuildTxxxFrame(
                version,
                "ALBUM DYNAMIC RANGE",
                "8");

        var payload = BuildFakeMp3Payload();

        var original = BuildId3TaggedFile(
            version,
            new[]
            {
                titleFrame,
                pictureFrame,
                replayGainFrame,
                oldTrackDr,
                oldAlbumDr
            },
            paddingLength: 256,
            payload);

        var filePath = WriteTempMp3(original);

        try
        {
            Mp3DynamicRangeTagWriter.Write(
                filePath,
                trackDynamicRange: 12,
                albumDynamicRange: 13);

            var modified =
                File.ReadAllBytes(filePath);

            var before = ParseTag(original);
            var after = ParseTag(modified);

            Assert.Equal(
                version,
                after.Version);

            // Genug Padding war vorhanden: Audio-Offset bleibt gleich.
            Assert.Equal(
                before.PayloadOffset,
                after.PayloadOffset);

            Assert.True(
                original
                    .AsSpan(before.PayloadOffset)
                    .SequenceEqual(
                        modified.AsSpan(after.PayloadOffset)),
                "MPEG-/Trailer-Daten wurden verändert.");

            AssertForeignFramesEqual(
                before,
                after);

            Assert.Equal(
                "12",
                GetSingleOwnedValue(
                    after,
                    "DYNAMIC RANGE"));

            Assert.Equal(
                "13",
                GetSingleOwnedValue(
                    after,
                    "ALBUM DYNAMIC RANGE"));
        }
        finally
        {
            DeleteIfExists(filePath);
        }
    }

    [Fact]
    public void Write_WithoutId3v2_CreatesMinimalId3V24AndPreservesWholeOriginalPayload()
    {
        var original = BuildFakeMp3Payload();
        var filePath = WriteTempMp3(original);

        try
        {
            Mp3DynamicRangeTagWriter.Write(
                filePath,
                trackDynamicRange: 9,
                albumDynamicRange: 10);

            var modified =
                File.ReadAllBytes(filePath);

            var after = ParseTag(modified);

            Assert.Equal(4, after.Version);

            Assert.Equal(
                "9",
                GetSingleOwnedValue(
                    after,
                    "DYNAMIC RANGE"));

            Assert.Equal(
                "10",
                GetSingleOwnedValue(
                    after,
                    "ALBUM DYNAMIC RANGE"));

            Assert.True(
                original.AsSpan().SequenceEqual(
                    modified.AsSpan(after.PayloadOffset)),
                "Die ursprüngliche MP3-Datei wurde hinter dem neu angelegten ID3v2.4-Tag verändert.");
        }
        finally
        {
            DeleteIfExists(filePath);
        }
    }

    [Fact]
    public void WriteThenRemove_WithoutOriginalId3v2_RestoresOriginalFileByteExactly()
    {
        var original = BuildFakeMp3Payload();
        var filePath = WriteTempMp3(original);

        try
        {
            Mp3DynamicRangeTagWriter.Write(
                filePath,
                trackDynamicRange: 12,
                albumDynamicRange: 13);

            Assert.False(
                original.AsSpan().SequenceEqual(
                    File.ReadAllBytes(filePath)),
                "Write sollte einen minimalen ID3v2.4-Tag anlegen.");

            Mp3DynamicRangeTagWriter.Remove(
                filePath);

            Assert.Equal(
                original,
                File.ReadAllBytes(filePath));
        }
        finally
        {
            DeleteIfExists(filePath);
        }
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void Write_TrackOnly_PreservesExistingAlbumDrFrameByteExactly(
        int version)
    {
        var albumFrame =
            BuildTxxxFrame(
                version,
                "ALBUM DYNAMIC RANGE",
                "14");

        var original = BuildId3TaggedFile(
            version,
            new[]
            {
                BuildTextFrame(
                    version,
                    "TPE1",
                    "Artist"),
                albumFrame
            },
            paddingLength: 128,
            BuildFakeMp3Payload());

        var filePath = WriteTempMp3(original);

        try
        {
            Mp3DynamicRangeTagWriter.Write(
                filePath,
                trackDynamicRange: 11,
                albumDynamicRange: null);

            var after =
                ParseTag(
                    File.ReadAllBytes(filePath));

            var preservedAlbumFrame =
                Assert.Single(
                    after.Frames,
                    frame =>
                        IsOwnedTxxx(
                            frame,
                            version,
                            "ALBUM DYNAMIC RANGE"));

            Assert.True(
                albumFrame
                    .AsSpan()
                    .SequenceEqual(
                        preservedAlbumFrame.RawBytes),
                "Vorhandener Album-DR wurde bei Track-only-Write verändert.");

            Assert.Equal(
                "11",
                GetSingleOwnedValue(
                    after,
                    "DYNAMIC RANGE"));
        }
        finally
        {
            DeleteIfExists(filePath);
        }
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void Write_DuplicateOwnedFrames_AreCollapsedToSingleFrames(
        int version)
    {
        var original = BuildId3TaggedFile(
            version,
            new[]
            {
                BuildTxxxFrame(
                    version,
                    "DYNAMIC RANGE",
                    "5"),
                BuildTxxxFrame(
                    version,
                    "dynamic range",
                    "6"),
                BuildTxxxFrame(
                    version,
                    "ALBUM DYNAMIC RANGE",
                    "7"),
                BuildTxxxFrame(
                    version,
                    "album dynamic range",
                    "8")
            },
            paddingLength: 128,
            BuildFakeMp3Payload());

        var filePath = WriteTempMp3(original);

        try
        {
            Mp3DynamicRangeTagWriter.Write(
                filePath,
                trackDynamicRange: 15,
                albumDynamicRange: 16);

            var after =
                ParseTag(
                    File.ReadAllBytes(filePath));

            Assert.Single(
                after.Frames,
                frame =>
                    IsOwnedTxxx(
                        frame,
                        version,
                        "DYNAMIC RANGE"));

            Assert.Single(
                after.Frames,
                frame =>
                    IsOwnedTxxx(
                        frame,
                        version,
                        "ALBUM DYNAMIC RANGE"));

            Assert.Equal(
                "15",
                GetSingleOwnedValue(
                    after,
                    "DYNAMIC RANGE"));

            Assert.Equal(
                "16",
                GetSingleOwnedValue(
                    after,
                    "ALBUM DYNAMIC RANGE"));
        }
        finally
        {
            DeleteIfExists(filePath);
        }
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void Remove_RemovesAllOwnedFramesAndPreservesForeignFramesAndPayload(
        int version)
    {
        var original = BuildId3TaggedFile(
            version,
            new[]
            {
                BuildTextFrame(
                    version,
                    "TALB",
                    "Album"),
                BuildTxxxFrame(
                    version,
                    "CUSTOM FIELD",
                    "Keep me"),
                BuildTxxxFrame(
                    version,
                    "DYNAMIC RANGE",
                    "8"),
                BuildTxxxFrame(
                    version,
                    "dynamic range",
                    "9"),
                BuildTxxxFrame(
                    version,
                    "ALBUM DYNAMIC RANGE",
                    "10"),
                BuildBinaryFrame(
                    version,
                    "PRIV",
                    new byte[]
                    {
                        0x10, 0x20, 0x30,
                        0x40, 0x50
                    })
            },
            paddingLength: 64,
            BuildFakeMp3Payload());

        var filePath = WriteTempMp3(original);

        try
        {
            Mp3DynamicRangeTagWriter.Remove(
                filePath);

            var modified =
                File.ReadAllBytes(filePath);

            var before = ParseTag(original);
            var after = ParseTag(modified);

            Assert.Equal(
                before.PayloadOffset,
                after.PayloadOffset);

            Assert.DoesNotContain(
                after.Frames,
                frame =>
                    IsOwnedTxxx(
                        frame,
                        version,
                        "DYNAMIC RANGE") ||
                    IsOwnedTxxx(
                        frame,
                        version,
                        "ALBUM DYNAMIC RANGE"));

            AssertForeignFramesEqual(
                before,
                after);

            Assert.True(
                original
                    .AsSpan(before.PayloadOffset)
                    .SequenceEqual(
                        modified.AsSpan(after.PayloadOffset)),
                "MPEG-/Trailer-Daten wurden beim Remove verändert.");
        }
        finally
        {
            DeleteIfExists(filePath);
        }
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void Remove_WithoutOwnedFrames_IsByteExactNoOp(
        int version)
    {
        var original = BuildId3TaggedFile(
            version,
            new[]
            {
                BuildTextFrame(
                    version,
                    "TIT2",
                    "Untouched"),
                BuildTxxxFrame(
                    version,
                    "CUSTOM FIELD",
                    "Untouched too")
            },
            paddingLength: 32,
            BuildFakeMp3Payload());

        var filePath = WriteTempMp3(original);

        try
        {
            Mp3DynamicRangeTagWriter.Remove(
                filePath);

            Assert.Equal(
                original,
                File.ReadAllBytes(filePath));
        }
        finally
        {
            DeleteIfExists(filePath);
        }
    }

    [Fact]
    public void Remove_WithoutId3v2_IsByteExactNoOp()
    {
        var original = BuildFakeMp3Payload();
        var filePath = WriteTempMp3(original);

        try
        {
            Mp3DynamicRangeTagWriter.Remove(
                filePath);

            Assert.Equal(
                original,
                File.ReadAllBytes(filePath));
        }
        finally
        {
            DeleteIfExists(filePath);
        }
    }

    [Fact]
    public void Write_Id3V22_IsRejectedAndFileRemainsUnchanged()
    {
        var original = BuildId3V22File();
        var filePath = WriteTempMp3(original);

        try
        {
            Assert.Throws<NotSupportedException>(
                () =>
                    Mp3DynamicRangeTagWriter.Write(
                        filePath,
                        trackDynamicRange: 12,
                        albumDynamicRange: 13));

            Assert.Equal(
                original,
                File.ReadAllBytes(filePath));
        }
        finally
        {
            DeleteIfExists(filePath);
        }
    }

    [Theory]
    [InlineData(3, 0x80)]
    [InlineData(3, 0x40)]
    [InlineData(4, 0x80)]
    [InlineData(4, 0x40)]
    [InlineData(4, 0x10)]
    public void Write_TagHeaderSpecialFlags_AreRejectedAndFileRemainsUnchanged(
        int version,
        byte flags)
    {
        var original = BuildId3TaggedFile(
            version,
            new[]
            {
                BuildTextFrame(
                    version,
                    "TIT2",
                    "Flagged")
            },
            paddingLength: 16,
            BuildFakeMp3Payload(),
            flags);

        var filePath = WriteTempMp3(original);

        try
        {
            Assert.Throws<NotSupportedException>(
                () =>
                    Mp3DynamicRangeTagWriter.Write(
                        filePath,
                        trackDynamicRange: 12,
                        albumDynamicRange: 13));

            Assert.Equal(
                original,
                File.ReadAllBytes(filePath));
        }
        finally
        {
            DeleteIfExists(filePath);
        }
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void Write_FlaggedTxxx_IsRejectedAndFileRemainsUnchanged(
        int version)
    {
        var flaggedTxxx =
            BuildTxxxFrame(
                version,
                "CUSTOM FIELD",
                "Flagged",
                frameFlags: 0x0001);

        var original = BuildId3TaggedFile(
            version,
            new[]
            {
                flaggedTxxx
            },
            paddingLength: 16,
            BuildFakeMp3Payload());

        var filePath = WriteTempMp3(original);

        try
        {
            Assert.Throws<NotSupportedException>(
                () =>
                    Mp3DynamicRangeTagWriter.Write(
                        filePath,
                        trackDynamicRange: 12,
                        albumDynamicRange: 13));

            Assert.Equal(
                original,
                File.ReadAllBytes(filePath));
        }
        finally
        {
            DeleteIfExists(filePath);
        }
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void Write_MalformedFrameSize_IsRejectedAndFileRemainsUnchanged(
        int version)
    {
        var validFrame =
            BuildTextFrame(
                version,
                "TIT2",
                "Broken size");

        // Frame behauptet nun eine Nutzlast von 1 MiB,
        // obwohl der Tag viel kleiner ist.
        if (version == 3)
        {
            BinaryPrimitives.WriteInt32BigEndian(
                validFrame.AsSpan(4, 4),
                1024 * 1024);
        }
        else
        {
            WriteSynchsafe(
                validFrame.AsSpan(4, 4),
                1024 * 1024);
        }

        var original = BuildId3TaggedFile(
            version,
            new[]
            {
                validFrame
            },
            paddingLength: 0,
            BuildFakeMp3Payload());

        var filePath = WriteTempMp3(original);

        try
        {
            Assert.Throws<InvalidDataException>(
                () =>
                    Mp3DynamicRangeTagWriter.Write(
                        filePath,
                        trackDynamicRange: 12,
                        albumDynamicRange: 13));

            Assert.Equal(
                original,
                File.ReadAllBytes(filePath));
        }
        finally
        {
            DeleteIfExists(filePath);
        }
    }

    private static void AssertForeignFramesEqual(
        ParsedTestTag before,
        ParsedTestTag after)
    {
        var beforeForeign =
            before.Frames
                .Where(
                    frame =>
                        !IsOwnedTxxx(
                            frame,
                            before.Version,
                            "DYNAMIC RANGE") &&
                        !IsOwnedTxxx(
                            frame,
                            before.Version,
                            "ALBUM DYNAMIC RANGE"))
                .Select(frame => frame.RawBytes)
                .ToArray();

        var afterForeign =
            after.Frames
                .Where(
                    frame =>
                        !IsOwnedTxxx(
                            frame,
                            after.Version,
                            "DYNAMIC RANGE") &&
                        !IsOwnedTxxx(
                            frame,
                            after.Version,
                            "ALBUM DYNAMIC RANGE"))
                .Select(frame => frame.RawBytes)
                .ToArray();

        Assert.Equal(
            beforeForeign.Length,
            afterForeign.Length);

        for (var index = 0;
             index < beforeForeign.Length;
             index++)
        {
            Assert.True(
                beforeForeign[index]
                    .AsSpan()
                    .SequenceEqual(afterForeign[index]),
                $"Fremder ID3v2-Frame {index} wurde verändert.");
        }
    }

    private static string GetSingleOwnedValue(
        ParsedTestTag tag,
        string description)
    {
        var frame =
            Assert.Single(
                tag.Frames,
                frame =>
                    IsOwnedTxxx(
                        frame,
                        tag.Version,
                        description));

        var parsed =
            ParseTxxx(
                frame,
                tag.Version);

        return parsed.Value;
    }

    private static bool IsOwnedTxxx(
        ParsedTestFrame frame,
        int version,
        string description)
    {
        if (!string.Equals(
                frame.Id,
                "TXXX",
                StringComparison.Ordinal))
        {
            return false;
        }

        var parsed =
            ParseTxxx(
                frame,
                version);

        return string.Equals(
            parsed.Description,
            description,
            StringComparison.OrdinalIgnoreCase);
    }

    private static ParsedTestTxxx ParseTxxx(
        ParsedTestFrame frame,
        int version)
    {
        var payload =
            frame.RawBytes.AsSpan(10);

        Assert.True(payload.Length >= 2);

        var encoding = payload[0];
        var content = payload[1..];

        if (encoding == 0)
        {
            var separator =
                content.IndexOf((byte)0);

            Assert.True(separator >= 0);

            return new ParsedTestTxxx(
                Encoding.Latin1.GetString(
                    content[..separator]),
                Encoding.Latin1.GetString(
                    content[(separator + 1)..]));
        }

        if (encoding == 3 &&
            version == 4)
        {
            var separator =
                content.IndexOf((byte)0);

            Assert.True(separator >= 0);

            return new ParsedTestTxxx(
                Encoding.UTF8.GetString(
                    content[..separator]),
                Encoding.UTF8.GetString(
                    content[(separator + 1)..]));
        }

        throw new InvalidDataException(
            "Testparser unterstützt für eigene synthetische Frames nur Latin-1 bzw. UTF-8.");
    }

    private static ParsedTestTag ParseTag(
        byte[] file)
    {
        Assert.True(file.Length >= 10);
        Assert.Equal((byte)'I', file[0]);
        Assert.Equal((byte)'D', file[1]);
        Assert.Equal((byte)'3', file[2]);

        var version = file[3];

        Assert.True(
            version is 3 or 4);

        var bodyLength =
            ReadSynchsafe(
                file.AsSpan(6, 4));

        var payloadOffset =
            10 + bodyLength;

        Assert.InRange(
            payloadOffset,
            10,
            file.Length);

        var frames =
            new List<ParsedTestFrame>();

        var offset = 10;
        var end = payloadOffset;

        while (offset < end)
        {
            if (file[offset] == 0)
                break;

            Assert.True(
                offset + 10 <= end);

            var id =
                Encoding.ASCII.GetString(
                    file,
                    offset,
                    4);

            var payloadLength =
                version == 3
                    ? BinaryPrimitives.ReadInt32BigEndian(
                        file.AsSpan(
                            offset + 4,
                            4))
                    : ReadSynchsafe(
                        file.AsSpan(
                            offset + 4,
                            4));

            Assert.True(payloadLength > 0);

            var totalLength =
                10 + payloadLength;

            Assert.True(
                offset + totalLength <= end);

            frames.Add(
                new ParsedTestFrame(
                    id,
                    file.AsSpan(
                        offset,
                        totalLength)
                        .ToArray()));

            offset += totalLength;
        }

        return new ParsedTestTag(
            version,
            payloadOffset,
            frames);
    }

    private static byte[] BuildId3TaggedFile(
        int version,
        IEnumerable<byte[]> frames,
        int paddingLength,
        byte[] payload,
        byte flags = 0)
    {
        var frameBytes =
            frames.SelectMany(
                frame => frame)
                .ToArray();

        var bodyLength =
            frameBytes.Length +
            paddingLength;

        var result = new byte[
            10 +
            bodyLength +
            payload.Length];

        result[0] = (byte)'I';
        result[1] = (byte)'D';
        result[2] = (byte)'3';
        result[3] = checked((byte)version);
        result[4] = 0;
        result[5] = flags;

        WriteSynchsafe(
            result.AsSpan(6, 4),
            bodyLength);

        Buffer.BlockCopy(
            frameBytes,
            0,
            result,
            10,
            frameBytes.Length);

        Buffer.BlockCopy(
            payload,
            0,
            result,
            10 + bodyLength,
            payload.Length);

        return result;
    }

    private static byte[] BuildTextFrame(
        int version,
        string frameId,
        string value)
    {
        var encoding =
            version == 3
                ? Encoding.Latin1
                : Encoding.UTF8;

        var encodingByte =
            version == 3
                ? (byte)0
                : (byte)3;

        var text =
            encoding.GetBytes(value);

        var payload = new byte[
            1 + text.Length];

        payload[0] = encodingByte;
        text.CopyTo(payload, 1);

        return BuildFrame(
            version,
            frameId,
            payload,
            frameFlags: 0);
    }

    private static byte[] BuildTxxxFrame(
        int version,
        string description,
        string value,
        ushort frameFlags = 0)
    {
        var encoding =
            version == 3
                ? Encoding.Latin1
                : Encoding.UTF8;

        var encodingByte =
            version == 3
                ? (byte)0
                : (byte)3;

        var descriptionBytes =
            encoding.GetBytes(description);

        var valueBytes =
            encoding.GetBytes(value);

        var payload = new byte[
            1 +
            descriptionBytes.Length +
            1 +
            valueBytes.Length];

        payload[0] = encodingByte;

        descriptionBytes.CopyTo(
            payload,
            1);

        valueBytes.CopyTo(
            payload,
            1 +
            descriptionBytes.Length +
            1);

        return BuildFrame(
            version,
            "TXXX",
            payload,
            frameFlags);
    }

    private static byte[] BuildBinaryFrame(
        int version,
        string frameId,
        byte[] payload)
    {
        return BuildFrame(
            version,
            frameId,
            payload,
            frameFlags: 0);
    }

    private static byte[] BuildFrame(
        int version,
        string frameId,
        byte[] payload,
        ushort frameFlags)
    {
        Assert.Equal(4, frameId.Length);

        var result = new byte[
            10 +
            payload.Length];

        Encoding.ASCII
            .GetBytes(frameId)
            .CopyTo(result, 0);

        if (version == 3)
        {
            BinaryPrimitives.WriteInt32BigEndian(
                result.AsSpan(4, 4),
                payload.Length);
        }
        else
        {
            WriteSynchsafe(
                result.AsSpan(4, 4),
                payload.Length);
        }

        BinaryPrimitives.WriteUInt16BigEndian(
            result.AsSpan(8, 2),
            frameFlags);

        payload.CopyTo(
            result,
            10);

        return result;
    }

    private static byte[] BuildFakeMp3Payload()
    {
        var audio = new byte[4096];

        // Typischer MPEG-1 Layer III Frame-Anfang.
        audio[0] = 0xFF;
        audio[1] = 0xFB;
        audio[2] = 0x90;
        audio[3] = 0x64;

        for (var index = 4;
             index < audio.Length;
             index++)
        {
            audio[index] =
                (byte)((index * 37 + 11) & 0xFF);
        }

        // Synthetischer ID3v1-Trailer. Der MP3-Writer
        // muss auch diesen Bereich bytegenau erhalten.
        var id3v1 = new byte[128];
        id3v1[0] = (byte)'T';
        id3v1[1] = (byte)'A';
        id3v1[2] = (byte)'G';

        Encoding.Latin1
            .GetBytes("Legacy title")
            .CopyTo(id3v1, 3);

        return audio
            .Concat(id3v1)
            .ToArray();
    }

    private static byte[] BuildId3V22File()
    {
        var payload = BuildFakeMp3Payload();
        var body = new byte[]
        {
            (byte)'T', (byte)'T', (byte)'2',
            0x00, 0x00, 0x04,
            0x00, (byte)'T', (byte)'e', (byte)'s'
        };

        var result = new byte[
            10 + body.Length + payload.Length];

        result[0] = (byte)'I';
        result[1] = (byte)'D';
        result[2] = (byte)'3';
        result[3] = 2;
        result[4] = 0;
        result[5] = 0;

        WriteSynchsafe(
            result.AsSpan(6, 4),
            body.Length);

        body.CopyTo(result, 10);
        payload.CopyTo(
            result,
            10 + body.Length);

        return result;
    }

    private static int ReadSynchsafe(
        ReadOnlySpan<byte> bytes)
    {
        Assert.Equal(4, bytes.Length);

        var value = 0;

        foreach (var current in bytes)
        {
            Assert.Equal(0, current & 0x80);

            value =
                (value << 7) |
                current;
        }

        return value;
    }

    private static void WriteSynchsafe(
        Span<byte> destination,
        int value)
    {
        Assert.Equal(4, destination.Length);
        Assert.InRange(value, 0, 0x0FFFFFFF);

        destination[0] =
            (byte)((value >> 21) & 0x7F);

        destination[1] =
            (byte)((value >> 14) & 0x7F);

        destination[2] =
            (byte)((value >> 7) & 0x7F);

        destination[3] =
            (byte)(value & 0x7F);
    }

    private static string WriteTempMp3(
        byte[] bytes)
    {
        var filePath = Path.Combine(
            Path.GetTempPath(),
            $"dranalyzer-mp3-{Guid.NewGuid():N}.mp3");

        File.WriteAllBytes(
            filePath,
            bytes);

        return filePath;
    }

    private static void DeleteIfExists(
        string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    private sealed record ParsedTestTag(
        int Version,
        int PayloadOffset,
        IReadOnlyList<ParsedTestFrame> Frames);

    private sealed record ParsedTestFrame(
        string Id,
        byte[] RawBytes);

    private sealed record ParsedTestTxxx(
        string Description,
        string Value);
}
