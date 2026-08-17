using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using DRAnalyzer.Core.Analysis;
using DRAnalyzer.Core.Metadata;
using DRAnalyzer.Core.Tagging;

namespace DRAnalyzer.Tests;

[Trait("Category", "ExternalReference")]
public sealed class ManualRealM4aPreservationTests
{
    [Fact]
    public void ReferenceCopy_WriteAndRemove_PreservesRealM4aFile()
    {
        var albumDirectory =
            Environment.GetEnvironmentVariable(
                "DRANALYZER_REFERENCE_M4A_DIR");

        Assert.False(
            string.IsNullOrWhiteSpace(albumDirectory),
            "DRANALYZER_REFERENCE_M4A_DIR ist nicht gesetzt.");

        Assert.True(
            Directory.Exists(albumDirectory),
            $"M4A-Referenzordner fehlt: {albumDirectory}");

        var originalPath =
            FindFirstSupportedM4a(albumDirectory!);

        Assert.False(
            string.IsNullOrWhiteSpace(originalPath),
            "Im Referenzordner wurde keine vom aktuellen M4A-Writer unterstützte .m4a-Datei gefunden.");

        var originalHashBefore =
            CalculateSha256(originalPath!);

        var originalBytes =
            File.ReadAllBytes(originalPath!);

        var beforeSnapshot =
            ReadSnapshot(originalBytes);

        var beforeMetadata =
            AudioMetadataReader.Read(originalPath!);

        var beforeAnalysis =
            DynamicRangeAnalyzer.Analyze(originalPath!);

        var beforeOwned =
            GetOwnedValues(beforeSnapshot.IlstChildren);

        var tempDirectory =
            Path.Combine(
                Path.GetTempPath(),
                "DRAnalyzer-M4aPreservation-" +
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(tempDirectory);

        var copyPath =
            Path.Combine(
                tempDirectory,
                Path.GetFileName(originalPath));

        try
        {
            File.Copy(
                originalPath!,
                copyPath,
                overwrite: false);

            M4aDynamicRangeTagWriter.Write(
                copyPath,
                trackDynamicRange: 20,
                albumDynamicRange: 21);

            AssertNoWriterResidues(tempDirectory);

            var afterWriteBytes =
                File.ReadAllBytes(copyPath);

            var afterWriteSnapshot =
                ReadSnapshot(afterWriteBytes);

            var afterWriteMetadata =
                AudioMetadataReader.Read(copyPath);

            var afterWriteAnalysis =
                DynamicRangeAnalyzer.Analyze(copyPath);

            AssertNonMoovTopLevelBoxesPreserved(
                beforeSnapshot,
                afterWriteSnapshot);

            AssertForeignIlstChildrenPreserved(
                beforeSnapshot,
                afterWriteSnapshot);

            var afterWriteOwned =
                GetOwnedValues(
                    afterWriteSnapshot.IlstChildren);

            Assert.Equal(
                new[] { "20" },
                afterWriteOwned.Track);

            Assert.Equal(
                new[] { "21" },
                afterWriteOwned.Album);

            AssertForeignMetadataEqual(
                beforeMetadata.Tags,
                afterWriteMetadata.Tags);

            Assert.Equal(
                "20",
                afterWriteMetadata.DynamicRange);

            Assert.Equal(
                "21",
                afterWriteMetadata.AlbumDynamicRange);

            AssertAnalysisEquivalent(
                beforeAnalysis,
                afterWriteAnalysis);

            M4aDynamicRangeTagWriter.Remove(copyPath);

            AssertNoWriterResidues(tempDirectory);

            var afterRemoveBytes =
                File.ReadAllBytes(copyPath);

            var afterRemoveSnapshot =
                ReadSnapshot(afterRemoveBytes);

            var afterRemoveMetadata =
                AudioMetadataReader.Read(copyPath);

            var afterRemoveAnalysis =
                DynamicRangeAnalyzer.Analyze(copyPath);

            AssertNonMoovTopLevelBoxesPreserved(
                beforeSnapshot,
                afterRemoveSnapshot);

            AssertForeignIlstChildrenPreserved(
                beforeSnapshot,
                afterRemoveSnapshot);

            var afterRemoveOwned =
                GetOwnedValues(
                    afterRemoveSnapshot.IlstChildren);

            Assert.Empty(afterRemoveOwned.Track);
            Assert.Empty(afterRemoveOwned.Album);

            AssertForeignMetadataEqual(
                beforeMetadata.Tags,
                afterRemoveMetadata.Tags);

            Assert.True(
                string.IsNullOrWhiteSpace(
                    afterRemoveMetadata.DynamicRange));

            Assert.True(
                string.IsNullOrWhiteSpace(
                    afterRemoveMetadata.AlbumDynamicRange));

            AssertAnalysisEquivalent(
                beforeAnalysis,
                afterRemoveAnalysis);

            if (beforeOwned.Track.Count == 0 &&
                beforeOwned.Album.Count == 0)
            {
                Assert.True(
                    originalBytes
                        .AsSpan()
                        .SequenceEqual(afterRemoveBytes),
                    "Write -> Remove hat eine ursprünglich DR-taglose M4A-Datei nicht bytegenau wiederhergestellt.");
            }

            var originalHashAfter =
                CalculateSha256(originalPath!);

            Assert.Equal(
                originalHashBefore,
                originalHashAfter);

            Console.WriteLine(
                $"M4A realfile: {Path.GetFileName(originalPath)}");

            Console.WriteLine(
                $"Channels: {beforeAnalysis.Channels}");

            Console.WriteLine(
                $"Foreign ilst items preserved: {GetForeignIlstChildren(beforeSnapshot.IlstChildren).Length}");

            Console.WriteLine(
                $"Non-moov top-level boxes preserved: {beforeSnapshot.TopLevelBoxes.Count(box => box.Type != "moov")}");

            Console.WriteLine(
                "Write DR: 20 / Album DR: 21");

            Console.WriteLine(
                "Remove: owned DR freeform items removed");

            Console.WriteLine(
                "Re-analysis after Write and Remove successful");

            if (beforeOwned.Track.Count == 0 &&
                beforeOwned.Album.Count == 0)
            {
                Console.WriteLine(
                    "Write -> Remove restored the complete test copy byte-exactly");
            }

            Console.WriteLine(
                "Original SHA-256 unchanged");
        }
        finally
        {
            try
            {
                if (File.Exists(copyPath))
                    File.Delete(copyPath);

                if (Directory.Exists(tempDirectory))
                    Directory.Delete(tempDirectory, recursive: true);
            }
            catch
            {
                // Testresultat nicht durch Cleanup-Fehler verdecken.
            }
        }
    }

    private static string? FindFirstSupportedM4a(
        string albumDirectory)
    {
        foreach (var path in Directory
                     .EnumerateFiles(
                         albumDirectory,
                         "*.m4a",
                         SearchOption.AllDirectories)
                     .OrderBy(
                         path => path,
                         StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                _ = ReadSnapshot(File.ReadAllBytes(path));
                return path;
            }
            catch
            {
                // Nicht vom konservativen Stage-1-Scope unterstützte Dateien überspringen.
            }
        }

        return null;
    }

    private static M4aSnapshot ReadSnapshot(
        byte[] file)
    {
        var topLevel =
            ParseChildren(
                file,
                0,
                file.Length,
                allowSizeZero: false);

        Assert.NotEmpty(topLevel);
        Assert.Equal("ftyp", topLevel[0].Type);

        var moov =
            Assert.Single(
                topLevel,
                box => box.Type == "moov");

        Assert.Contains(topLevel, box => box.Type == "mdat");

        Assert.DoesNotContain(
            topLevel,
            box => box.Type is "moof" or "mfra" or "sidx");

        var udta =
            Child(file, moov, "udta");

        var meta =
            Child(file, udta, "meta");

        Assert.True(
            meta.PayloadLength >= 4,
            "Der M4A-meta-Atom ist zu kurz.");

        var ilst =
            ParseChildren(
                    file,
                    meta.PayloadOffset + 4,
                    meta.End,
                    allowSizeZero: false)
                .Single(box => box.Type == "ilst");

        var ilstChildren =
            ParseChildren(
                    file,
                    ilst.PayloadOffset,
                    ilst.End,
                    allowSizeZero: false)
                .Select(
                    box =>
                        file.AsSpan(
                                box.Offset,
                                box.Size)
                            .ToArray())
                .ToArray();

        var topLevelRaw =
            topLevel
                .Select(
                    box =>
                        new RawBox(
                            box.Type,
                            file.AsSpan(
                                    box.Offset,
                                    box.Size)
                                .ToArray()))
                .ToArray();

        return new M4aSnapshot(
            topLevelRaw,
            ilstChildren);
    }

    private static void AssertNonMoovTopLevelBoxesPreserved(
        M4aSnapshot before,
        M4aSnapshot after)
    {
        var beforeForeign =
            before.TopLevelBoxes
                .Where(box => box.Type != "moov")
                .ToArray();

        var afterForeign =
            after.TopLevelBoxes
                .Where(box => box.Type != "moov")
                .ToArray();

        Assert.Equal(
            beforeForeign.Length,
            afterForeign.Length);

        for (var index = 0;
             index < beforeForeign.Length;
             index++)
        {
            Assert.Equal(
                beforeForeign[index].Type,
                afterForeign[index].Type);

            Assert.True(
                beforeForeign[index].Bytes
                    .AsSpan()
                    .SequenceEqual(
                        afterForeign[index].Bytes),
                $"Top-Level-Atom '{beforeForeign[index].Type}' an Position {index} wurde verändert.");
        }
    }

    private static void AssertForeignIlstChildrenPreserved(
        M4aSnapshot before,
        M4aSnapshot after)
    {
        var beforeForeign =
            GetForeignIlstChildren(
                before.IlstChildren);

        var afterForeign =
            GetForeignIlstChildren(
                after.IlstChildren);

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
                    .SequenceEqual(
                        afterForeign[index]),
                $"Fremder ilst-Metadateneintrag {index} wurde verändert oder umsortiert.");
        }
    }

    private static byte[][] GetForeignIlstChildren(
        IReadOnlyList<byte[]> children)
    {
        return children
            .Where(child => OwnedName(child) is null)
            .Select(child => child.ToArray())
            .ToArray();
    }

    private static OwnedValues GetOwnedValues(
        IReadOnlyList<byte[]> children)
    {
        var track =
            new List<string>();

        var album =
            new List<string>();

        foreach (var child in children)
        {
            var name =
                OwnedName(child);

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

        return new OwnedValues(
            track,
            album);
    }

    private static string? OwnedName(
        byte[] item)
    {
        var roots =
            ParseChildren(
                item,
                0,
                item.Length,
                allowSizeZero: false);

        if (roots.Count != 1 ||
            roots[0].Type != "----")
        {
            return null;
        }

        var root =
            roots[0];

        var nameBoxes =
            ParseChildren(
                    item,
                    root.PayloadOffset,
                    root.End,
                    allowSizeZero: false)
                .Where(box => box.Type == "name")
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
                item.Length,
                allowSizeZero: false)
            .Single();

        var data =
            ParseChildren(
                    item,
                    root.PayloadOffset,
                    root.End,
                    allowSizeZero: false)
                .Single(box => box.Type == "data");

        Assert.True(
            data.PayloadLength >= 8,
            "Ein DR-freeform-data-Atom ist zu kurz.");

        var dataType =
            BinaryPrimitives.ReadUInt32BigEndian(
                item.AsSpan(
                    data.PayloadOffset,
                    4));

        Assert.Equal(1u, dataType);

        return Encoding.UTF8.GetString(
            item,
            data.PayloadOffset + 8,
            data.PayloadLength - 8);
    }

    private static void AssertForeignMetadataEqual(
        IReadOnlyDictionary<string, string> before,
        IReadOnlyDictionary<string, string> after)
    {
        var beforeForeign =
            before
                .Where(pair => !IsOwnedMetadataKey(pair.Key))
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        var afterForeign =
            after
                .Where(pair => !IsOwnedMetadataKey(pair.Key))
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        Assert.Equal(
            beforeForeign.Length,
            afterForeign.Length);

        for (var index = 0;
             index < beforeForeign.Length;
             index++)
        {
            Assert.True(
                string.Equals(
                    beforeForeign[index].Key,
                    afterForeign[index].Key,
                    StringComparison.OrdinalIgnoreCase),
                $"Metadaten-Key wurde verändert: '{beforeForeign[index].Key}' -> '{afterForeign[index].Key}'.");

            Assert.Equal(
                beforeForeign[index].Value,
                afterForeign[index].Value);
        }
    }

    private static bool IsOwnedMetadataKey(
        string key)
    {
        return
            string.Equals(
                key,
                "DYNAMIC RANGE",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                key,
                "ALBUM DYNAMIC RANGE",
                StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertAnalysisEquivalent(
        DynamicRangeResult before,
        DynamicRangeResult after)
    {
        Assert.Equal(before.DynamicRange, after.DynamicRange);
        Assert.Equal(before.RoundedDynamicRange, after.RoundedDynamicRange);
        Assert.Equal(before.PeakDb, after.PeakDb);
        Assert.Equal(before.RmsDb, after.RmsDb);
        Assert.Equal(before.Channels, after.Channels);
        Assert.Equal(before.SampleRate, after.SampleRate);
        Assert.Equal(before.BlockCount, after.BlockCount);
        Assert.Equal(before.ChannelDynamicRange, after.ChannelDynamicRange);
        Assert.Equal(before.ChannelPeakDb, after.ChannelPeakDb);
        Assert.Equal(before.ChannelRmsDb, after.ChannelRmsDb);
    }

    private static void AssertNoWriterResidues(
        string directory)
    {
        var residues =
            Directory
                .EnumerateFiles(
                    directory,
                    "*",
                    SearchOption.TopDirectoryOnly)
                .Where(
                    path =>
                        path.Contains(
                            ".dranalyzer.tmp",
                            StringComparison.OrdinalIgnoreCase) ||
                        path.Contains(
                            ".dranalyzer.backup",
                            StringComparison.OrdinalIgnoreCase))
                .ToArray();

        Assert.Empty(residues);
    }

    private static TestBox Child(
        byte[] bytes,
        TestBox parent,
        string type)
    {
        return ParseChildren(
                bytes,
                parent.PayloadOffset,
                parent.End,
                allowSizeZero: false)
            .Single(box => box.Type == type);
    }

    private static IReadOnlyList<TestBox> ParseChildren(
        byte[] bytes,
        int start,
        int end,
        bool allowSizeZero)
    {
        var result =
            new List<TestBox>();

        var offset = start;

        while (offset < end)
        {
            if (end - offset < 8)
                throw new InvalidDataException("MP4-Atom-Header ist abgeschnitten.");

            var size32 =
                BinaryPrimitives.ReadUInt32BigEndian(
                    bytes.AsSpan(offset, 4));

            var type =
                Encoding.Latin1.GetString(
                    bytes,
                    offset + 4,
                    4);

            int size;
            int headerSize;

            if (size32 == 1)
            {
                if (end - offset < 16)
                    throw new InvalidDataException("64-Bit-MP4-Atom-Header ist abgeschnitten.");

                var size64 =
                    BinaryPrimitives.ReadUInt64BigEndian(
                        bytes.AsSpan(offset + 8, 8));

                if (size64 > int.MaxValue)
                    throw new NotSupportedException("Testparser unterstützt keine Einzelatome > 2 GiB.");

                size = (int)size64;
                headerSize = 16;
            }
            else if (size32 == 0)
            {
                if (!allowSizeZero)
                    throw new NotSupportedException($"size=0-Atom '{type}' wird hier nicht unterstützt.");

                size = end - offset;
                headerSize = 8;
            }
            else
            {
                if (size32 > int.MaxValue)
                    throw new NotSupportedException("Testparser unterstützt keine Einzelatome > 2 GiB.");

                size = (int)size32;
                headerSize = 8;
            }

            if (size < headerSize ||
                size > end - offset)
            {
                throw new InvalidDataException(
                    $"MP4-Atom '{type}' besitzt eine ungültige Größe.");
            }

            result.Add(
                new TestBox(
                    type,
                    offset,
                    size,
                    headerSize));

            offset += size;
        }

        if (offset != end)
            throw new InvalidDataException("MP4-Kindatome enden nicht exakt am Containerende.");

        return result;
    }

    private static string CalculateSha256(
        string filePath)
    {
        using var stream =
            File.OpenRead(filePath);

        return Convert.ToHexString(
            SHA256.HashData(stream));
    }

    private sealed record M4aSnapshot(
        IReadOnlyList<RawBox> TopLevelBoxes,
        IReadOnlyList<byte[]> IlstChildren);

    private sealed record RawBox(
        string Type,
        byte[] Bytes);

    private sealed record OwnedValues(
        IReadOnlyList<string> Track,
        IReadOnlyList<string> Album);

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
}
