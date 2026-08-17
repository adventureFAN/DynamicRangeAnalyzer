using System.Buffers.Binary;
using System.Text;
using DRAnalyzer.Core.Tagging;

namespace DRAnalyzer.Tests;

public sealed class WavPackDynamicRangeTagWriterTests
{
    private const uint ContainsHeader = 1u << 31;
    private const uint LacksFooter = 1u << 30;
    private const uint IsHeader = 1u << 29;

    [Fact]
    public void Write_WithoutTag_CreatesApeV2AndPreservesWavPackPayload()
    {
        var original = BuildWavPackPayload();
        var file = WriteTempWavPack(original);

        try
        {
            WavPackDynamicRangeTagWriter.Write(file, 11, 12);
            var modified = File.ReadAllBytes(file);
            var parsed = ParseApeFile(modified);

            Assert.Equal(original, modified.AsSpan(0, original.Length).ToArray());
            Assert.True(parsed.HasHeader);
            Assert.Equal("11", GetTextValue(parsed, "DYNAMIC RANGE"));
            Assert.Equal("12", GetTextValue(parsed, "ALBUM DYNAMIC RANGE"));
        }
        finally
        {
            DeleteIfExists(file);
        }
    }

    [Fact]
    public void Write_ExistingHeaderAndFooter_PreservesForeignItemsByteExactly()
    {
        var title = BuildTextItem("Title", "Synthetic Track");
        var artist = BuildTextItem("Artist", "Synthetic Artist");
        var cover = BuildBinaryItem("Cover Art (Front)", "cover.jpg", new byte[] { 1, 2, 3, 4, 5, 6, 7 });
        var replayGain = BuildTextItem("REPLAYGAIN_TRACK_GAIN", "-5.00 dB");
        var oldTrack = BuildTextItem("dynamic range", "7");
        var oldAlbum = BuildTextItem("ALBUM DYNAMIC RANGE", "8");
        var payload = BuildWavPackPayload();
        var original = Combine(payload, BuildApeTag(true, title, artist, cover, replayGain, oldTrack, oldAlbum));
        var file = WriteTempWavPack(original);

        try
        {
            var before = ParseApeFile(original);
            WavPackDynamicRangeTagWriter.Write(file, 13, 14);
            var modified = File.ReadAllBytes(file);
            var after = ParseApeFile(modified);

            Assert.Equal(payload, modified.AsSpan(0, payload.Length).ToArray());
            Assert.True(after.HasHeader);
            Assert.Equal("13", GetTextValue(after, "DYNAMIC RANGE"));
            Assert.Equal("14", GetTextValue(after, "ALBUM DYNAMIC RANGE"));
            AssertForeignItemsEqual(before, after);
        }
        finally
        {
            DeleteIfExists(file);
        }
    }

    [Fact]
    public void Write_ExistingFooterOnly_PreservesFooterOnlyLayoutAndForeignItems()
    {
        var payload = BuildWavPackPayload();
        var original = Combine(
            payload,
            BuildApeTag(false,
                BuildTextItem("Title", "Footer only"),
                BuildTextItem("DYNAMIC RANGE", "6")));
        var file = WriteTempWavPack(original);

        try
        {
            WavPackDynamicRangeTagWriter.Write(file, 10, 11);
            var parsed = ParseApeFile(File.ReadAllBytes(file));

            Assert.False(parsed.HasHeader);
            Assert.Equal("10", GetTextValue(parsed, "DYNAMIC RANGE"));
            Assert.Equal("11", GetTextValue(parsed, "ALBUM DYNAMIC RANGE"));
            Assert.Equal("Footer only", GetTextValue(parsed, "Title"));
        }
        finally
        {
            DeleteIfExists(file);
        }
    }

