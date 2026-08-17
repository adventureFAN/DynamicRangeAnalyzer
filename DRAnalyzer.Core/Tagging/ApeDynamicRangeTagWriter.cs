using System.Buffers.Binary;
using System.Text;

namespace DRAnalyzer.Core.Tagging;

public static class ApeDynamicRangeTagWriter
{
    private const string TrackDynamicRangeField = "DYNAMIC RANGE";
    private const string AlbumDynamicRangeField = "ALBUM DYNAMIC RANGE";

    private const uint ApeVersion = 2000;
    private const int DescriptorLength = 32;
    private const int MaximumTagBytes = 64 * 1024 * 1024;
    private const int MaximumItemCount = 65536;

    private const uint TagFlagContainsHeader = 1u << 31;
    private const uint TagFlagLacksFooter = 1u << 30;
    private const uint TagFlagIsHeader = 1u << 29;
    private const uint AllowedTagFlags =
        TagFlagContainsHeader |
        TagFlagLacksFooter |
        TagFlagIsHeader;

    private static readonly byte[] MonkeyAudioMarker = Encoding.ASCII.GetBytes("MAC ");
    private static readonly byte[] ApeTagMarker = Encoding.ASCII.GetBytes("APETAGEX");
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static void Write(string filePath, int trackDynamicRange, int? albumDynamicRange)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
            throw new FileNotFoundException("Die APE-Datei wurde nicht gefunden.", filePath);

        if (trackDynamicRange < 0)
            throw new ArgumentOutOfRangeException(nameof(trackDynamicRange));

        if (albumDynamicRange is < 0)
            throw new ArgumentOutOfRangeException(nameof(albumDynamicRange));

        var fullPath = Path.GetFullPath(filePath);
        var source = ReadFile(fullPath);
        var editedItems = BuildWrittenItems(source.Tag, trackDynamicRange, albumDynamicRange);
        var tagBytes = BuildTag(source.Tag, editedItems);

