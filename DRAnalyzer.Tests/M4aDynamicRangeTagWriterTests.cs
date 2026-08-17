using System.Buffers.Binary;
using System.Text;
using DRAnalyzer.Core.Tagging;

namespace DRAnalyzer.Tests;

public sealed class M4aDynamicRangeTagWriterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Write_StandardIlst_PreservesForeignMetadataAndMdat(
        bool moovBeforeMdat)
    {
        var filePath = NewTempPath();

        try
        {
            var original =
                BuildSyntheticM4a(
                    moovBeforeMdat,
                    useCo64: false,
                    ownedItems: []);

            File.WriteAllBytes(filePath, original);

            var originalMdat =
                GetTopLevelBox(original, "mdat");

            var originalForeign =
                GetForeignIlstChildren(original);

            M4aDynamicRangeTagWriter.Write(
                filePath,
                trackDynamicRange: 12,
                albumDynamicRange: 13);

            var written =
                File.ReadAllBytes(filePath);

            Assert.Equal(
                originalMdat,
                GetTopLevelBox(written, "mdat"));

            AssertByteArraySequenceEqual(
                originalForeign,
                GetForeignIlstChildren(written));

            var values =
                GetOwnedValues(written);

            Assert.Equal(new[] { "12" }, values.Track);
            Assert.Equal(new[] { "13" }, values.Album);
        }
        finally
        {
            DeleteIfExists(filePath);
        }
    }

    [Fact]
    public void Write_MoovBeforeMdat_AdjustsStcoByExactMoovDelta()
    {
        var filePath = NewTempPath();

        try
        {
            var original =
                BuildSyntheticM4a(
                    moovBeforeMdat: true,
                    useCo64: false,
                    ownedItems: []);

            File.WriteAllBytes(filePath, original);

            var originalMoov =
                GetTopLevelBoxInfo(original, "moov");

            var originalOffset =
                GetSingleChunkOffset(original);

            M4aDynamicRangeTagWriter.Write(
                filePath,
                9,
                10);

            var written =
                File.ReadAllBytes(filePath);

            var writtenMoov =
                GetTopLevelBoxInfo(written, "moov");

            var delta =
                checked(
                    (long)writtenMoov.Size -
                    originalMoov.Size);

            Assert.True(delta > 0);

            Assert.Equal(
                checked(originalOffset + delta),
                GetSingleChunkOffset(written));

            Assert.Equal(
                GetTopLevelBox(original, "mdat"),
                GetTopLevelBox(written, "mdat"));
        }
        finally
        {
            DeleteIfExists(filePath);
        }
    }

    [Fact]
    public void Write_MoovBeforeMdat_AdjustsCo64ByExactMoovDelta()
    {
        var filePath = NewTempPath();

        try
        {
            var original =
                BuildSyntheticM4a(
                    moovBeforeMdat: true,
                    useCo64: true,
                    ownedItems: []);

            File.WriteAllBytes(filePath, original);

            var originalMoov =
                GetTopLevelBoxInfo(original, "moov");

            var originalOffset =
                GetSingleChunkOffset(original);

            M4aDynamicRangeTagWriter.Write(
                filePath,
                9,
                10);

            var written =
                File.ReadAllBytes(filePath);

            var writtenMoov =
                GetTopLevelBoxInfo(written, "moov");

            var delta =
                checked(
                    (long)writtenMoov.Size -
                    originalMoov.Size);

            Assert.Equal(
                checked(originalOffset + delta),
                GetSingleChunkOffset(written));
        }
        finally
        {
            DeleteIfExists(filePath);
        }
    }

    [Fact]
    public void Write_UpdateRemovesDuplicateOwnedItems()
    {
        var filePath = NewTempPath();

        try
        {
            var owned = new[]
            {
                BuildFreeform(
                    "com.apple.iTunes",
                    "DYNAMIC RANGE",
                    "4"),
                BuildFreeform(
                    "legacy.tool",
                    "dynamic range",
                    "5"),
                BuildFreeform(
                    "com.apple.iTunes",
                    "ALBUM DYNAMIC RANGE",
                    "6"),
                BuildFreeform(
                    "legacy.tool",
                    "album dynamic range",
                    "7")
            };

            File.WriteAllBytes(
                filePath,
                BuildSyntheticM4a(
                    moovBeforeMdat: false,
                    useCo64: false,
                    ownedItems: owned));

            M4aDynamicRangeTagWriter.Write(
                filePath,
                14,
                15);

            var values =
                GetOwnedValues(
                    File.ReadAllBytes(filePath));

            Assert.Equal(new[] { "14" }, values.Track);
            Assert.Equal(new[] { "15" }, values.Album);
        }
        finally
        {
            DeleteIfExists(filePath);
        }
    }

    [Fact]
    public void Write_TrackOnly_PreservesExistingAlbumItemByteExactly()
    {
        var filePath = NewTempPath();

        try
        {
            var album =
                BuildFreeform(
                    "legacy.namespace",
                    "ALBUM DYNAMIC RANGE",
                    "17");

            File.WriteAllBytes(
                filePath,
                BuildSyntheticM4a(
                    moovBeforeMdat: false,
                    useCo64: false,
                    ownedItems: [album]));

            M4aDynamicRangeTagWriter.Write(
                filePath,
                trackDynamicRange: 11,
                albumDynamicRange: null);

            var written =
                File.ReadAllBytes(filePath);

            Assert.Contains(
                GetIlstChildren(written),
                child => child.SequenceEqual(album));

            var values =
                GetOwnedValues(written);

            Assert.Equal(new[] { "11" }, values.Track);
            Assert.Equal(new[] { "17" }, values.Album);
        }
        finally
        {
            DeleteIfExists(filePath);
        }
    }

    [Fact]
    public void Remove_RemovesAllOwnedItemsAndPreservesForeignItems()
    {
        var filePath = NewTempPath();

        try
        {
            var owned = new[]
            {
                BuildFreeform(
                    "com.apple.iTunes",
                    "DYNAMIC RANGE",
                    "8"),
                BuildFreeform(
                    "legacy.namespace",
                    "dynamic range",
                    "9"),
                BuildFreeform(
                    "com.apple.iTunes",
                    "ALBUM DYNAMIC RANGE",
                    "10")
            };

            var original =
                BuildSyntheticM4a(
                    moovBeforeMdat: true,
                    useCo64: false,
                    ownedItems: owned);

            File.WriteAllBytes(filePath, original);

            var foreign =
                GetForeignIlstChildren(original);

            var originalMdat =
                GetTopLevelBox(original, "mdat");

            M4aDynamicRangeTagWriter.Remove(filePath);

            var removed =
                File.ReadAllBytes(filePath);

            var values =
                GetOwnedValues(removed);

            Assert.Empty(values.Track);
            Assert.Empty(values.Album);

            AssertByteArraySequenceEqual(
                foreign,
                GetForeignIlstChildren(removed));

            Assert.Equal(
                originalMdat,
                GetTopLevelBox(removed, "mdat"));
        }
        finally
        {
            DeleteIfExists(filePath);
        }
    }

    [Fact]
    public void Remove_WithoutOwnedTags_IsByteExactNoOp()
    {
        var filePath = NewTempPath();

        try
        {
            var original =
                BuildSyntheticM4a(
                    moovBeforeMdat: true,
                    useCo64: false,
                    ownedItems: []);

            File.WriteAllBytes(filePath, original);

            M4aDynamicRangeTagWriter.Remove(filePath);

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
    [InlineData(false)]
    [InlineData(true)]
    public void WriteThenRemove_ExistingStandardIlst_RestoresOriginalFileByteExactly(
        bool moovBeforeMdat)
    {
        var filePath = NewTempPath();

        try
        {
            var original =
                BuildSyntheticM4a(
                    moovBeforeMdat,
                    useCo64: false,
                    ownedItems: []);

            File.WriteAllBytes(filePath, original);

            M4aDynamicRangeTagWriter.Write(
                filePath,
                12,
                13);

            M4aDynamicRangeTagWriter.Remove(filePath);

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
    public void Write_MissingStandardIlst_IsRejectedAndOriginalRemainsUnchanged()
    {
        var filePath = NewTempPath();

        try
        {
            var original =
                BuildSyntheticM4a(
                    moovBeforeMdat: false,
                    useCo64: false,
                    ownedItems: [],
                    includeIlst: false);

            File.WriteAllBytes(filePath, original);

            Assert.Throws<NotSupportedException>(
                () =>
                    M4aDynamicRangeTagWriter.Write(
                        filePath,
                        12,
                        13));

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
    public void Write_FragmentedMp4_IsRejectedAndOriginalRemainsUnchanged()
    {
        var filePath = NewTempPath();

        try
        {
            var original =
                BuildSyntheticM4a(
                    moovBeforeMdat: false,
                    useCo64: false,
                    ownedItems: [],
                    includeMoof: true);

            File.WriteAllBytes(filePath, original);

            Assert.Throws<NotSupportedException>(
                () =>
                    M4aDynamicRangeTagWriter.Write(
                        filePath,
                        12,
                        13));

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
    public void Write_MalformedTopLevelAtom_IsRejectedAndOriginalRemainsUnchanged()
    {
        var filePath = NewTempPath();

        try
        {
            var original =
                BuildSyntheticM4a(
                    moovBeforeMdat: false,
                    useCo64: false,
                    ownedItems: []);

            var malformed =
                original.ToArray();

            var moov =
                GetTopLevelBoxInfo(malformed, "moov");

            BinaryPrimitives.WriteUInt32BigEndian(
                malformed.AsSpan(moov.Offset, 4),
                uint.MaxValue);

            File.WriteAllBytes(filePath, malformed);

            Assert.Throws<InvalidDataException>(
                () =>
                    M4aDynamicRangeTagWriter.Write(
                        filePath,
                        12,
                        13));

            Assert.Equal(
                malformed,
                File.ReadAllBytes(filePath));
        }
        finally
        {
            DeleteIfExists(filePath);
        }
    }


    private static void AssertByteArraySequenceEqual(
        IReadOnlyList<byte[]> expected,
        IReadOnlyList<byte[]> actual)
    {
        Assert.Equal(expected.Count, actual.Count);

        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i], actual[i]);
        }
    }

    private static string NewTempPath()
    {
        return Path.Combine(
            Path.GetTempPath(),
            $"dranalyzer-m4a-{Guid.NewGuid():N}.m4a");
    }

    private static void DeleteIfExists(
        string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private static byte[] BuildSyntheticM4a(
        bool moovBeforeMdat,
        bool useCo64,
        IReadOnlyList<byte[]> ownedItems,
        bool includeIlst = true,
        bool includeMoof = false)
    {
        var ftyp =
            Box(
                "ftyp",
                Concat(
                    Encoding.ASCII.GetBytes("M4A "),
                    new byte[4],
                    Encoding.ASCII.GetBytes("M4A "),
                    Encoding.ASCII.GetBytes("isom")));

        var mdatPayload =
            Enumerable
                .Range(0, 257)
                .Select(i => (byte)(i * 31))
                .ToArray();

        var mdat =
            Box(
                "mdat",
                mdatPayload);

        var moof =
            includeMoof
                ? Box("moof", Box("mfhd", new byte[8]))
                : Array.Empty<byte>();

        var foreignItems =
            new[]
            {
                TextItem(
                    "©nam",
                    "Synthetic Song"),
                TextItem(
                    "©ART",
                    "Synthetic Artist"),
                CoverItem(),
                BuildFreeform(
                    "com.apple.iTunes",
                    "REPLAYGAIN_TRACK_GAIN",
                    "-7.25 dB"),
                BuildFreeform(
                    "custom.namespace",
                    "KEEP ME",
                    "ユニコード ✓")
            };

        var ilstChildren =
            foreignItems
                .Concat(ownedItems)
                .ToArray();

        var ilst =
            Box(
                "ilst",
                Concat(ilstChildren));

        var hdlr = BuildItunesHandler();

        byte[] meta;

        if (includeIlst)
        {
            meta =
                Box(
                    "meta",
                    Concat(
                        new byte[4],
                        hdlr,
                        ilst));
        }
        else
        {
            meta =
                Box(
                    "meta",
                    Concat(
                        new byte[4],
                        hdlr));
        }

        var udta =
            Box(
                "udta",
                meta);

        byte[] BuildMoov(long chunkOffset)
        {
            var offsetBox =
                useCo64
                    ? Co64(chunkOffset)
                    : Stco(chunkOffset);

            var stbl =
                Box(
                    "stbl",
                    offsetBox);

            var minf =
                Box(
                    "minf",
                    stbl);

            var mdia =
                Box(
                    "mdia",
                    minf);

            var trak =
                Box(
                    "trak",
                    mdia);

            return Box(
                "moov",
                Concat(
                    trak,
                    udta));
        }

        if (!moovBeforeMdat)
        {
            var chunkOffset =
                checked((long)ftyp.Length + 8);

            var moov =
                BuildMoov(chunkOffset);

            return Concat(
                ftyp,
                moof,
                mdat,
                moov);
        }

        // Erst Größe des moov bestimmen, dann den realen absoluten
        // Chunk-Offset in den danach folgenden mdat-Payload eintragen.
        var placeholderMoov =
            BuildMoov(0);

        var realChunkOffset =
            checked(
                (long)ftyp.Length +
                moof.Length +
                placeholderMoov.Length +
                8);

        var finalMoov =
            BuildMoov(realChunkOffset);

        Assert.Equal(
            placeholderMoov.Length,
            finalMoov.Length);

        return Concat(
            ftyp,
            moof,
            finalMoov,
            mdat);
    }

    private static byte[] Stco(
        long offset)
    {
        Assert.InRange(
            offset,
            0L,
            (long)uint.MaxValue);

        var payload =
            new byte[12];

        BinaryPrimitives.WriteUInt32BigEndian(
            payload.AsSpan(4, 4),
            1);

        BinaryPrimitives.WriteUInt32BigEndian(
            payload.AsSpan(8, 4),
            checked((uint)offset));

        return Box(
            "stco",
            payload);
    }

    private static byte[] Co64(
        long offset)
    {
        Assert.True(offset >= 0);

        var payload =
            new byte[16];

        BinaryPrimitives.WriteUInt32BigEndian(
            payload.AsSpan(4, 4),
            1);

        BinaryPrimitives.WriteUInt64BigEndian(
            payload.AsSpan(8, 8),
            checked((ulong)offset));

        return Box(
            "co64",
            payload);
    }

    private static byte[] BuildItunesHandler()
    {
        var payload =
            new byte[25];

        Encoding.ASCII
            .GetBytes("mdir")
            .CopyTo(
                payload,
                8);

        Encoding.ASCII
            .GetBytes("appl")
            .CopyTo(
                payload,
                12);

        return Box(
            "hdlr",
            payload);
    }

    private static byte[] TextItem(
        string type,
        string value)
    {
        var valueBytes =
            Encoding.UTF8.GetBytes(value);

        var prefix =
            new byte[8];

        BinaryPrimitives.WriteUInt32BigEndian(
            prefix.AsSpan(0, 4),
            1);

        return Box(
            type,
            Box(
                "data",
                Concat(
                    prefix,
                    valueBytes)));
    }

    private static byte[] CoverItem()
    {
        var fakeJpeg =
            new byte[]
            {
                0xFF, 0xD8, 0xFF, 0xE0,
                0x11, 0x22, 0x33, 0x44,
                0xFF, 0xD9
            };

        var prefix =
            new byte[8];

        BinaryPrimitives.WriteUInt32BigEndian(
            prefix.AsSpan(0, 4),
            13);

        return Box(
            "covr",
            Box(
                "data",
                Concat(
                    prefix,
                    fakeJpeg)));
    }

    private static byte[] BuildFreeform(
        string mean,
        string name,
        string value)
    {
        var meanBox =
            Box(
                "mean",
                Concat(
                    new byte[4],
                    Encoding.UTF8.GetBytes(mean)));

        var nameBox =
            Box(
                "name",
                Concat(
                    new byte[4],
                    Encoding.UTF8.GetBytes(name)));

        var dataPrefix =
            new byte[8];

        BinaryPrimitives.WriteUInt32BigEndian(
            dataPrefix.AsSpan(0, 4),
            1);

        var dataBox =
            Box(
                "data",
                Concat(
                    dataPrefix,
                    Encoding.UTF8.GetBytes(value)));

        return Box(
            "----",
            Concat(
                meanBox,
                nameBox,
                dataBox));
    }

    private static byte[] GetTopLevelBox(
        byte[] file,
        string type)
    {
        var info =
            GetTopLevelBoxInfo(
                file,
                type);

        return file
            .AsSpan(info.Offset, info.Size)
            .ToArray();
    }

    private static TestBox GetTopLevelBoxInfo(
        byte[] file,
        string type)
    {
        return ParseChildren(
                file,
                0,
                file.Length)
            .Single(
                box => box.Type == type);
    }

    private static long GetSingleChunkOffset(
        byte[] file)
    {
        var moov =
            GetTopLevelBoxInfo(
                file,
                "moov");

        var trak =
            Child(file, moov, "trak");

        var mdia =
            Child(file, trak, "mdia");

        var minf =
            Child(file, mdia, "minf");

        var stbl =
            Child(file, minf, "stbl");

        var offsetBox =
            ParseChildren(
                    file,
                    stbl.PayloadOffset,
                    stbl.End)
                .Single(
                    box =>
                        box.Type is "stco" or "co64");

        var count =
            BinaryPrimitives.ReadUInt32BigEndian(
                file.AsSpan(
                    offsetBox.PayloadOffset + 4,
                    4));

        Assert.Equal(1u, count);

        if (offsetBox.Type == "stco")
        {
            return BinaryPrimitives.ReadUInt32BigEndian(
                file.AsSpan(
                    offsetBox.PayloadOffset + 8,
                    4));
        }

        var value =
            BinaryPrimitives.ReadUInt64BigEndian(
                file.AsSpan(
                    offsetBox.PayloadOffset + 8,
                    8));

        Assert.True(value <= long.MaxValue);
        return (long)value;
    }

    private static IReadOnlyList<byte[]> GetIlstChildren(
        byte[] file)
    {
        var ilst =
            LocateIlst(file);

        return ParseChildren(
                file,
                ilst.PayloadOffset,
                ilst.End)
            .Select(
                box =>
                    file.AsSpan(
                            box.Offset,
                            box.Size)
                        .ToArray())
            .ToArray();
    }

    private static IReadOnlyList<byte[]> GetForeignIlstChildren(
        byte[] file)
    {
        return GetIlstChildren(file)
            .Where(
                child =>
                    OwnedName(child) is null)
            .ToArray();
    }

    private static OwnedTestValues GetOwnedValues(
        byte[] file)
    {
        var track =
            new List<string>();

        var album =
            new List<string>();

        foreach (var child in GetIlstChildren(file))
        {
            var name = OwnedName(child);

            if (name is null)
                continue;

            var value =
                FreeformValue(child);

            if (string.Equals(
                    name,
                    "DYNAMIC RANGE",
                    StringComparison.OrdinalIgnoreCase))
            {
                track.Add(value);
            }
            else
            {
                album.Add(value);
            }
        }

        return new OwnedTestValues(
            track,
            album);
    }

    private static string? OwnedName(
        byte[] item)
    {
        var root =
            ParseChildren(
                item,
                0,
                item.Length)
            .Single();

        if (root.Type != "----")
            return null;

        var nameBoxes =
            ParseChildren(
                    item,
                    root.PayloadOffset,
                    root.End)
                .Where(
                    box => box.Type == "name")
                .ToArray();

        if (nameBoxes.Length != 1 ||
            nameBoxes[0].PayloadLength < 4)
        {
            return null;
        }

        var name =
            Encoding.UTF8.GetString(
                item,
                nameBoxes[0].PayloadOffset + 4,
                nameBoxes[0].PayloadLength - 4);

        if (string.Equals(
                name,
                "DYNAMIC RANGE",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                name,
                "ALBUM DYNAMIC RANGE",
                StringComparison.OrdinalIgnoreCase))
        {
            return name;
        }

        return null;
    }

    private static string FreeformValue(
        byte[] item)
    {
        var root =
            ParseChildren(
                item,
                0,
                item.Length)
            .Single();

        var data =
            ParseChildren(
                    item,
                    root.PayloadOffset,
                    root.End)
                .Single(
                    box => box.Type == "data");

        Assert.True(data.PayloadLength >= 8);

        return Encoding.UTF8.GetString(
            item,
            data.PayloadOffset + 8,
            data.PayloadLength - 8);
    }

    private static TestBox LocateIlst(
        byte[] file)
    {
        var moov =
            GetTopLevelBoxInfo(
                file,
                "moov");

        var udta =
            Child(file, moov, "udta");

        var meta =
            Child(file, udta, "meta");

        return ParseChildren(
                file,
                meta.PayloadOffset + 4,
                meta.End)
            .Single(
                box => box.Type == "ilst");
    }

    private static TestBox Child(
        byte[] bytes,
        TestBox parent,
        string type)
    {
        return ParseChildren(
                bytes,
                parent.PayloadOffset,
                parent.End)
            .Single(
                box => box.Type == type);
    }

    private static IReadOnlyList<TestBox> ParseChildren(
        byte[] bytes,
        int start,
        int end)
    {
        var result =
            new List<TestBox>();

        var offset = start;

        while (offset < end)
        {
            Assert.True(end - offset >= 8);

            var size32 =
                BinaryPrimitives.ReadUInt32BigEndian(
                    bytes.AsSpan(offset, 4));

            Assert.NotEqual(0u, size32);

            int size;
            int headerSize;

            if (size32 == 1)
            {
                Assert.True(end - offset >= 16);

                var size64 =
                    BinaryPrimitives.ReadUInt64BigEndian(
                        bytes.AsSpan(offset + 8, 8));

                Assert.True(size64 <= int.MaxValue);
                size = (int)size64;
                headerSize = 16;
            }
            else
            {
                Assert.True(size32 <= int.MaxValue);
                size = (int)size32;
                headerSize = 8;
            }

            Assert.InRange(
                size,
                headerSize,
                end - offset);

            var type =
                Encoding.Latin1.GetString(
                    bytes,
                    offset + 4,
                    4);

            result.Add(
                new TestBox(
                    type,
                    offset,
                    size,
                    headerSize));

            offset += size;
        }

        Assert.Equal(end, offset);
        return result;
    }

    private static byte[] Box(
        string type,
        params byte[][] payloadParts)
    {
        var payload =
            Concat(payloadParts);

        var typeBytes =
            Encoding.Latin1.GetBytes(type);

        Assert.Equal(4, typeBytes.Length);

        var result =
            new byte[8 + payload.Length];

        BinaryPrimitives.WriteUInt32BigEndian(
            result.AsSpan(0, 4),
            checked((uint)result.Length));

        typeBytes.CopyTo(
            result,
            4);

        payload.CopyTo(
            result,
            8);

        return result;
    }

    private static byte[] Concat(
        params byte[][] parts)
    {
        var length =
            parts.Sum(
                part => part.Length);

        var result =
            new byte[length];

        var offset = 0;

        foreach (var part in parts)
        {
            part.CopyTo(
                result,
                offset);

            offset += part.Length;
        }

        return result;
    }

    private readonly record struct TestBox(
        string Type,
        int Offset,
        int Size,
        int HeaderSize)
    {
        public int PayloadOffset => Offset + HeaderSize;
        public int PayloadLength => Size - HeaderSize;
        public int End => Offset + Size;
    }

    private sealed record OwnedTestValues(
        IReadOnlyList<string> Track,
        IReadOnlyList<string> Album);
}
