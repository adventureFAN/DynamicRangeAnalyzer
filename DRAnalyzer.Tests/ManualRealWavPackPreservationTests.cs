using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using DRAnalyzer.Core.Analysis;
using DRAnalyzer.Core.Metadata;
using DRAnalyzer.Core.Tagging;

namespace DRAnalyzer.Tests;

[Trait("Category", "ExternalReference")]
public sealed class ManualRealWavPackPreservationTests
{
    private const int ApeDescriptorLength = 32;
    private const int Id3v1Length = 128;
    private const uint ApeVersion = 2000;
    private const uint ApeContainsHeader = 1u << 31;
    private const uint ApeLacksFooter = 1u << 30;
    private const uint ApeIsHeader = 1u << 29;
    private const uint AllowedApeFlags = ApeContainsHeader | ApeLacksFooter | ApeIsHeader;

    [Fact]
    public void ReferenceCopy_WriteAndRemove_PreservesRealWavPackFile()
    {
        var referenceDirectory = Environment.GetEnvironmentVariable("DRANALYZER_REFERENCE_WAVPACK_DIR");

        Assert.False(
            string.IsNullOrWhiteSpace(referenceDirectory),
            "DRANALYZER_REFERENCE_WAVPACK_DIR ist nicht gesetzt.");

        Assert.True(
            Directory.Exists(referenceDirectory),
            $"WavPack-Referenzordner fehlt: {referenceDirectory}");

        var candidates = Directory
            .EnumerateFiles(referenceDirectory, "*.wv", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.NotEmpty(candidates);

        string? originalPath = null;
        WavPackSnapshot? originalSnapshot = null;

        foreach (var candidate in candidates)
        {
            try
            {
                var snapshot = ReadSnapshot(candidate);
                originalPath = candidate;
                originalSnapshot = snapshot;
                break;
            }
            catch (Exception exception) when (
                exception is InvalidDataException or
                NotSupportedException or
                EndOfStreamException or
                OverflowException)
            {
                // Nächste reale Referenzdatei probieren. Der eigentliche Test
                // beginnt erst, wenn eine Datei unseren konservativen Scope erfüllt.
            }
        }

        Assert.False(
            string.IsNullOrWhiteSpace(originalPath),
            "Im Referenzordner wurde keine vom aktuellen WavPack-Writer unterstützte .wv-Datei gefunden.");

        Assert.NotNull(originalSnapshot);

        var originalHash = CalculateSha256(originalPath!);
        var beforeMetadata = AudioMetadataReader.Read(originalPath!);
        var beforeAnalysis = DynamicRangeAnalyzer.Analyze(originalPath!);

        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "DRAnalyzer-WavPackPreservation-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(tempDirectory);

        var copyPath = Path.Combine(tempDirectory, Path.GetFileName(originalPath));
        File.Copy(originalPath!, copyPath);

        try
        {
            WavPackDynamicRangeTagWriter.Write(
                copyPath,
                trackDynamicRange: 20,
                albumDynamicRange: 21);

            var afterWrite = ReadSnapshot(copyPath);

            Assert.Equal(originalSnapshot!.WavPackDataLength, afterWrite.WavPackDataLength);
            AssertFileRangeEqual(
                originalPath!,
                copyPath,
                originalSnapshot.WavPackDataLength,
                "Der komplette WavPack-Audiobereich wurde verändert.");

            AssertBytesEqual(
                originalSnapshot.TrailingId3v1,
                afterWrite.TrailingId3v1,
                "Ein vorhandener ID3v1-Trailer wurde verändert.");

            AssertForeignItemsPreserved(originalSnapshot.Tag, afterWrite.Tag);

            Assert.Equal("20", GetSingleTextValue(afterWrite.Tag, "DYNAMIC RANGE"));
            Assert.Equal("21", GetSingleTextValue(afterWrite.Tag, "ALBUM DYNAMIC RANGE"));

            Assert.Single(
                afterWrite.Tag!.Items,
                item => IsKey(item.Key, "DYNAMIC RANGE"));

            Assert.Single(
                afterWrite.Tag.Items,
                item => IsKey(item.Key, "ALBUM DYNAMIC RANGE"));

            var writeMetadata = AudioMetadataReader.Read(copyPath);
            AssertForeignMetadataEqual(beforeMetadata.Tags, writeMetadata.Tags);
            Assert.Equal("20", writeMetadata.DynamicRange);
            Assert.Equal("21", writeMetadata.AlbumDynamicRange);

            var writeAnalysis = DynamicRangeAnalyzer.Analyze(copyPath);
            AssertAnalysisEqual(beforeAnalysis, writeAnalysis);

            WavPackDynamicRangeTagWriter.Remove(copyPath);

            var afterRemove = ReadSnapshot(copyPath);

            Assert.Equal(originalSnapshot.WavPackDataLength, afterRemove.WavPackDataLength);
            AssertFileRangeEqual(
                originalPath!,
                copyPath,
                originalSnapshot.WavPackDataLength,
                "Der WavPack-Audiobereich wurde durch Remove verändert.");

            AssertBytesEqual(
                originalSnapshot.TrailingId3v1,
                afterRemove.TrailingId3v1,
                "Der ID3v1-Trailer wurde durch Remove verändert.");

            AssertForeignItemsPreserved(originalSnapshot.Tag, afterRemove.Tag);
            Assert.DoesNotContain(afterRemove.Tag?.Items ?? [], item => IsOwnedKey(item.Key));

            var removeMetadata = AudioMetadataReader.Read(copyPath);
            AssertForeignMetadataEqual(beforeMetadata.Tags, removeMetadata.Tags);
            Assert.True(string.IsNullOrEmpty(removeMetadata.DynamicRange));
            Assert.True(string.IsNullOrEmpty(removeMetadata.AlbumDynamicRange));

            var removeAnalysis = DynamicRangeAnalyzer.Analyze(copyPath);
            AssertAnalysisEqual(beforeAnalysis, removeAnalysis);

            if (!ContainsOwnedFields(originalSnapshot.Tag))
            {
                Assert.True(
                    FilesAreByteEqual(originalPath!, copyPath),
                    "Write -> Remove stellte die ursprüngliche WavPack-Datei nicht bytegenau wieder her.");
            }

            Assert.False(
                Directory.EnumerateFiles(tempDirectory, ".*.dranalyzer.*", SearchOption.TopDirectoryOnly).Any(),
                "Nach Write/Remove blieben temporäre DRAnalyzer-Dateien zurück.");

            Assert.Equal(originalHash, CalculateSha256(originalPath!));
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static WavPackSnapshot ReadSnapshot(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        if (stream.Length < 32)
            throw new InvalidDataException("Die WavPack-Datei ist zu kurz.");

        var trailingId3v1 = TryReadTrailingId3v1(stream);
        var effectiveEnd = stream.Length - (trailingId3v1?.Length ?? 0);

        ParsedApeTag? tag = null;
        long wavPackDataLength = effectiveEnd;

        if (effectiveEnd >= ApeDescriptorLength)
        {
            stream.Position = effectiveEnd - ApeDescriptorLength;
            var footerRaw = ReadExactly(stream, ApeDescriptorLength);

            if (Encoding.ASCII.GetString(footerRaw, 0, 8) == "APETAGEX")
            {
                var footer = ParseDescriptor(footerRaw, isHeader: false);

                if (footer.Version != ApeVersion)
                    throw new NotSupportedException("Nur APEv2 wird unterstützt.");

                if ((footer.Flags & ApeIsHeader) != 0)
                    throw new InvalidDataException("Der APEv2-Footer ist als Header markiert.");

                if ((footer.Flags & ~AllowedApeFlags) != 0)
                    throw new InvalidDataException("Der APEv2-Footer enthält unbekannte Flags.");

                if (footer.Size < ApeDescriptorLength)
                    throw new InvalidDataException("Der APEv2-Tag meldet eine ungültige Größe.");

                var hasHeader = (footer.Flags & ApeContainsHeader) != 0;
                var totalTagLength = checked((long)footer.Size + (hasHeader ? ApeDescriptorLength : 0));

                if (totalTagLength > effectiveEnd - 32)
                    throw new InvalidDataException("Der APEv2-Tag ragt in den WavPack-Datenbereich.");

                var tagStart = effectiveEnd - totalTagLength;
                var itemBytesLength = checked((int)footer.Size - ApeDescriptorLength);
                byte[]? headerRaw = null;
                long itemsStart = tagStart;

                if (hasHeader)
                {
                    stream.Position = tagStart;
                    headerRaw = ReadExactly(stream, ApeDescriptorLength);
                    var header = ParseDescriptor(headerRaw, isHeader: true);

                    if (header.Version != footer.Version ||
                        header.Size != footer.Size ||
                        header.ItemCount != footer.ItemCount)
                    {
                        throw new InvalidDataException("APEv2-Header und Footer stimmen nicht überein.");
                    }

                    var expectedHeaderFlags =
                        ApeContainsHeader |
                        ApeIsHeader |
                        (footer.Flags & ApeLacksFooter);

                    if (header.Flags != expectedHeaderFlags)
                        throw new InvalidDataException("Der APEv2-Header enthält unerwartete Flags.");

                    itemsStart += ApeDescriptorLength;
                }

                stream.Position = itemsStart;
                var itemBytes = ReadExactly(stream, itemBytesLength);
                var items = ParseItems(itemBytes, checked((int)footer.ItemCount));

                tag = new ParsedApeTag(hasHeader, headerRaw, footerRaw, items);
                wavPackDataLength = tagStart;
            }
        }

        ValidateWavPackBlocks(stream, wavPackDataLength);

        return new WavPackSnapshot(
            wavPackDataLength,
            tag,
            trailingId3v1);
    }

    private static void ValidateWavPackBlocks(FileStream stream, long dataLength)
    {
        const int headerLength = 32;
        long offset = 0;
        var header = new byte[headerLength];

        while (offset < dataLength)
        {
            if (dataLength - offset < headerLength)
                throw new InvalidDataException("Der WavPack-Datenbereich endet mitten im Blockheader.");

            stream.Position = offset;
            ReadExactly(stream, header);

            if (Encoding.ASCII.GetString(header, 0, 4) != "wvpk")
                throw new InvalidDataException("Ungültiger WavPack-Blockmarker.");

            var blockLength = checked((long)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4, 4)) + 8);
            var version = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(8, 2));

            if (blockLength < headerLength)
                throw new InvalidDataException("Ungültige WavPack-Blockgröße.");

            if (version < 0x0402 || version > 0x0410)
                throw new NotSupportedException($"WavPack-Streamversion 0x{version:X4} wird nicht unterstützt.");

            if (blockLength > dataLength - offset)
                throw new InvalidDataException("Ein WavPack-Block ragt über den Datenbereich hinaus.");

            offset += blockLength;
        }

        if (offset != dataLength)
            throw new InvalidDataException("Der WavPack-Datenbereich enthält nicht zugeordnete Bytes.");
    }