        RewriteSafely(
            fullPath,
            source,
            tagBytes,
            tempPath => ValidateWrittenCopy(
                fullPath,
                tempPath,
                source,
                trackDynamicRange,
                albumDynamicRange));
    }

    public static void Remove(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
            throw new FileNotFoundException("Die APE-Datei wurde nicht gefunden.", filePath);

        var fullPath = Path.GetFullPath(filePath);
        var source = ReadFile(fullPath);

        if (source.Tag is null ||
            !source.Tag.Items.Any(item => item.OwnedField != OwnedField.None))
        {
            return;
        }

        var foreignItems = source.Tag.Items
            .Where(item => item.OwnedField == OwnedField.None)
            .ToArray();

        if (foreignItems.Length == 0)
        {
            RewriteSafely(
                fullPath,
                source,
                replacementTag: null,
                tempPath => ValidateRemovedEntireTagCopy(fullPath, tempPath, source));
            return;
        }

        var replacementTag = BuildTag(source.Tag, foreignItems);

        RewriteSafely(
            fullPath,
            source,
            replacementTag,
            tempPath => ValidateRemovedFieldsCopy(fullPath, tempPath, source));
    }

    private static IReadOnlyList<ApeItem> BuildWrittenItems(
        ParsedApeTag? sourceTag,
        int trackDynamicRange,
        int? albumDynamicRange)
    {
        var output = new List<ApeItem>();
        var trackWritten = false;
        var albumWritten = false;

        if (sourceTag is not null)
        {
            foreach (var item in sourceTag.Items)
            {
                switch (item.OwnedField)
                {
                    case OwnedField.Track:
                        if (!trackWritten)
                        {
                            output.Add(BuildTextItem(
                                TrackDynamicRangeField,
                                trackDynamicRange.ToString()));
                            trackWritten = true;
                        }
                        break;

                    case OwnedField.Album:
                        if (albumDynamicRange is null)
                        {
                            output.Add(item);
                        }
                        else if (!albumWritten)
                        {
                            output.Add(BuildTextItem(
                                AlbumDynamicRangeField,
                                albumDynamicRange.Value.ToString()));
                            albumWritten = true;
                        }
                        break;

                    default:
                        output.Add(item);
                        break;
                }
            }
        }

        if (!trackWritten)
        {
            output.Add(BuildTextItem(
                TrackDynamicRangeField,
                trackDynamicRange.ToString()));
        }

        if (albumDynamicRange is not null && !albumWritten)
        {
            output.Add(BuildTextItem(
                AlbumDynamicRangeField,
                albumDynamicRange.Value.ToString()));
        }

        return output;
    }

    private static ApeItem BuildTextItem(string key, string value)
    {
        var keyBytes = Encoding.ASCII.GetBytes(key);
        var valueBytes = StrictUtf8.GetBytes(value);
        var raw = new byte[8 + keyBytes.Length + 1 + valueBytes.Length];

        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0, 4), checked((uint)valueBytes.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(4, 4), 0);
        keyBytes.CopyTo(raw, 8);
        valueBytes.CopyTo(raw, 8 + keyBytes.Length + 1);

        return new ApeItem(
            key,
            Flags: 0,
            raw,
            valueBytes,
            GetOwnedField(key));
    }

    private static byte[] BuildTag(ParsedApeTag? sourceTag, IReadOnlyList<ApeItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var itemBytesLength = items.Sum(item => checked(item.RawBytes.Length));
        var footerSize = checked(itemBytesLength + DescriptorLength);
        var hasHeader = sourceTag?.HasHeader ?? true;
        var totalLength = checked(footerSize + (hasHeader ? DescriptorLength : 0));

        if (totalLength > MaximumTagBytes)
            throw new InvalidDataException("Der resultierende APEv2-Tag ist zu groß.");

        var result = new byte[totalLength];
        var offset = 0;

        var preserveLegacyLacksFooterFlag =
            sourceTag is not null &&
            (BinaryPrimitives.ReadUInt32LittleEndian(sourceTag.FooterRaw.AsSpan(20, 4)) & TagFlagLacksFooter) != 0;

        if (hasHeader)
        {
            var headerFlags =
                TagFlagContainsHeader |
                TagFlagIsHeader |
                (preserveLegacyLacksFooterFlag ? TagFlagLacksFooter : 0u);

            WriteDescriptor(
                result.AsSpan(offset, DescriptorLength),
                sourceTag?.HeaderRaw,
                footerSize,
                items.Count,
                headerFlags);
            offset += DescriptorLength;
        }

        foreach (var item in items)
        {
            item.RawBytes.CopyTo(result, offset);
            offset += item.RawBytes.Length;
        }

        var footerFlags =
            (hasHeader ? TagFlagContainsHeader : 0u) |
            (preserveLegacyLacksFooterFlag ? TagFlagLacksFooter : 0u);
        WriteDescriptor(
            result.AsSpan(offset, DescriptorLength),
            sourceTag?.FooterRaw,
            footerSize,
            items.Count,
            footerFlags);

        return result;
    }

    private static void WriteDescriptor(
        Span<byte> destination,
        byte[]? template,
        int footerSize,
        int itemCount,
        uint requiredFlags)
    {
        if (destination.Length != DescriptorLength)
            throw new ArgumentException("Ungültige APEv2-Descriptorgröße.", nameof(destination));

        destination.Clear();

        if (template is { Length: DescriptorLength })
            template.AsSpan().CopyTo(destination);

        ApeTagMarker.AsSpan().CopyTo(destination);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(8, 4), ApeVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(12, 4), checked((uint)footerSize));
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(16, 4), checked((uint)itemCount));
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(20, 4), requiredFlags);

        // Reserved bytes are required to stay zero. Existing non-zero values are rejected while parsing.
        destination.Slice(24, 8).Clear();
    }

    private static void RewriteSafely(
        string fullPath,
        ParsedApeFile source,
        byte[]? replacementTag,
        Action<string> validateTemp)
    {
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Das Dateiverzeichnis konnte nicht ermittelt werden.");
        var fileName = Path.GetFileName(fullPath);
        var uniqueId = Guid.NewGuid().ToString("N");
        var tempPath = Path.Combine(directory, $".{fileName}.{uniqueId}.dranalyzer.tmp");
        var backupPath = Path.Combine(directory, $".{fileName}.{uniqueId}.dranalyzer.backup");
        var replaceSucceeded = false;

        try
        {
            WriteModifiedCopy(fullPath, tempPath, source.AudioAndContainerLength, replacementTag);
            validateTemp(tempPath);

            File.Replace(
                tempPath,
                fullPath,
                backupPath,
                ignoreMetadataErrors: true);

            replaceSucceeded = true;
        }
        finally
        {
            WriterFileCleanup.TryDelete(
                tempPath);

            // If File.Replace itself failed, retain any backup it created.
            // After a successful replace, cleanup is best-effort only.
            if (replaceSucceeded)
            {
                WriterFileCleanup.TryDelete(
                    backupPath);
            }
        }
    }

    private static void WriteModifiedCopy(
        string sourcePath,
        string destinationPath,
        long prefixLength,
        byte[]? replacementTag)
    {
        using var input = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        using var output = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);

        CopyExactly(input, output, prefixLength);

        if (replacementTag is not null)
            output.Write(replacementTag);

        output.Flush(flushToDisk: true);
    }

    private static void ValidateWrittenCopy(
        string sourcePath,
        string destinationPath,
        ParsedApeFile source,
        int trackDynamicRange,
        int? albumDynamicRange)
    {
        var destination = ReadFile(destinationPath);
        var destinationTag = destination.Tag
            ?? throw new InvalidDataException("Der geschriebene APEv2-Tag fehlt.");

        ValidatePrefixPreserved(
            sourcePath,
            source.AudioAndContainerLength,
            destinationPath,
            destination.AudioAndContainerLength);

        ValidateForeignItemsPreserved(source.Tag, destinationTag);

        var trackItems = destinationTag.Items
            .Where(item => item.OwnedField == OwnedField.Track)
            .ToArray();

        if (trackItems.Length != 1 ||
            !string.Equals(ReadTextValue(trackItems[0]), trackDynamicRange.ToString(), StringComparison.Ordinal))
        {
            throw new InvalidDataException("Der geschriebene DYNAMIC RANGE-APEv2-Eintrag ist ungültig.");
        }

        var albumItems = destinationTag.Items
            .Where(item => item.OwnedField == OwnedField.Album)
            .ToArray();

        if (albumDynamicRange is not null)
        {
            if (albumItems.Length != 1 ||
                !string.Equals(ReadTextValue(albumItems[0]), albumDynamicRange.Value.ToString(), StringComparison.Ordinal))
            {
                throw new InvalidDataException("Der geschriebene ALBUM DYNAMIC RANGE-APEv2-Eintrag ist ungültig.");
            }
        }
        else
        {
            var sourceAlbumItems = source.Tag?.Items
                .Where(item => item.OwnedField == OwnedField.Album)
                .Select(item => item.RawBytes)
                .ToArray() ?? Array.Empty<byte[]>();

            var destinationAlbumItems = albumItems
                .Select(item => item.RawBytes)
                .ToArray();

            AssertRawItemSequenceEqual(sourceAlbumItems, destinationAlbumItems);
        }
    }

    private static void ValidateRemovedFieldsCopy(
        string sourcePath,
        string destinationPath,
        ParsedApeFile source)
    {
        var destination = ReadFile(destinationPath);
        var destinationTag = destination.Tag
            ?? throw new InvalidDataException("Der APEv2-Tag wurde unerwartet vollständig entfernt.");

        ValidatePrefixPreserved(
            sourcePath,
            source.AudioAndContainerLength,
            destinationPath,
            destination.AudioAndContainerLength);

        if (destinationTag.Items.Any(item => item.OwnedField != OwnedField.None))
            throw new InvalidDataException("Die DRAnalyzer-APEv2-Einträge wurden nicht vollständig entfernt.");

        ValidateForeignItemsPreserved(source.Tag, destinationTag);
    }

    private static void ValidateRemovedEntireTagCopy(
        string sourcePath,
        string destinationPath,
        ParsedApeFile source)
    {
        var destination = ReadFile(destinationPath);

        if (destination.Tag is not null)
            throw new InvalidDataException("Der ausschließlich aus DRAnalyzer-Feldern bestehende APEv2-Tag wurde nicht vollständig entfernt.");

        ValidatePrefixPreserved(
            sourcePath,
            source.AudioAndContainerLength,
            destinationPath,
            destination.AudioAndContainerLength);
    }

    private static void ValidateForeignItemsPreserved(ParsedApeTag? sourceTag, ParsedApeTag destinationTag)
    {
        var sourceForeign = sourceTag?.Items
            .Where(item => item.OwnedField == OwnedField.None)
            .Select(item => item.RawBytes)
            .ToArray() ?? Array.Empty<byte[]>();

        var destinationForeign = destinationTag.Items
            .Where(item => item.OwnedField == OwnedField.None)
            .Select(item => item.RawBytes)
            .ToArray();

        AssertRawItemSequenceEqual(sourceForeign, destinationForeign);
    }

    private static void AssertRawItemSequenceEqual(byte[][] expected, byte[][] actual)
    {
        if (expected.Length != actual.Length)
            throw new InvalidDataException("Die Anzahl fremder APEv2-Einträge wurde verändert.");

        for (var index = 0; index < expected.Length; index++)
        {
            if (!expected[index].AsSpan().SequenceEqual(actual[index]))
                throw new InvalidDataException("Ein fremder APEv2-Eintrag wurde verändert oder umsortiert.");
        }
    }

    private static string ReadTextValue(ApeItem item)
    {
        try
        {
            return StrictUtf8.GetString(item.ValueBytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("Ein DRAnalyzer-APEv2-Textwert ist nicht gültiges UTF-8.", exception);
        }
    }

    private static void ValidatePrefixPreserved(
        string sourcePath,
        long sourceLength,
        string destinationPath,
        long destinationLength)
    {
        if (sourceLength != destinationLength)
            throw new InvalidDataException("Der Monkey's-Audio-Nutzdatenbereich wurde verschoben oder in der Länge verändert.");

        using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var destination = new FileStream(destinationPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        var sourceBuffer = new byte[1024 * 1024];
        var destinationBuffer = new byte[sourceBuffer.Length];
        var remaining = sourceLength;

        while (remaining > 0)
        {
            var requested = (int)Math.Min(sourceBuffer.Length, remaining);
            ReadExactly(source, sourceBuffer.AsSpan(0, requested));
            ReadExactly(destination, destinationBuffer.AsSpan(0, requested));

            if (!sourceBuffer.AsSpan(0, requested).SequenceEqual(destinationBuffer.AsSpan(0, requested)))
                throw new InvalidDataException("Der Monkey's-Audio-Nutzdatenbereich wurde verändert.");

            remaining -= requested;
        }
    }

    private static ParsedApeFile ReadFile(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        if (stream.Length < 4)
            throw new InvalidDataException("Die Datei ist zu kurz für Monkey's Audio.");

        Span<byte> marker = stackalloc byte[4];
        ReadExactly(stream, marker);

        if (!marker.SequenceEqual(MonkeyAudioMarker))
            throw new InvalidDataException("Die Datei besitzt keinen gültigen Monkey's-Audio-Marker.");

        if (stream.Length < DescriptorLength)
            return new ParsedApeFile(stream.Length, null);

        stream.Position = stream.Length - DescriptorLength;
        var footerRaw = new byte[DescriptorLength];
        ReadExactly(stream, footerRaw);

        if (!footerRaw.AsSpan(0, 8).SequenceEqual(ApeTagMarker))
            return new ParsedApeFile(stream.Length, null);

        var footer = ParseDescriptor(footerRaw, isHeader: false);

        if (footer.Version != ApeVersion)
            throw new NotSupportedException($"APEv{footer.Version / 1000.0:0.0} wird nicht unterstützt; erwartet wird APEv2.");

        if ((footer.Flags & TagFlagIsHeader) != 0)
            throw new InvalidDataException("Der APEv2-Footer ist fälschlich als Header markiert.");

        // Some real-world APEv2 writers historically set the bit-30
        // "lacks footer" flag even though a valid footer is physically
        // present at the end of the file. Because we reached this code by
        // reading an actual APETAGEX footer at EOF, the physical structure
        // is authoritative. The legacy flag is preserved when rewriting.

        if ((footer.Flags & ~AllowedTagFlags) != 0)
            throw new InvalidDataException("Der APEv2-Footer enthält unbekannte Flags.");

        if (footer.Size < DescriptorLength)
            throw new InvalidDataException("Der APEv2-Tag meldet eine ungültige Größe.");

        var hasHeader = (footer.Flags & TagFlagContainsHeader) != 0;
        var totalTagLength = checked((long)footer.Size + (hasHeader ? DescriptorLength : 0));

        if (totalTagLength > MaximumTagBytes)
            throw new InvalidDataException("Der APEv2-Tag ist ungewöhnlich groß und wird aus Sicherheitsgründen abgelehnt.");

        if (totalTagLength > stream.Length - 4)
            throw new InvalidDataException("Der APEv2-Tag ragt über den Dateianfang hinaus.");

        if (footer.ItemCount > MaximumItemCount)
            throw new InvalidDataException("Der APEv2-Tag enthält ungewöhnlich viele Einträge.");

        var tagStart = stream.Length - totalTagLength;
        var itemBytesLength = checked((int)footer.Size - DescriptorLength);
        byte[]? headerRaw = null;
        long itemsStart = tagStart;

        if (hasHeader)
        {
            stream.Position = tagStart;
            headerRaw = new byte[DescriptorLength];
            ReadExactly(stream, headerRaw);
            var header = ParseDescriptor(headerRaw, isHeader: true);

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
                throw new InvalidDataException("Der APEv2-Header enthält unerwartete Flags.");

            itemsStart += DescriptorLength;
        }

        stream.Position = itemsStart;
        var itemBytes = new byte[itemBytesLength];
        ReadExactly(stream, itemBytes);
        var items = ParseItems(itemBytes, checked((int)footer.ItemCount));

        return new ParsedApeFile(
            tagStart,
            new ParsedApeTag(
                hasHeader,
                headerRaw,
                footerRaw,
                items));
    }

    private static ApeDescriptor ParseDescriptor(byte[] raw, bool isHeader)
    {
        if (raw.Length != DescriptorLength || !raw.AsSpan(0, 8).SequenceEqual(ApeTagMarker))
            throw new InvalidDataException(isHeader ? "Ungültiger APEv2-Header." : "Ungültiger APEv2-Footer.");

        if (!raw.AsSpan(24, 8).SequenceEqual(new byte[8]))
            throw new InvalidDataException("Die reservierten APEv2-Descriptorbytes sind nicht 0.");

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

            if (offset >= bytes.Length)
                throw new InvalidDataException("Ein APEv2-Key ist nicht nullterminiert.");

            if (offset == keyStart)
                throw new InvalidDataException("Ein APEv2-Key ist leer.");

            var key = Encoding.ASCII.GetString(bytes, keyStart, offset - keyStart);
            offset++;

            if (valueLength > int.MaxValue || valueLength > bytes.Length - offset)
                throw new InvalidDataException("Ein APEv2-Wert ragt über das Tag-Ende hinaus.");

            var valueLengthInt = checked((int)valueLength);
            var valueBytes = bytes.AsSpan(offset, valueLengthInt).ToArray();
            offset += valueLengthInt;

            var raw = bytes.AsSpan(itemStart, offset - itemStart).ToArray();
            items.Add(new ApeItem(key, flags, raw, valueBytes, GetOwnedField(key)));
        }

        if (offset != bytes.Length)
            throw new InvalidDataException("Der APEv2-Tag enthält nicht zugeordnete Bytes nach dem letzten Eintrag.");

        return items;
    }

    private static OwnedField GetOwnedField(string key)
    {
        if (string.Equals(key, TrackDynamicRangeField, StringComparison.OrdinalIgnoreCase))
            return OwnedField.Track;

        if (string.Equals(key, AlbumDynamicRangeField, StringComparison.OrdinalIgnoreCase))
            return OwnedField.Album;

        return OwnedField.None;
    }

    private static void CopyExactly(Stream input, Stream output, long byteCount)
    {
        var buffer = new byte[1024 * 1024];
        var remaining = byteCount;

        while (remaining > 0)
        {
            var requested = (int)Math.Min(buffer.Length, remaining);
            var read = input.Read(buffer, 0, requested);
            if (read <= 0)
                throw new EndOfStreamException("Die Quelldatei endete unerwartet.");

            output.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    private static void ReadExactly(Stream stream, Span<byte> destination)
    {
        var totalRead = 0;

        while (totalRead < destination.Length)
        {
            var read = stream.Read(destination[totalRead..]);
            if (read <= 0)
                throw new EndOfStreamException("Die Datei endete unerwartet.");

            totalRead += read;
        }
    }

    private sealed record ParsedApeFile(long AudioAndContainerLength, ParsedApeTag? Tag);

    private sealed record ParsedApeTag(
        bool HasHeader,
        byte[]? HeaderRaw,
        byte[] FooterRaw,
        IReadOnlyList<ApeItem> Items);

    private sealed record ApeItem(
        string Key,
        uint Flags,
        byte[] RawBytes,
        byte[] ValueBytes,
        OwnedField OwnedField);

    private readonly record struct ApeDescriptor(
        uint Version,
        uint Size,
        uint ItemCount,
        uint Flags);

    private enum OwnedField
    {
        None,
        Track,
        Album
    }
}