    [Fact]
    public void Write_TrackOnly_PreservesExistingAlbumItemByteExactly()
    {
        var albumItem = BuildTextItem("ALBUM DYNAMIC RANGE", "15");
        var payload = BuildWavPackPayload();
        var original = Combine(payload, BuildApeTag(true, BuildTextItem("Artist", "Artist"), albumItem));
        var file = WriteTempWavPack(original);

        try
        {
            WavPackDynamicRangeTagWriter.Write(file, 9, null);
            var parsed = ParseApeFile(File.ReadAllBytes(file));
            var preserved = Assert.Single(parsed.Items, item => IsKey(item, "ALBUM DYNAMIC RANGE"));

            Assert.Equal(albumItem, preserved.RawBytes);
            Assert.Equal("9", GetTextValue(parsed, "DYNAMIC RANGE"));
        }
        finally
        {
            DeleteIfExists(file);
        }
    }

    [Fact]
    public void Write_DuplicateOwnedItems_CollapsesEachFieldToOne()
    {
        var original = Combine(
            BuildWavPackPayload(),
            BuildApeTag(true,
                BuildTextItem("DYNAMIC RANGE", "5"),
                BuildTextItem("dynamic range", "6"),
                BuildTextItem("ALBUM DYNAMIC RANGE", "7"),
                BuildTextItem("album dynamic range", "8"),
                BuildTextItem("Title", "Title")));
        var file = WriteTempWavPack(original);

        try
        {
            WavPackDynamicRangeTagWriter.Write(file, 12, 13);
            var parsed = ParseApeFile(File.ReadAllBytes(file));

            Assert.Single(parsed.Items, item => IsKey(item, "DYNAMIC RANGE"));
            Assert.Single(parsed.Items, item => IsKey(item, "ALBUM DYNAMIC RANGE"));
            Assert.Equal("12", GetTextValue(parsed, "DYNAMIC RANGE"));
            Assert.Equal("13", GetTextValue(parsed, "ALBUM DYNAMIC RANGE"));
        }
        finally
        {
            DeleteIfExists(file);
        }
    }

    [Fact]
    public void Remove_WithoutOwnItems_IsByteExactNoOp()
    {
        var original = Combine(
            BuildWavPackPayload(),
            BuildApeTag(true,
                BuildTextItem("Title", "Title"),
                BuildBinaryItem("Cover Art (Front)", "cover.jpg", new byte[] { 9, 8, 7, 6 })));
        var file = WriteTempWavPack(original);

        try
        {
            WavPackDynamicRangeTagWriter.Remove(file);
            Assert.Equal(original, File.ReadAllBytes(file));
        }
        finally
        {
            DeleteIfExists(file);
        }
    }

    [Fact]
    public void WriteThenRemove_WithoutOriginalTag_RestoresOriginalFileByteExactly()
    {
        var original = BuildWavPackPayload();
        var file = WriteTempWavPack(original);

        try
        {
            WavPackDynamicRangeTagWriter.Write(file, 10, 11);
            Assert.NotEqual(original, File.ReadAllBytes(file));

            WavPackDynamicRangeTagWriter.Remove(file);
            Assert.Equal(original, File.ReadAllBytes(file));
        }
        finally
        {
            DeleteIfExists(file);
        }
    }

    [Fact]
    public void WriteThenRemove_WithTrailingId3v1_PreservesTrailerAtEndAndRestoresOriginalByteExactly()
    {
        var id3v1 = new byte[128];
        Encoding.ASCII.GetBytes("TAG").CopyTo(id3v1, 0);
        Encoding.ASCII.GetBytes("Legacy ID3v1").CopyTo(id3v1, 3);
        var original = Combine(BuildWavPackPayload(), id3v1);
        var file = WriteTempWavPack(original);

        try
        {
            WavPackDynamicRangeTagWriter.Write(file, 10, 11);
            var afterWrite = File.ReadAllBytes(file);

            Assert.Equal(id3v1, afterWrite.AsSpan(afterWrite.Length - 128, 128).ToArray());
            var parsed = ParseApeFile(afterWrite);
            Assert.Equal("10", GetTextValue(parsed, "DYNAMIC RANGE"));
            Assert.Equal("11", GetTextValue(parsed, "ALBUM DYNAMIC RANGE"));

            WavPackDynamicRangeTagWriter.Remove(file);
            Assert.Equal(original, File.ReadAllBytes(file));
        }
        finally
        {
            DeleteIfExists(file);
        }
    }