    private static byte[]? TryReadTrailingId3v1(FileStream stream)
    {
        if (stream.Length < Id3v1Length)
            return null;

        stream.Position = stream.Length - Id3v1Length;
        var trailer = ReadExactly(stream, Id3v1Length);
        return Encoding.ASCII.GetString(trailer, 0, 3) == "TAG" ? trailer : null;
    }

    private static ApeDescriptor ParseDescriptor(byte[] raw, bool isHeader)
    {
        if (raw.Length != ApeDescriptorLength || Encoding.ASCII.GetString(raw, 0, 8) != "APETAGEX")
            throw new InvalidDataException(isHeader ? "Ungültiger APEv2-Header." : "Ungültiger APEv2-Footer.");

        if (raw.AsSpan(24, 8).ToArray().Any(value => value != 0))
            throw new InvalidDataException("Reservierte APEv2-Descriptorbytes sind nicht 0.");

        return new ApeDescriptor(
            BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(8, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(12, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(16, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(20, 4)));
    }

    private static IReadOnlyList<ApeItem> ParseItems(byte[] bytes, int expectedCount)
    {
        var items = new List<ApeItem>(expectedCount);
        var offset = 0;

        for (var index = 0; index < expectedCount; index++)
        {
            if (bytes.Length - offset < 9)
                throw new InvalidDataException("Ein APEv2-Eintrag ist abgeschnitten.");

            var itemStart = offset;
            var valueLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
            var flags = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4, 4));
            offset += 8;

            var keyStart = offset;
            while (offset < bytes.Length && bytes[offset] != 0)
            {
                if (bytes[offset] < 0x20 || bytes[offset] > 0x7e)
                    throw new InvalidDataException("Ein APEv2-Key enthält ungültige Zeichen.");

                offset++;
            }

            if (offset >= bytes.Length || offset == keyStart)
                throw new InvalidDataException("Ungültiger APEv2-Key.");

            var key = Encoding.ASCII.GetString(bytes, keyStart, offset - keyStart);
            offset++;

            if (valueLength > int.MaxValue || valueLength > bytes.Length - offset)
                throw new InvalidDataException("Ein APEv2-Wert ragt über das Tag-Ende hinaus.");

            var valueLengthInt = checked((int)valueLength);
            var value = bytes.AsSpan(offset, valueLengthInt).ToArray();
            offset += valueLengthInt;

            items.Add(new ApeItem(
                key,
                flags,
                bytes.AsSpan(itemStart, offset - itemStart).ToArray(),
                value));
        }

        if (offset != bytes.Length)
            throw new InvalidDataException("Der APEv2-Tag enthält nicht zugeordnete Bytes.");

        return items;
    }

