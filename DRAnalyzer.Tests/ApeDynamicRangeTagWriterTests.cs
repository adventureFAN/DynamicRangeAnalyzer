using System.Buffers.Binary;
using System.Text;
using DRAnalyzer.Core.Tagging;

namespace DRAnalyzer.Tests;

public sealed class ApeDynamicRangeTagWriterTests
{
    private const uint ContainsHeader = 1u << 31;
    private const uint LacksFooter = 1u << 30;
    private const uint IsHeader = 1u << 29;

    [Fact]
    public void Write_WithoutTag_CreatesApeV2AndPreservesMonkeyAudioPayload()
    {
        var original = BuildMonkeyAudioPayload();
        var file = WriteTempApe(original);

        try
        {
            ApeDynamicRangeTagWriter.Write(file, 11, 12);
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
        var payload = BuildMonkeyAudioPayload();
        var original = Combine(payload, BuildApeTag(true, title, artist, cover, replayGain, oldTrack, oldAlbum));
        var file = WriteTempApe(original);

        try
        {
            var before = ParseApeFile(original);
            ApeDynamicRangeTagWriter.Write(file, 13, 14);
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
        var payload = BuildMonkeyAudioPayload();
        var original = Combine(
            payload,
            BuildApeTag(false,
                BuildTextItem("Title", "Footer only"),
                BuildTextItem("DYNAMIC RANGE", "6")));
        var file = WriteTempApe(original);

        try
        {
            ApeDynamicRangeTagWriter.Write(file, 10, 11);
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
        var payload = BuildMonkeyAudioPayload();
        var original = Combine(payload, BuildApeTag(true, BuildTextItem("Artist", "Artist"), albumItem));
        var file = WriteTempApe(original);

        try
        {
            ApeDynamicRangeTagWriter.Write(file, 9, null);
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
            BuildMonkeyAudioPayload(),
            BuildApeTag(true,
                BuildTextItem("DYNAMIC RANGE", "5"),
                BuildTextItem("dynamic range", "6"),
                BuildTextItem("ALBUM DYNAMIC RANGE", "7"),
                BuildTextItem("album dynamic range", "8"),
                BuildTextItem("Title", "Title")));
        var file = WriteTempApe(original);

        try
        {
            ApeDynamicRangeTagWriter.Write(file, 12, 13);
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
            BuildMonkeyAudioPayload(),
            BuildApeTag(true,
                BuildTextItem("Title", "Title"),
                BuildBinaryItem("Cover Art (Front)", "cover.jpg", new byte[] { 9, 8, 7, 6 })));
        var file = WriteTempApe(original);

        try
        {
            ApeDynamicRangeTagWriter.Remove(file);
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
        var original = BuildMonkeyAudioPayload();
        var file = WriteTempApe(original);

        try
        {
            ApeDynamicRangeTagWriter.Write(file, 10, 11);
            Assert.NotEqual(original, File.ReadAllBytes(file));

            ApeDynamicRangeTagWriter.Remove(file);
            Assert.Equal(original, File.ReadAllBytes(file));
        }
        finally
        {
            DeleteIfExists(file);
        }
    }

    [Fact]
    public void WriteThenRemove_WithoutOriginalTag_PreservesTrailingLegacyBytesExactly()
    {
        var payload = Combine(BuildMonkeyAudioPayload(), Encoding.ASCII.GetBytes("TAG" + new string('X', 125)));
        var file = WriteTempApe(payload);

        try
        {
            ApeDynamicRangeTagWriter.Write(file, 10, 11);
            ApeDynamicRangeTagWriter.Remove(file);
            Assert.Equal(payload, File.ReadAllBytes(file));
        }
        finally
        {
            DeleteIfExists(file);
        }
    }

    [Fact]
    public void Remove_WithForeignItems_PreservesForeignItemsAndPayload()
    {
        var payload = BuildMonkeyAudioPayload();
        var title = BuildTextItem("Title", "Title");
        var cover = BuildBinaryItem("Cover Art (Front)", "front.jpg", new byte[] { 1, 4, 9, 16, 25 });
        var original = Combine(
            payload,
            BuildApeTag(true,
                title,
                BuildTextItem("DYNAMIC RANGE", "10"),
                cover,
                BuildTextItem("ALBUM DYNAMIC RANGE", "11")));
        var file = WriteTempApe(original);

        try
        {
            var before = ParseApeFile(original);
            ApeDynamicRangeTagWriter.Remove(file);
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
        var payload = BuildMonkeyAudioPayload();
        var tag = BuildApeTag(false,
            BuildTextItem("Title", "Legacy footer flag"),
            BuildBinaryItem("Cover Art (front)", "cover.jpg", new byte[] { 1, 2, 3, 4, 5 }));

        // Historical real-world quirk: bit 30 claims there is no footer,
        // even though this descriptor is physically the footer at EOF.
        BinaryPrimitives.WriteUInt32LittleEndian(
            tag.AsSpan(tag.Length - 12, 4),
            LacksFooter);

        var original = Combine(payload, tag);
        var file = WriteTempApe(original);

        try
        {
            ApeDynamicRangeTagWriter.Write(file, 12, 13);

            var afterWrite = File.ReadAllBytes(file);
            var footerFlags = BinaryPrimitives.ReadUInt32LittleEndian(
                afterWrite.AsSpan(afterWrite.Length - 12, 4));

            Assert.Equal(LacksFooter, footerFlags);
            Assert.Equal("12", GetTextValue(ParseApeFile(afterWrite), "DYNAMIC RANGE"));
            Assert.Equal("13", GetTextValue(ParseApeFile(afterWrite), "ALBUM DYNAMIC RANGE"));

            ApeDynamicRangeTagWriter.Remove(file);
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
        var original = Combine(BuildMonkeyAudioPayload(), tag);
        AssertRejectedUnchanged(original, file => ApeDynamicRangeTagWriter.Write(file, 10, 11));
    }

    [Fact]
    public void HeaderFooterMismatch_IsRejectedAndOriginalRemainsUnchanged()
    {
        var tag = BuildApeTag(true, BuildTextItem("Title", "Mismatch"));
        BinaryPrimitives.WriteUInt32LittleEndian(tag.AsSpan(16, 4), 999);
        var original = Combine(BuildMonkeyAudioPayload(), tag);
        AssertRejectedUnchanged(original, file => ApeDynamicRangeTagWriter.Write(file, 10, 11));
    }

    [Fact]
    public void BrokenFooterSize_IsRejectedAndOriginalRemainsUnchanged()
    {
        var tag = BuildApeTag(true, BuildTextItem("Title", "Broken"));
        BinaryPrimitives.WriteUInt32LittleEndian(tag.AsSpan(tag.Length - 20, 4), uint.MaxValue);
        var original = Combine(BuildMonkeyAudioPayload(), tag);
        AssertRejectedUnchanged(original, file => ApeDynamicRangeTagWriter.Write(file, 10, 11));
    }

    [Fact]
    public void NonMonkeyAudioFile_IsRejectedAndOriginalRemainsUnchanged()
    {
        var original = BuildMonkeyAudioPayload();
        Encoding.ASCII.GetBytes("NOPE").CopyTo(original, 0);
        AssertRejectedUnchanged(original, file => ApeDynamicRangeTagWriter.Write(file, 10, 11));
    }

    [Fact]
    public void NonZeroReservedDescriptorBytes_AreRejectedAndOriginalRemainsUnchanged()
    {
        var tag = BuildApeTag(true, BuildTextItem("Title", "Reserved"));
        tag[^1] = 1;
        var original = Combine(BuildMonkeyAudioPayload(), tag);
        AssertRejectedUnchanged(original, file => ApeDynamicRangeTagWriter.Write(file, 10, 11));
    }

    private static void AssertRejectedUnchanged(byte[] original, Action<string> action)
    {
        var file = WriteTempApe(original);
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

    private static byte[] BuildMonkeyAudioPayload()
    {
        var payload = new byte[4096];
        Encoding.ASCII.GetBytes("MAC ").CopyTo(payload, 0);

        for (var index = 4; index < payload.Length; index++)
            payload[index] = checked((byte)((index * 37 + 11) & 0xff));

        return payload;
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
        var footerOffset = file.Length - 32;
        Assert.Equal("APETAGEX", Encoding.ASCII.GetString(file, footerOffset, 8));
        Assert.Equal(2000u, BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(footerOffset + 8, 4)));

        var footerSize = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(footerOffset + 12, 4)));
        var count = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(footerOffset + 16, 4)));
        var footerFlags = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(footerOffset + 20, 4));
        var hasHeader = (footerFlags & ContainsHeader) != 0;
        var tagStart = file.Length - footerSize - (hasHeader ? 32 : 0);
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

    private static string WriteTempApe(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"DRAnalyzer-Ape-{Guid.NewGuid():N}.ape");
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