    [Fact]
    public void Remove_WithForeignItems_PreservesForeignItemsAndPayload()
    {
        var payload = BuildWavPackPayload();
        var title = BuildTextItem("Title", "Title");
        var cover = BuildBinaryItem("Cover Art (Front)", "front.jpg", new byte[] { 1, 4, 9, 16, 25 });
        var original = Combine(
            payload,
            BuildApeTag(true,
                title,
                BuildTextItem("DYNAMIC RANGE", "10"),
                cover,
                BuildTextItem("ALBUM DYNAMIC RANGE", "11")));
        var file = WriteTempWavPack(original);

        try
        {
            var before = ParseApeFile(original);
            WavPackDynamicRangeTagWriter.Remove(file);
            var modified = File.ReadAllBytes(file);
            var after = ParseApeFile(modified);

            Assert.Equal(payload, modified.AsSpan(0, payload.Length).ToArray());
            Assert.DoesNotContain(after.Items, item => IsKey(item, "DYNAMIC RANGE") || IsKey(item, "ALBUM DYNAMIC RANGE"));
            AssertForeignItemsEqual(before, after);
        }
        finally
        {
            DeleteIfExists(file);
        }
    }

    [Fact]
    public void WriteThenRemove_PhysicalFooterWithLegacyLacksFooterFlag_IsSupportedAndRestoresOriginalByteExactly()
    {
        var payload = BuildWavPackPayload();
        var tag = BuildApeTag(false,
            BuildTextItem("Title", "Legacy footer flag"),
            BuildBinaryItem("Cover Art (front)", "cover.jpg", new byte[] { 1, 2, 3, 4, 5 }));

        // Historical real-world quirk: bit 30 claims there is no footer,
        // even though this descriptor is physically the footer at EOF.
        BinaryPrimitives.WriteUInt32LittleEndian(
            tag.AsSpan(tag.Length - 12, 4),
            LacksFooter);

        var original = Combine(payload, tag);
        var file = WriteTempWavPack(original);

        try
        {
            WavPackDynamicRangeTagWriter.Write(file, 12, 13);

            var afterWrite = File.ReadAllBytes(file);
            var footerFlags = BinaryPrimitives.ReadUInt32LittleEndian(
                afterWrite.AsSpan(afterWrite.Length - 12, 4));

            Assert.Equal(LacksFooter, footerFlags);
            Assert.Equal("12", GetTextValue(ParseApeFile(afterWrite), "DYNAMIC RANGE"));
            Assert.Equal("13", GetTextValue(ParseApeFile(afterWrite), "ALBUM DYNAMIC RANGE"));

            WavPackDynamicRangeTagWriter.Remove(file);
            Assert.Equal(original, File.ReadAllBytes(file));
        }
        finally
        {
            DeleteIfExists(file);
        }
    }

    [Fact]
    public void ExistingApeV1_IsRejectedAndOriginalRemainsUnchanged()
    {
        var tag = BuildApeTag(true, BuildTextItem("Title", "Old"));
        BinaryPrimitives.WriteUInt32LittleEndian(tag.AsSpan(8, 4), 1000);
        BinaryPrimitives.WriteUInt32LittleEndian(tag.AsSpan(tag.Length - 24, 4), 1000);
        var original = Combine(BuildWavPackPayload(), tag);
        AssertRejectedUnchanged(original, file => WavPackDynamicRangeTagWriter.Write(file, 10, 11));
    }

    [Fact]
    public void HeaderFooterMismatch_IsRejectedAndOriginalRemainsUnchanged()
    {
        var tag = BuildApeTag(true, BuildTextItem("Title", "Mismatch"));
        BinaryPrimitives.WriteUInt32LittleEndian(tag.AsSpan(16, 4), 999);
        var original = Combine(BuildWavPackPayload(), tag);
        AssertRejectedUnchanged(original, file => WavPackDynamicRangeTagWriter.Write(file, 10, 11));
    }