    private static void AssertForeignItemsPreserved(ParsedApeTag? before, ParsedApeTag? after)
    {
        var expected = (before?.Items ?? [])
            .Where(item => !IsOwnedKey(item.Key))
            .Select(item => item.RawBytes)
            .ToArray();

        var actual = (after?.Items ?? [])
            .Where(item => !IsOwnedKey(item.Key))
            .Select(item => item.RawBytes)
            .ToArray();

        Assert.Equal(expected.Length, actual.Length);

        for (var index = 0; index < expected.Length; index++)
        {
            Assert.Equal(expected[index], actual[index]);
        }
    }

    private static string GetSingleTextValue(ParsedApeTag? tag, string key)
    {
        Assert.NotNull(tag);
        var item = Assert.Single(tag!.Items, item => IsKey(item.Key, key));
        return Encoding.UTF8.GetString(item.ValueBytes);
    }

    private static bool ContainsOwnedFields(ParsedApeTag? tag)
    {
        return tag?.Items.Any(item => IsOwnedKey(item.Key)) == true;
    }

    private static bool IsOwnedKey(string key)
    {
        return IsKey(key, "DYNAMIC RANGE") || IsKey(key, "ALBUM DYNAMIC RANGE");
    }

    private static bool IsKey(string actual, string expected)
    {
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertForeignMetadataEqual(
        IReadOnlyDictionary<string, string> expected,
        IReadOnlyDictionary<string, string> actual)
    {
        var expectedForeign = expected
            .Where(pair => !IsOwnedKey(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pair => pair.Value, StringComparer.Ordinal)
            .ToArray();

        var actualForeign = actual
            .Where(pair => !IsOwnedKey(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pair => pair.Value, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedForeign.Length, actualForeign.Length);

        for (var index = 0; index < expectedForeign.Length; index++)
        {
            Assert.True(
                string.Equals(expectedForeign[index].Key, actualForeign[index].Key, StringComparison.OrdinalIgnoreCase),
                $"Metadaten-Key {index} wurde verändert: '{expectedForeign[index].Key}' -> '{actualForeign[index].Key}'.");
            Assert.Equal(expectedForeign[index].Value, actualForeign[index].Value);
        }
    }

    private static void AssertAnalysisEqual(DynamicRangeResult expected, DynamicRangeResult actual)
    {
        Assert.Equal(expected.DynamicRange, actual.DynamicRange);
        Assert.Equal(expected.RoundedDynamicRange, actual.RoundedDynamicRange);
        Assert.Equal(expected.PeakDb, actual.PeakDb);
        Assert.Equal(expected.RmsDb, actual.RmsDb);
        Assert.Equal(expected.Channels, actual.Channels);
        Assert.Equal(expected.SampleRate, actual.SampleRate);
        Assert.Equal(expected.BlockCount, actual.BlockCount);
        Assert.Equal(expected.ChannelDynamicRange, actual.ChannelDynamicRange);
        Assert.Equal(expected.ChannelPeakDb, actual.ChannelPeakDb);
        Assert.Equal(expected.ChannelRmsDb, actual.ChannelRmsDb);
    }

    private static void AssertFileRangeEqual(
        string expectedPath,
        string actualPath,
        long byteCount,
        string message)
    {
        using var expected = new FileStream(expectedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var actual = new FileStream(actualPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        var expectedBuffer = new byte[1024 * 1024];
        var actualBuffer = new byte[1024 * 1024];
        var remaining = byteCount;

        while (remaining > 0)
        {
            var requested = (int)Math.Min(expectedBuffer.Length, remaining);
            var expectedRead = expected.Read(expectedBuffer, 0, requested);
            var actualRead = actual.Read(actualBuffer, 0, requested);

            Assert.True(expectedRead == actualRead && expectedRead > 0, message);
            Assert.True(
                expectedBuffer.AsSpan(0, expectedRead).SequenceEqual(actualBuffer.AsSpan(0, actualRead)),
                message);

            remaining -= expectedRead;
        }
    }

    private static void AssertBytesEqual(byte[]? expected, byte[]? actual, string message)
    {
        if (expected is null || actual is null)
        {
            Assert.True(expected is null && actual is null, message);
            return;
        }

        Assert.True(expected.AsSpan().SequenceEqual(actual), message);
    }

    private static byte[] ReadExactly(Stream stream, int length)
    {
        var result = new byte[length];
        ReadExactly(stream, result);
        return result;
    }

    private static void ReadExactly(Stream stream, Span<byte> destination)
    {
        var total = 0;

        while (total < destination.Length)
        {
            var read = stream.Read(destination[total..]);
            if (read <= 0)
                throw new EndOfStreamException("Die Datei endete unerwartet.");

            total += read;
        }
    }

    private static string CalculateSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool FilesAreByteEqual(string firstPath, string secondPath)
    {
        var firstInfo = new FileInfo(firstPath);
        var secondInfo = new FileInfo(secondPath);

        if (firstInfo.Length != secondInfo.Length)
            return false;

        using var first = File.OpenRead(firstPath);
        using var second = File.OpenRead(secondPath);
        var firstBuffer = new byte[1024 * 1024];
        var secondBuffer = new byte[1024 * 1024];

        while (true)
        {
            var firstRead = first.Read(firstBuffer, 0, firstBuffer.Length);
            var secondRead = second.Read(secondBuffer, 0, secondBuffer.Length);

            if (firstRead != secondRead)
                return false;

            if (firstRead == 0)
                return true;

            if (!firstBuffer.AsSpan(0, firstRead).SequenceEqual(secondBuffer.AsSpan(0, secondRead)))
                return false;
        }
    }

    private sealed record WavPackSnapshot(
        long WavPackDataLength,
        ParsedApeTag? Tag,
        byte[]? TrailingId3v1);

    private sealed record ParsedApeTag(
        bool HasHeader,
        byte[]? HeaderRaw,
        byte[] FooterRaw,
        IReadOnlyList<ApeItem> Items);

    private sealed record ApeItem(
        string Key,
        uint Flags,
        byte[] RawBytes,
        byte[] ValueBytes);

    private readonly record struct ApeDescriptor(
        uint Version,
        uint Size,
        uint ItemCount,
        uint Flags);
}
