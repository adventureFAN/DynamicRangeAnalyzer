using System.Buffers.Binary;
using System.Text;
using DRAnalyzer.Core.Tagging;

namespace DRAnalyzer.Tests;

public sealed class WavDynamicRangeTagWriterTests
{
    [Fact]
    public void Write_WithoutId3_AppendsId3V24AndPreservesAllExistingChunksByteExactly()
    {
        var fmt = BuildPcmFmtChunk();
        var info = BuildChunk("LIST", BuildInfoListPayload());
        var oddCustom = BuildChunk("JUNK", new byte[] { 1, 2, 3, 4, 5 }, padByte: 0x7E);
        var data = BuildChunk("data", BuildPcmData());
        var original = BuildWave(fmt, info, oddCustom, data);
        var file = WriteTempWave(original);

        try
        {
            WavDynamicRangeTagWriter.Write(file, 11, 12);
            var modified = File.ReadAllBytes(file);

            AssertRiffChunkPreserved(original, modified, "fmt ");
            AssertRiffChunkPreserved(original, modified, "LIST");
            AssertRiffChunkPreserved(original, modified, "JUNK");
            AssertRiffChunkPreserved(original, modified, "data");

            var id3 = Assert.Single(ParseWave(modified).Chunks, chunk => chunk.Id == "ID3 ");
            var parsed = ParseId3(id3.Payload);
            Assert.Equal(4, parsed.Version);
            Assert.Equal("11", GetOwnedValue(parsed, "DYNAMIC RANGE"));
            Assert.Equal("12", GetOwnedValue(parsed, "ALBUM DYNAMIC RANGE"));
        }
        finally
        {
            DeleteIfExists(file);
        }
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void Write_ExistingId3V23AndV24_PreservesForeignFramesAndRiffChunks(int version)
    {
        var foreignTitle = BuildTextFrame(version, "TIT2", "Synthetic WAV");
        var foreignCover = BuildBinaryFrame(version, "APIC", new byte[] { 0, 1, 2, 3, 4, 5, 6 });
        var replayGain = BuildTxxxFrame(version, "REPLAYGAIN_TRACK_GAIN", "-4.0 dB");
        var oldTrack = BuildTxxxFrame(version, "dynamic range", "7");
        var oldAlbum = BuildTxxxFrame(version, "ALBUM DYNAMIC RANGE", "8");
        var id3 = BuildChunk("ID3 ", BuildId3Tag(version, new[] { foreignTitle, foreignCover, replayGain, oldTrack, oldAlbum }, 128));
        var data = BuildChunk("data", BuildPcmData());
        var original = BuildWave(BuildPcmFmtChunk(), id3, BuildChunk("bext", Encoding.ASCII.GetBytes("broadcast metadata")), data);
        var file = WriteTempWave(original);

        try
        {
            WavDynamicRangeTagWriter.Write(file, 14, 15);
            var modified = File.ReadAllBytes(file);
            var beforeId3 = ParseId3(Assert.Single(ParseWave(original).Chunks, c => c.Id == "ID3 ").Payload);
            var afterId3 = ParseId3(Assert.Single(ParseWave(modified).Chunks, c => c.Id == "ID3 ").Payload);

            Assert.Equal(version, afterId3.Version);
            Assert.Equal("14", GetOwnedValue(afterId3, "DYNAMIC RANGE"));
            Assert.Equal("15", GetOwnedValue(afterId3, "ALBUM DYNAMIC RANGE"));
            AssertForeignFramesEqual(beforeId3, afterId3);
            AssertRiffChunkPreserved(original, modified, "fmt ");
            AssertRiffChunkPreserved(original, modified, "bext");
            AssertRiffChunkPreserved(original, modified, "data");
        }
        finally
        {
            DeleteIfExists(file);
        }
    }

    [Fact]
    public void Write_TrackOnly_PreservesExistingAlbumFrameByteExactly()
    {
        var albumFrame = BuildTxxxFrame(4, "ALBUM DYNAMIC RANGE", "13");
        var id3 = BuildChunk("ID3 ", BuildId3Tag(4, new[] { BuildTextFrame(4, "TPE1", "Artist"), albumFrame }, 64));
        var original = BuildWave(BuildPcmFmtChunk(), id3, BuildChunk("data", BuildPcmData()));
        var file = WriteTempWave(original);

        try
        {
            WavDynamicRangeTagWriter.Write(file, 10, null);
            var parsed = ParseId3(Assert.Single(ParseWave(File.ReadAllBytes(file)).Chunks, c => c.Id == "ID3 ").Payload);
            var preserved = Assert.Single(parsed.Frames, f => IsOwnedTxxx(f, parsed.Version, "ALBUM DYNAMIC RANGE"));
            Assert.Equal(albumFrame, preserved.RawBytes);
            Assert.Equal("10", GetOwnedValue(parsed, "DYNAMIC RANGE"));
        }
        finally
        {
            DeleteIfExists(file);
        }
    }

    [Fact]
    public void Write_DuplicateOwnedFrames_CollapsesEachFieldToOne()
    {
        var id3 = BuildChunk("ID3 ", BuildId3Tag(4, new[]
        {
            BuildTxxxFrame(4, "DYNAMIC RANGE", "5"),
            BuildTxxxFrame(4, "dynamic range", "6"),
            BuildTxxxFrame(4, "ALBUM DYNAMIC RANGE", "7"),
            BuildTxxxFrame(4, "album dynamic range", "8"),
            BuildTextFrame(4, "TIT2", "Title")
        }, 64));
        var file = WriteTempWave(BuildWave(BuildPcmFmtChunk(), id3, BuildChunk("data", BuildPcmData())));

        try
        {
            WavDynamicRangeTagWriter.Write(file, 12, 13);
            var parsed = ParseId3(Assert.Single(ParseWave(File.ReadAllBytes(file)).Chunks, c => c.Id == "ID3 ").Payload);
            Assert.Single(parsed.Frames, f => IsOwnedTxxx(f, parsed.Version, "DYNAMIC RANGE"));
            Assert.Single(parsed.Frames, f => IsOwnedTxxx(f, parsed.Version, "ALBUM DYNAMIC RANGE"));
        }
        finally
        {
            DeleteIfExists(file);
        }
    }

    [Fact]
    public void Remove_WithoutOwnTags_IsByteExactNoOp()
    {
        var id3 = BuildChunk("ID3 ", BuildId3Tag(4, new[] { BuildTextFrame(4, "TIT2", "Title") }, 32));
        var original = BuildWave(BuildPcmFmtChunk(), id3, BuildChunk("data", BuildPcmData()));
        var file = WriteTempWave(original);

        try
        {
            WavDynamicRangeTagWriter.Remove(file);
            Assert.Equal(original, File.ReadAllBytes(file));
        }
        finally
        {
            DeleteIfExists(file);
        }
    }

    [Fact]
    public void WriteThenRemove_WithoutOriginalId3_RestoresOriginalFileByteExactly()
    {
        var original = BuildWave(
            BuildPcmFmtChunk(),
            BuildChunk("LIST", BuildInfoListPayload()),
            BuildChunk("data", BuildPcmData()));
        var file = WriteTempWave(original);

        try
        {
            WavDynamicRangeTagWriter.Write(file, 9, 10);
            Assert.NotEqual(original, File.ReadAllBytes(file));

            WavDynamicRangeTagWriter.Remove(file);
            Assert.Equal(original, File.ReadAllBytes(file));
        }
        finally
        {
            DeleteIfExists(file);
        }
    }

    [Fact]
    public void Remove_WithForeignFrames_PreservesForeignFramesAndOtherChunks()
    {
        var id3 = BuildChunk("ID3 ", BuildId3Tag(4, new[]
        {
            BuildTextFrame(4, "TIT2", "Title"),
            BuildTxxxFrame(4, "REPLAYGAIN_ALBUM_GAIN", "-5 dB"),
            BuildTxxxFrame(4, "DYNAMIC RANGE", "10"),
            BuildTxxxFrame(4, "ALBUM DYNAMIC RANGE", "11")
        }, 96));
        var data = BuildChunk("data", BuildPcmData());
        var original = BuildWave(BuildPcmFmtChunk(), BuildChunk("LIST", BuildInfoListPayload()), id3, data);
        var file = WriteTempWave(original);

        try
        {
            var before = ParseId3(Assert.Single(ParseWave(original).Chunks, c => c.Id == "ID3 ").Payload);
            WavDynamicRangeTagWriter.Remove(file);
            var modified = File.ReadAllBytes(file);
            var after = ParseId3(Assert.Single(ParseWave(modified).Chunks, c => c.Id == "ID3 ").Payload);

            Assert.DoesNotContain(after.Frames, f => IsOwnedTxxx(f, after.Version, "DYNAMIC RANGE") || IsOwnedTxxx(f, after.Version, "ALBUM DYNAMIC RANGE"));
            AssertForeignFramesEqual(before, after);
            AssertRiffChunkPreserved(original, modified, "fmt ");
            AssertRiffChunkPreserved(original, modified, "LIST");
            AssertRiffChunkPreserved(original, modified, "data");
        }
        finally
        {
            DeleteIfExists(file);
        }
    }

    [Fact]
    public void MultipleId3Chunks_AreRejectedAndOriginalRemainsUnchanged()
    {
        var id3 = BuildChunk("ID3 ", BuildId3Tag(4, new[] { BuildTextFrame(4, "TIT2", "One") }, 0));
        var original = BuildWave(BuildPcmFmtChunk(), id3, id3, BuildChunk("data", BuildPcmData()));
        AssertRejectedUnchanged(original, file => WavDynamicRangeTagWriter.Write(file, 10, 11));
    }

    [Theory]
    [InlineData("RF64")]
    [InlineData("BW64")]
    [InlineData("RIFX")]
    public void NonClassicRiffWave_IsRejectedAndOriginalRemainsUnchanged(string marker)
    {
        var original = BuildWave(BuildPcmFmtChunk(), BuildChunk("data", BuildPcmData()));
        Encoding.ASCII.GetBytes(marker).CopyTo(original, 0);
        AssertRejectedUnchanged(original, file => WavDynamicRangeTagWriter.Write(file, 10, 11));
    }

    [Fact]
    public void Id3V22_IsRejectedAndOriginalRemainsUnchanged()
    {
        var badId3 = BuildId3Tag(4, new[] { BuildTextFrame(4, "TIT2", "Title") }, 0);
        badId3[3] = 2;
        var original = BuildWave(BuildPcmFmtChunk(), BuildChunk("ID3 ", badId3), BuildChunk("data", BuildPcmData()));
        AssertRejectedUnchanged(original, file => WavDynamicRangeTagWriter.Write(file, 10, 11));
    }

    [Fact]
    public void Id3SpecialFlags_AreRejectedAndOriginalRemainsUnchanged()
    {
        var badId3 = BuildId3Tag(4, new[] { BuildTextFrame(4, "TIT2", "Title") }, 0);
        badId3[5] = 0x40;
        var original = BuildWave(BuildPcmFmtChunk(), BuildChunk("ID3 ", badId3), BuildChunk("data", BuildPcmData()));
        AssertRejectedUnchanged(original, file => WavDynamicRangeTagWriter.Write(file, 10, 11));
    }

    [Fact]
    public void BrokenChunkSize_IsRejectedAndOriginalRemainsUnchanged()
    {
        var original = BuildWave(BuildPcmFmtChunk(), BuildChunk("data", BuildPcmData()));
        var parsed = ParseWave(original);
        var data = Assert.Single(parsed.Chunks, c => c.Id == "data");
        BinaryPrimitives.WriteUInt32LittleEndian(original.AsSpan(data.HeaderOffset + 4, 4), uint.MaxValue - 1);
        AssertRejectedUnchanged(original, file => WavDynamicRangeTagWriter.Write(file, 10, 11));
    }

    private static void AssertRejectedUnchanged(byte[] original, Action<string> action)
    {
        var file = WriteTempWave(original);
        try
        {
            Assert.ThrowsAny<Exception>(() => action(file));
            Assert.Equal(original, File.ReadAllBytes(file));
        }
        finally
        {
            DeleteIfExists(file);
        }
    }

    private static byte[] BuildWave(params byte[][] chunks)
    {
        var length = 12 + chunks.Sum(chunk => chunk.Length);
        var result = new byte[length];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4, 4), checked((uint)(length - 8)));
        Encoding.ASCII.GetBytes("WAVE").CopyTo(result, 8);
        var offset = 12;
        foreach (var chunk in chunks)
        {
            chunk.CopyTo(result, offset);
            offset += chunk.Length;
        }
        return result;
    }

    private static byte[] BuildPcmFmtChunk()
    {
        var payload = new byte[16];
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 44100);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), 176400);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(12, 2), 4);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(14, 2), 16);
        return BuildChunk("fmt ", payload);
    }

    private static byte[] BuildPcmData() => Enumerable.Range(0, 4096).Select(i => (byte)(i * 37)).ToArray();

    private static byte[] BuildInfoListPayload()
    {
        using var stream = new MemoryStream();
        stream.Write(Encoding.ASCII.GetBytes("INFO"));
        stream.Write(BuildChunk("INAM", Encoding.UTF8.GetBytes("Synthetic Title\0")));
        stream.Write(BuildChunk("IART", Encoding.UTF8.GetBytes("Synthetic Artist\0")));
        return stream.ToArray();
    }

    private static byte[] BuildChunk(string id, byte[] payload, byte padByte = 0)
    {
        var padded = payload.Length + (payload.Length & 1);
        var result = new byte[8 + padded];
        Encoding.ASCII.GetBytes(id).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4, 4), checked((uint)payload.Length));
        payload.CopyTo(result, 8);
        if ((payload.Length & 1) != 0)
            result[^1] = padByte;
        return result;
    }

    private static byte[] BuildId3Tag(int version, IEnumerable<byte[]> frames, int paddingLength)
    {
        var frameBytes = frames.SelectMany(frame => frame).ToArray();
        var body = new byte[frameBytes.Length + paddingLength];
        frameBytes.CopyTo(body, 0);
        var result = new byte[10 + body.Length];
        Encoding.ASCII.GetBytes("ID3").CopyTo(result, 0);
        result[3] = checked((byte)version);
        WriteSynchsafe(result.AsSpan(6, 4), body.Length);
        body.CopyTo(result, 10);
        return result;
    }

    private static byte[] BuildTextFrame(int version, string id, string value)
    {
        var payload = Encoding.UTF8.GetBytes("\u0003" + value);
        return BuildFrame(version, id, payload);
    }

    private static byte[] BuildBinaryFrame(int version, string id, byte[] payload) => BuildFrame(version, id, payload);

    private static byte[] BuildTxxxFrame(int version, string description, string value)
    {
        var encodingByte = version == 3 ? (byte)0 : (byte)3;
        var encoding = version == 3 ? Encoding.Latin1 : Encoding.UTF8;
        var payload = new List<byte> { encodingByte };
        payload.AddRange(encoding.GetBytes(description));
        payload.Add(0);
        payload.AddRange(encoding.GetBytes(value));
        return BuildFrame(version, "TXXX", payload.ToArray());
    }

    private static byte[] BuildFrame(int version, string id, byte[] payload)
    {
        var result = new byte[10 + payload.Length];
        Encoding.ASCII.GetBytes(id).CopyTo(result, 0);
        if (version == 3)
            BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(4, 4), payload.Length);
        else
            WriteSynchsafe(result.AsSpan(4, 4), payload.Length);
        payload.CopyTo(result, 10);
        return result;
    }

    private static void WriteSynchsafe(Span<byte> target, int value)
    {
        target[0] = (byte)((value >> 21) & 0x7F);
        target[1] = (byte)((value >> 14) & 0x7F);
        target[2] = (byte)((value >> 7) & 0x7F);
        target[3] = (byte)(value & 0x7F);
    }

    private static ParsedWave ParseWave(byte[] bytes)
    {
        Assert.True(bytes.Length >= 12);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal(bytes.Length - 8, (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4)));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(bytes, 8, 4));

        var chunks = new List<ParsedChunk>();
        var offset = 12;
        while (offset < bytes.Length)
        {
            var id = Encoding.ASCII.GetString(bytes, offset, 4);
            var size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4, 4)));
            var padded = size + (size & 1);
            Assert.True(offset + 8 + padded <= bytes.Length);
            chunks.Add(new ParsedChunk(id, offset, bytes.AsSpan(offset + 8, size).ToArray(), bytes.AsSpan(offset, 8 + padded).ToArray()));
            offset += 8 + padded;
        }
        Assert.Equal(bytes.Length, offset);
        return new ParsedWave(chunks);
    }

    private static ParsedId3 ParseId3(byte[] payload)
    {
        Assert.True(payload.Length >= 10);
        Assert.Equal("ID3", Encoding.ASCII.GetString(payload, 0, 3));
        var version = payload[3];
        var bodyLength = ReadSynchsafe(payload.AsSpan(6, 4));
        Assert.Equal(payload.Length, 10 + bodyLength);
        var frames = new List<ParsedFrame>();
        var offset = 10;
        var end = 10 + bodyLength;
        while (offset < end && payload[offset] != 0)
        {
            var id = Encoding.ASCII.GetString(payload, offset, 4);
            var size = version == 3
                ? BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(offset + 4, 4))
                : ReadSynchsafe(payload.AsSpan(offset + 4, 4));
            var raw = payload.AsSpan(offset, 10 + size).ToArray();
            frames.Add(new ParsedFrame(id, raw));
            offset += 10 + size;
        }
        return new ParsedId3(version, frames);
    }

    private static int ReadSynchsafe(ReadOnlySpan<byte> bytes)
    {
        var value = 0;
        foreach (var b in bytes)
            value = (value << 7) | b;
        return value;
    }

    private static string GetOwnedValue(ParsedId3 tag, string description)
    {
        var frame = Assert.Single(tag.Frames, f => IsOwnedTxxx(f, tag.Version, description));
        var size = tag.Version == 3
            ? BinaryPrimitives.ReadInt32BigEndian(frame.RawBytes.AsSpan(4, 4))
            : ReadSynchsafe(frame.RawBytes.AsSpan(4, 4));
        var payload = frame.RawBytes.AsSpan(10, size);
        var encoding = payload[0] == 0 ? Encoding.Latin1 : Encoding.UTF8;
        Assert.True(payload[0] is 0 or 3);
        var separator = payload[1..].IndexOf((byte)0);
        Assert.True(separator >= 0);
        return encoding.GetString(payload[(separator + 2)..]);
    }

    private static bool IsOwnedTxxx(ParsedFrame frame, int version, string description)
    {
        if (frame.Id != "TXXX")
            return false;
        var size = version == 3
            ? BinaryPrimitives.ReadInt32BigEndian(frame.RawBytes.AsSpan(4, 4))
            : ReadSynchsafe(frame.RawBytes.AsSpan(4, 4));
        var payload = frame.RawBytes.AsSpan(10, size);
        if (payload.Length < 2 || payload[0] is not 0 and not 3)
            return false;
        var separator = payload[1..].IndexOf((byte)0);
        if (separator < 0)
            return false;
        var encoding = payload[0] == 0 ? Encoding.Latin1 : Encoding.UTF8;
        var actual = encoding.GetString(payload.Slice(1, separator));
        return string.Equals(actual, description, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertForeignFramesEqual(ParsedId3 before, ParsedId3 after)
    {
        var beforeForeign = before.Frames.Where(f => !IsOwnedTxxx(f, before.Version, "DYNAMIC RANGE") && !IsOwnedTxxx(f, before.Version, "ALBUM DYNAMIC RANGE")).Select(f => f.RawBytes).ToArray();
        var afterForeign = after.Frames.Where(f => !IsOwnedTxxx(f, after.Version, "DYNAMIC RANGE") && !IsOwnedTxxx(f, after.Version, "ALBUM DYNAMIC RANGE")).Select(f => f.RawBytes).ToArray();
        Assert.Equal(beforeForeign.Length, afterForeign.Length);
        for (var i = 0; i < beforeForeign.Length; i++)
            Assert.Equal(beforeForeign[i], afterForeign[i]);
    }

    private static void AssertRiffChunkPreserved(byte[] before, byte[] after, string id)
    {
        var beforeChunk = Assert.Single(ParseWave(before).Chunks, c => c.Id == id);
        var afterChunk = Assert.Single(ParseWave(after).Chunks, c => c.Id == id);
        Assert.Equal(beforeChunk.RawBytes, afterChunk.RawBytes);
    }

    private static string WriteTempWave(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"DRAnalyzer-Wav-{Guid.NewGuid():N}.wav");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private sealed record ParsedWave(IReadOnlyList<ParsedChunk> Chunks);
    private sealed record ParsedChunk(string Id, int HeaderOffset, byte[] Payload, byte[] RawBytes);
    private sealed record ParsedId3(int Version, IReadOnlyList<ParsedFrame> Frames);
    private sealed record ParsedFrame(string Id, byte[] RawBytes);
}