    [Fact]
    public void BrokenFooterSize_IsRejectedAndOriginalRemainsUnchanged()
    {
        var tag = BuildApeTag(true, BuildTextItem("Title", "Broken"));
        BinaryPrimitives.WriteUInt32LittleEndian(tag.AsSpan(tag.Length - 20, 4), uint.MaxValue);
        var original = Combine(BuildWavPackPayload(), tag);
        AssertRejectedUnchanged(original, file => WavPackDynamicRangeTagWriter.Write(file, 10, 11));
    }

    [Fact]
    public void UnsupportedWavPackVersion_IsRejectedAndOriginalRemainsUnchanged()
    {
        var original = BuildWavPackPayload();
        BinaryPrimitives.WriteUInt16LittleEndian(original.AsSpan(8, 2), 0x0401);
        AssertRejectedUnchanged(original, file => WavPackDynamicRangeTagWriter.Write(file, 10, 11));
    }

    [Fact]
    public void BrokenWavPackBlockSize_IsRejectedAndOriginalRemainsUnchanged()
    {
        var original = BuildWavPackPayload();
        BinaryPrimitives.WriteUInt32LittleEndian(original.AsSpan(4, 4), uint.MaxValue);
        AssertRejectedUnchanged(original, file => WavPackDynamicRangeTagWriter.Write(file, 10, 11));
    }

    [Fact]
    public void NonWavPackFile_IsRejectedAndOriginalRemainsUnchanged()
    {
        var original = BuildWavPackPayload();
        Encoding.ASCII.GetBytes("NOPE").CopyTo(original, 0);
        AssertRejectedUnchanged(original, file => WavPackDynamicRangeTagWriter.Write(file, 10, 11));
    }

    [Fact]
    public void NonZeroReservedDescriptorBytes_AreRejectedAndOriginalRemainsUnchanged()
    {
        var tag = BuildApeTag(true, BuildTextItem("Title", "Reserved"));
        tag[^1] = 1;
        var original = Combine(BuildWavPackPayload(), tag);
        AssertRejectedUnchanged(original, file => WavPackDynamicRangeTagWriter.Write(file, 10, 11));
    }

    private static void AssertRejectedUnchanged(byte[] original, Action<string> action)
    {
        var file = WriteTempWavPack(original);
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

    private static byte[] BuildWavPackPayload()
    {
        return Combine(
            BuildWavPackBlock(160, 0x0410, 17),
            BuildWavPackBlock(224, 0x0410, 41));
    }

    private static byte[] BuildWavPackBlock(int length, ushort version, int seed)
    {
        if (length < 32)
            throw new ArgumentOutOfRangeException(nameof(length));

        var block = new byte[length];
        Encoding.ASCII.GetBytes("wvpk").CopyTo(block, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(4, 4), checked((uint)(length - 8)));
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(8, 2), version);

        for (var index = 10; index < block.Length; index++)
            block[index] = checked((byte)((index * seed + 23) & 0xff));

        return block;
    }

    private static byte[] BuildApeTag(bool includeHeader, params byte[][] items)
    {
        var itemsLength = items.Sum(item => item.Length);
        var footerSize = checked(itemsLength + 32);
        var total = footerSize + (includeHeader ? 32 : 0);
        var result = new byte[total];
        var offset = 0;

        if (includeHeader)
        {
            WriteDescriptor(result.AsSpan(offset, 32), footerSize, items.Length, ContainsHeader | IsHeader);
            offset += 32;
        }

        foreach (var item in items)
        {
            item.CopyTo(result, offset);
            offset += item.Length;
        }

        WriteDescriptor(result.AsSpan(offset, 32), footerSize, items.Length, includeHeader ? ContainsHeader : 0);
        return result;
    }

