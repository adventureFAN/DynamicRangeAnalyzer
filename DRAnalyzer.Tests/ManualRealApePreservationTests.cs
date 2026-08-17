using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using DRAnalyzer.Core.Analysis;
using DRAnalyzer.Core.Metadata;
using DRAnalyzer.Core.Tagging;

namespace DRAnalyzer.Tests;

[Trait("Category", "ExternalReference")]
public sealed class ManualRealApePreservationTests
{
    private const uint ApeVersion = 2000;
    private const int DescriptorLength = 32;
    private const uint TagFlagContainsHeader = 1u << 31;
    private const uint TagFlagLacksFooter = 1u << 30;
    private const uint TagFlagIsHeader = 1u << 29;
    private const uint AllowedTagFlags =
        TagFlagContainsHeader |
        TagFlagLacksFooter |
        TagFlagIsHeader;

    private static readonly byte[] MonkeyAudioMarker = Encoding.ASCII.GetBytes("MAC ");
    private static readonly byte[] ApeTagMarker = Encoding.ASCII.GetBytes("APETAGEX");

    [Fact]
    public void ReferenceCopy_WriteAndRemove_PreservesRealApeFile()
    {
        var albumDirectory =
            Environment.GetEnvironmentVariable(
                "DRANALYZER_REFERENCE_APE_DIR");

        Assert.False(
            string.IsNullOrWhiteSpace(albumDirectory),
            "DRANALYZER_REFERENCE_APE_DIR ist nicht gesetzt.");

        Assert.True(
            Directory.Exists(albumDirectory),
            $"APE-Referenzordner fehlt: {albumDirectory}");

        var originalPath =
            FindFirstSupportedApe(albumDirectory!);

        Assert.False(
            string.IsNullOrWhiteSpace(originalPath),
            "Im Referenzordner wurde keine vom aktuellen APE-Writer unterstützte .ape-Datei gefunden.");

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
            GetOwnedValues(beforeSnapshot.Items);

        var tempDirectory =
            Path.Combine(
                Path.GetTempPath(),
                "DRAnalyzer-ApePreservation-" +
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

            ApeDynamicRangeTagWriter.Write(
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

            AssertPrefixPreserved(
                originalBytes,
                beforeSnapshot,
                afterWriteBytes,
                afterWriteSnapshot);

            AssertForeignItemsPreserved(
                beforeSnapshot,
                afterWriteSnapshot);

            var afterWriteOwned =
                GetOwnedValues(afterWriteSnapshot.Items);

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

            ApeDynamicRangeTagWriter.Remove(copyPath);

            AssertNoWriterResidues(tempDirectory);

            var afterRemoveBytes =
                File.ReadAllBytes(copyPath);

            var afterRemoveSnapshot =
                ReadSnapshot(afterRemoveBytes);

            var afterRemoveMetadata =
                AudioMetadataReader.Read(copyPath);

            var afterRemoveAnalysis =
                DynamicRangeAnalyzer.Analyze(copyPath);

            AssertPrefixPreserved(
                originalBytes,
                beforeSnapshot,
                afterRemoveBytes,
                afterRemoveSnapshot);

            AssertForeignItemsPreserved(
                beforeSnapshot,
                afterRemoveSnapshot);

            var afterRemoveOwned =
                GetOwnedValues(afterRemoveSnapshot.Items);

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
                    "Write -> Remove hat die ursprünglich DR-tagfreie APE-Testkopie nicht bytegenau wiederhergestellt.");
            }

            var originalHashAfter =
                CalculateSha256(originalPath!);

            Assert.Equal(
                originalHashBefore,
                originalHashAfter);

            Console.WriteLine(
                $"APE realfile: {Path.GetFileName(originalPath)}");

            Console.WriteLine(
                $"Channels: {beforeAnalysis.Channels}");

            Console.WriteLine(
                $"Monkey's Audio/container prefix bytes preserved: {beforeSnapshot.PrefixLength}");

            Console.WriteLine(
                $"Foreign APEv2 items preserved: {GetForeignItems(beforeSnapshot.Items).Length}");

            Console.WriteLine(
                $"APEv2 tag originally present: {beforeSnapshot.HasTag}");

            Console.WriteLine(
                $"APEv2 header originally present: {beforeSnapshot.HasHeader}");

            Console.WriteLine(
                "Write DR: 20 / Album DR: 21");

            Console.WriteLine(
                "Remove: owned APEv2 DR items removed");

            Console.WriteLine(
                "Re-analysis after Write and Remove successful");

            if (beforeOwned.Track.Count == 0 &&
                beforeOwned.Album.Count == 0)
            {
                Console.WriteLine(
                    "Write -> Remove restored the complete originally DR-tagfree test copy byte-exactly");
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

    private static string? FindFirstSupportedApe(
        string albumDirectory)
    {
        foreach (var path in Directory
                     .EnumerateFiles(
                         albumDirectory,
                         "*.ape",
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

    private static ApeSnapshot ReadSnapshot(
        byte[] bytes)
    {
        if (bytes.Length < 4 ||
            !bytes.AsSpan(0, 4).SequenceEqual(MonkeyAudioMarker))
        {
            throw new InvalidDataException("Datei besitzt keinen gültigen Monkey's-Audio-Marker.");
        }

        if (bytes.Length < DescriptorLength ||
            !bytes.AsSpan(bytes.Length - DescriptorLength, 8).SequenceEqual(ApeTagMarker))
        {
            return new ApeSnapshot(
                bytes.Length,
                HasTag: false,
                HasHeader: false,
                Array.Empty<ApeItemSnapshot>());
        }

        var footerOffset =
            bytes.Length - DescriptorLength;

        var footer =
            ParseDescriptor(
                bytes.AsSpan(footerOffset, DescriptorLength),
                isHeader: false);

        if (footer.Version != ApeVersion)
            throw new NotSupportedException("Nur APEv2 wird unterstützt.");

        if ((footer.Flags & TagFlagIsHeader) != 0)
            throw new InvalidDataException("APEv2-Footer ist als Header markiert.");

        // Real-world APEv2 files may have the historical bit-30
        // "lacks footer" flag set even though this APETAGEX descriptor is
        // physically present as the footer at EOF. The physical footer wins.

        if ((footer.Flags & ~AllowedTagFlags) != 0)
            throw new InvalidDataException("APEv2-Footer enthält unbekannte Flags.");

        if (footer.Size < DescriptorLength)
            throw new InvalidDataException("Ungültige APEv2-Taggröße.");

        var hasHeader =
            (footer.Flags & TagFlagContainsHeader) != 0;

        var totalTagLength =
            checked((long)footer.Size +
                    (hasHeader ? DescriptorLength : 0));

        if (totalTagLength > bytes.Length - 4)
            throw new InvalidDataException("APEv2-Tag ragt über den Dateianfang hinaus.");

        var tagStart =
            checked(bytes.Length - (int)totalTagLength);

        var itemsStart = tagStart;

        if (hasHeader)
        {
            var header =
                ParseDescriptor(
                    bytes.AsSpan(tagStart, DescriptorLength),
                    isHeader: true);

            if (header.Version != footer.Version ||
                header.Size != footer.Size ||
                header.ItemCount != footer.ItemCount)
            {
                throw new InvalidDataException("APEv2-Header und Footer stimmen nicht überein.");
            }

            var expectedHeaderFlags =
                TagFlagContainsHeader |
                TagFlagIsHeader |
                (footer.Flags & TagFlagLacksFooter);

            if (header.Flags != expectedHeaderFlags)
                throw new InvalidDataException("APEv2-Header enthält unerwartete Flags.");

            itemsStart += DescriptorLength;
        }

        var itemBytesLength =
            checked((int)footer.Size - DescriptorLength);

        var items =
            ParseItems(
                bytes.AsSpan(itemsStart, itemBytesLength),
                checked((int)footer.ItemCount));

        return new ApeSnapshot(
            tagStart,
            HasTag: true,
            hasHeader,
            items);
    }

    private static ApeDescriptor ParseDescriptor(
        ReadOnlySpan<byte> raw,
        bool isHeader)
    {
        if (raw.Length != DescriptorLength ||
            !raw[..8].SequenceEqual(ApeTagMarker))
        {
            throw new InvalidDataException(
                isHeader
                    ? "Ungültiger APEv2-Header."
                    : "Ungültiger APEv2-Footer.");
        }

        for (var index = 24; index < 32; index++)
        {
            if (raw[index] != 0)
                throw new InvalidDataException("Reservierte APEv2-Descriptorbytes sind nicht 0.");
        }

        return new ApeDescriptor(
            BinaryPrimitives.ReadUInt32LittleEndian(raw.Slice(8, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(raw.Slice(12, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(raw.Slice(16, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(raw.Slice(20, 4)));
    }

    private static ApeItemSnapshot[] ParseItems(
        ReadOnlySpan<byte> bytes,
        int expectedCount)
    {
        var items =
            new List<ApeItemSnapshot>(expectedCount);

        var offset = 0;

        for (var index = 0;
             index < expectedCount;
             index++)
        {
            if (bytes.Length - offset < 9)
                throw new InvalidDataException("APEv2-Eintrag ist abgeschnitten.");

            var itemStart = offset;

            var valueLength =
                BinaryPrimitives.ReadUInt32LittleEndian(
                    bytes.Slice(offset, 4));

            var flags =
                BinaryPrimitives.ReadUInt32LittleEndian(
                    bytes.Slice(offset + 4, 4));

            offset += 8;

            var keyStart = offset;

            while (offset < bytes.Length &&
                   bytes[offset] != 0)
            {
                if (bytes[offset] < 0x20 ||
                    bytes[offset] > 0x7e)
                {
                    throw new InvalidDataException("APEv2-Key enthält ungültige Zeichen.");
                }

                offset++;
            }

            if (offset >= bytes.Length)
                throw new InvalidDataException("APEv2-Key ist nicht nullterminiert.");

            if (offset == keyStart)
                throw new InvalidDataException("APEv2-Key ist leer.");

            var key =
                Encoding.ASCII.GetString(
                    bytes.Slice(
                        keyStart,
                        offset - keyStart));

            offset++;

            if (valueLength > int.MaxValue ||
                valueLength > bytes.Length - offset)
            {
                throw new InvalidDataException("APEv2-Wert ragt über das Tag-Ende hinaus.");
            }

            var valueLengthInt =
                checked((int)valueLength);

            var valueBytes =
                bytes.Slice(
                        offset,
                        valueLengthInt)
                    .ToArray();

            offset += valueLengthInt;

            var rawBytes =
                bytes.Slice(
                        itemStart,
                        offset - itemStart)
                    .ToArray();

            items.Add(
                new ApeItemSnapshot(
                    key,
                    flags,
                    rawBytes,
                    valueBytes));
        }

        if (offset != bytes.Length)
            throw new InvalidDataException("APEv2-Tag enthält nicht zugeordnete Bytes.");

        return items.ToArray();
    }

    private static void AssertPrefixPreserved(
        byte[] originalBytes,
        ApeSnapshot before,
        byte[] modifiedBytes,
        ApeSnapshot after)
    {
        Assert.Equal(
            before.PrefixLength,
            after.PrefixLength);

        Assert.True(
            originalBytes
                .AsSpan(0, before.PrefixLength)
                .SequenceEqual(
                    modifiedBytes.AsSpan(0, after.PrefixLength)),
            "Der Monkey's-Audio-/Containerbereich vor dem APEv2-Tag wurde verändert.");
    }

    private static void AssertForeignItemsPreserved(
        ApeSnapshot before,
        ApeSnapshot after)
    {
        var expected =
            GetForeignItems(before.Items)
                .Select(item => item.RawBytes)
                .ToArray();

        var actual =
            GetForeignItems(after.Items)
                .Select(item => item.RawBytes)
                .ToArray();

        Assert.Equal(
            expected.Length,
            actual.Length);

        for (var index = 0;
             index < expected.Length;
             index++)
        {
            Assert.True(
                expected[index]
                    .AsSpan()
                    .SequenceEqual(actual[index]),
                $"Fremder APEv2-Eintrag #{index + 1} wurde verändert oder umsortiert.");
        }
    }

    private static ApeItemSnapshot[] GetForeignItems(
        IReadOnlyList<ApeItemSnapshot> items)
    {
        return items
            .Where(item => !IsOwned(item.Key))
            .ToArray();
    }

    private static OwnedValues GetOwnedValues(
        IReadOnlyList<ApeItemSnapshot> items)
    {
        var track =
            items
                .Where(item =>
                    string.Equals(
                        item.Key,
                        "DYNAMIC RANGE",
                        StringComparison.OrdinalIgnoreCase))
                .Select(item =>
                    Encoding.UTF8.GetString(item.ValueBytes))
                .ToArray();

        var album =
            items
                .Where(item =>
                    string.Equals(
                        item.Key,
                        "ALBUM DYNAMIC RANGE",
                        StringComparison.OrdinalIgnoreCase))
                .Select(item =>
                    Encoding.UTF8.GetString(item.ValueBytes))
                .ToArray();

        return new OwnedValues(track, album);
    }

    private static bool IsOwned(string key)
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

    private static void AssertForeignMetadataEqual(
        IReadOnlyDictionary<string, string> before,
        IReadOnlyDictionary<string, string> after)
    {
        var beforeForeign =
            before
                .Where(pair => !IsOwned(pair.Key))
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        var afterForeign =
            after
                .Where(pair => !IsOwned(pair.Key))
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
                .Where(path =>
                    path.Contains(
                        ".dranalyzer.tmp",
                        StringComparison.OrdinalIgnoreCase) ||
                    path.Contains(
                        ".dranalyzer.backup",
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();

        Assert.Empty(residues);
    }

    private static string CalculateSha256(
        string filePath)
    {
        using var stream =
            File.OpenRead(filePath);

        return Convert.ToHexString(
            SHA256.HashData(stream));
    }

    private sealed record ApeSnapshot(
        int PrefixLength,
        bool HasTag,
        bool HasHeader,
        IReadOnlyList<ApeItemSnapshot> Items);

    private sealed record ApeItemSnapshot(
        string Key,
        uint Flags,
        byte[] RawBytes,
        byte[] ValueBytes);

    private readonly record struct ApeDescriptor(
        uint Version,
        uint Size,
        uint ItemCount,
        uint Flags);

    private sealed record OwnedValues(
        IReadOnlyList<string> Track,
        IReadOnlyList<string> Album);
}