    private static void WriteDescriptor(Span<byte> destination, int footerSize, int count, uint flags)
    {
        destination.Clear();
        Encoding.ASCII.GetBytes("APETAGEX").CopyTo(destination);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(8, 4), 2000);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(12, 4), checked((uint)footerSize));
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(16, 4), checked((uint)count));
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(20, 4), flags);
    }

    private static byte[] BuildTextItem(string key, string value)
    {
        return BuildItem(key, 0, Encoding.UTF8.GetBytes(value));
    }

    private static byte[] BuildBinaryItem(string key, string filename, byte[] payload)
    {
        var filenameBytes = Encoding.UTF8.GetBytes(filename);
        var value = new byte[filenameBytes.Length + 1 + payload.Length];
        filenameBytes.CopyTo(value, 0);
        payload.CopyTo(value, filenameBytes.Length + 1);
        return BuildItem(key, 1u << 1, value);
    }

    private static byte[] BuildItem(string key, uint flags, byte[] value)
    {
        var keyBytes = Encoding.ASCII.GetBytes(key);
        var result = new byte[8 + keyBytes.Length + 1 + value.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0, 4), checked((uint)value.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4, 4), flags);
        keyBytes.CopyTo(result, 8);
        value.CopyTo(result, 8 + keyBytes.Length + 1);
        return result;
    }

    private static ParsedApe ParseApeFile(byte[] file)
    {
        Assert.True(file.Length >= 32);
        var trailingId3v1 = file.Length >= 128 && Encoding.ASCII.GetString(file, file.Length - 128, 3) == "TAG" ? 128 : 0;
        var effectiveEnd = file.Length - trailingId3v1;
        var footerOffset = effectiveEnd - 32;
        Assert.Equal("APETAGEX", Encoding.ASCII.GetString(file, footerOffset, 8));
        Assert.Equal(2000u, BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(footerOffset + 8, 4)));

        var footerSize = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(footerOffset + 12, 4)));
        var count = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(footerOffset + 16, 4)));
        var footerFlags = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(footerOffset + 20, 4));
        var hasHeader = (footerFlags & ContainsHeader) != 0;
        var tagStart = effectiveEnd - footerSize - (hasHeader ? 32 : 0);
        var itemsStart = tagStart + (hasHeader ? 32 : 0);
        var itemsEnd = footerOffset;
        var items = new List<ParsedItem>();
        var offset = itemsStart;

        for (var index = 0; index < count; index++)
        {
            var start = offset;
            var valueLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(offset, 4)));
            var flags = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(offset + 4, 4));
            offset += 8;
            var keyStart = offset;
            while (file[offset] != 0)
                offset++;
            var key = Encoding.ASCII.GetString(file, keyStart, offset - keyStart);
            offset++;
            var value = file.AsSpan(offset, valueLength).ToArray();
            offset += valueLength;
            items.Add(new ParsedItem(key, flags, file.AsSpan(start, offset - start).ToArray(), value));
        }

        Assert.Equal(itemsEnd, offset);
        return new ParsedApe(hasHeader, tagStart, items);
    }

    private static void AssertForeignItemsEqual(ParsedApe before, ParsedApe after)
    {
        var expected = before.Items.Where(item => !IsOwned(item)).Select(item => item.RawBytes).ToArray();
        var actual = after.Items.Where(item => !IsOwned(item)).Select(item => item.RawBytes).ToArray();
        Assert.Equal(expected.Length, actual.Length);

        for (var index = 0; index < expected.Length; index++)
            Assert.Equal(expected[index], actual[index]);
    }

    private static string GetTextValue(ParsedApe parsed, string key)
    {
        var item = Assert.Single(parsed.Items, item => IsKey(item, key));
        return Encoding.UTF8.GetString(item.ValueBytes);
    }

    private static bool IsOwned(ParsedItem item)
    {
        return IsKey(item, "DYNAMIC RANGE") || IsKey(item, "ALBUM DYNAMIC RANGE");
    }

    private static bool IsKey(ParsedItem item, string key)
    {
        return string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase);
    }

    private static string WriteTempWavPack(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"DRAnalyzer-WavPack-{Guid.NewGuid():N}.wv");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private static byte[] Combine(params byte[][] parts)
    {
        var result = new byte[parts.Sum(part => part.Length)];
        var offset = 0;

        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }

    private sealed record ParsedApe(bool HasHeader, int TagStart, IReadOnlyList<ParsedItem> Items);
    private sealed record ParsedItem(string Key, uint Flags, byte[] RawBytes, byte[] ValueBytes);
}
