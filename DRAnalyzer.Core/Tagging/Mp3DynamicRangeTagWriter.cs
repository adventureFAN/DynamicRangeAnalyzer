using System.Buffers.Binary;
using System.Text;

namespace DRAnalyzer.Core.Tagging;

public static class Mp3DynamicRangeTagWriter
{
    private const string TrackDynamicRangeField =
        "DYNAMIC RANGE";

    private const string AlbumDynamicRangeField =
        "ALBUM DYNAMIC RANGE";

    private const int TagHeaderLength = 10;
    private const int FrameHeaderLength = 10;
    private const int MaximumSynchsafeValue = 0x0FFFFFFF;

    private static readonly byte[] Id3Marker =
        Encoding.ASCII.GetBytes("ID3");

    public static void Write(
        string filePath,
        int trackDynamicRange,
        int? albumDynamicRange)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            filePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException(
                "Die MP3-Datei wurde nicht gefunden.",
                filePath);
        }

        if (trackDynamicRange < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(trackDynamicRange));
        }

        if (albumDynamicRange is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(albumDynamicRange));
        }

        var fullPath = Path.GetFullPath(filePath);
        var sourceTag = ReadTag(fullPath);

        var targetVersion =
            sourceTag.Version ?? 4;

        var editedBody = BuildWrittenBody(
            sourceTag,
            targetVersion,
            trackDynamicRange,
            albumDynamicRange);

        RewriteSafely(
            fullPath,
            sourceTag,
            targetVersion,
            editedBody,
            tempPath =>
                ValidateWrittenCopy(
                    fullPath,
                    tempPath,
                    sourceTag,
                    targetVersion,
                    trackDynamicRange,
                    albumDynamicRange));
    }

    public static void Remove(
        string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            filePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException(
                "Die MP3-Datei wurde nicht gefunden.",
                filePath);
        }

        var fullPath = Path.GetFullPath(filePath);
        var sourceTag = ReadTag(fullPath);

        if (sourceTag.Version is null)
            return;

        if (!sourceTag.Frames.Any(
                frame => frame.OwnedField != OwnedField.None))
        {
            return;
        }

        var hasForeignFrames =
            sourceTag.Frames.Any(
                frame =>
                    frame.OwnedField ==
                    OwnedField.None);

        if (!hasForeignFrames)
        {
            RewriteWithoutId3Safely(
                fullPath,
                sourceTag);

            return;
        }

        var editedBody = BuildRemovedBody(
            sourceTag);

        RewriteSafely(
            fullPath,
            sourceTag,
            sourceTag.Version.Value,
            editedBody,
            tempPath =>
                ValidateRemovedCopy(
                    fullPath,
                    tempPath,
                    sourceTag));
    }

    private static byte[] BuildWrittenBody(
        ParsedTag sourceTag,
        int targetVersion,
        int trackDynamicRange,
        int? albumDynamicRange)
    {
        var output = new List<byte>();

        var trackWritten = false;
        var albumWritten = false;

        foreach (var frame in sourceTag.Frames)
        {
            switch (frame.OwnedField)
            {
                case OwnedField.Track:
                    if (!trackWritten)
                    {
                        output.AddRange(
                            BuildTxxxFrame(
                                targetVersion,
                                TrackDynamicRangeField,
                                trackDynamicRange.ToString()));

                        trackWritten = true;
                    }

                    break;

                case OwnedField.Album:
                    if (albumDynamicRange is null)
                    {
                        // Track-only write: vorhandenen Album-DR
                        // vollständig und bytegenau erhalten.
                        output.AddRange(frame.RawBytes);
                    }
                    else if (!albumWritten)
                    {
                        output.AddRange(
                            BuildTxxxFrame(
                                targetVersion,
                                AlbumDynamicRangeField,
                                albumDynamicRange.Value.ToString()));

                        albumWritten = true;
                    }

                    break;

                default:
                    output.AddRange(frame.RawBytes);
                    break;
            }
        }

        if (!trackWritten)
        {
            output.AddRange(
                BuildTxxxFrame(
                    targetVersion,
                    TrackDynamicRangeField,
                    trackDynamicRange.ToString()));
        }

        if (albumDynamicRange is not null &&
            !albumWritten)
        {
            output.AddRange(
                BuildTxxxFrame(
                    targetVersion,
                    AlbumDynamicRangeField,
                    albumDynamicRange.Value.ToString()));
        }

        return AddPadding(
            output,
            sourceTag.BodyLength);
    }

    private static byte[] BuildRemovedBody(
        ParsedTag sourceTag)
    {
        var output = new List<byte>();

        foreach (var frame in sourceTag.Frames)
        {
            if (frame.OwnedField == OwnedField.None)
            {
                output.AddRange(frame.RawBytes);
            }
        }

        return AddPadding(
            output,
            sourceTag.BodyLength);
    }

    private static byte[] AddPadding(
        List<byte> content,
        int originalBodyLength)
    {
        if (content.Count > MaximumSynchsafeValue)
        {
            throw new InvalidDataException(
                "Der resultierende ID3v2-Tag ist zu groß.");
        }

        var targetLength =
            Math.Max(
                content.Count,
                originalBodyLength);

        if (targetLength > MaximumSynchsafeValue)
        {
            throw new InvalidDataException(
                "Der resultierende ID3v2-Tag ist zu groß.");
        }

        if (targetLength == content.Count)
            return content.ToArray();

        var result = new byte[targetLength];
        content.CopyTo(result, 0);
        return result;
    }

    private static byte[] BuildTxxxFrame(
        int version,
        string description,
        string value)
    {
        byte encodingByte;
        Encoding encoding;

        switch (version)
        {
            case 3:
                encodingByte = 0;
                encoding = Encoding.Latin1;
                break;

            case 4:
                encodingByte = 3;
                encoding = new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: true);
                break;

            default:
                throw new NotSupportedException(
                    $"ID3v2.{version} wird nicht unterstützt.");
        }

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

        Buffer.BlockCopy(
            descriptionBytes,
            0,
            payload,
            1,
            descriptionBytes.Length);

        var valueOffset =
            1 + descriptionBytes.Length + 1;

        Buffer.BlockCopy(
            valueBytes,
            0,
            payload,
            valueOffset,
            valueBytes.Length);

        var frame = new byte[
            FrameHeaderLength +
            payload.Length];

        Encoding.ASCII
            .GetBytes("TXXX")
            .CopyTo(frame, 0);

        WriteFrameSize(
            frame.AsSpan(4, 4),
            version,
            payload.Length);

        // Beide Frame-Flagbytes bleiben bewusst 0.
        Buffer.BlockCopy(
            payload,
            0,
            frame,
            FrameHeaderLength,
            payload.Length);

        return frame;
    }

    private static void RewriteWithoutId3Safely(
        string fullPath,
        ParsedTag sourceTag)
    {
        var directory =
            Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException(
                "Das Dateiverzeichnis konnte nicht ermittelt werden.");

        var fileName =
            Path.GetFileName(fullPath);

        var uniqueId =
            Guid.NewGuid().ToString("N");

        var tempPath = Path.Combine(
            directory,
            $".{fileName}.{uniqueId}.dranalyzer.tmp");

        var backupPath = Path.Combine(
            directory,
            $".{fileName}.{uniqueId}.dranalyzer.backup");

        var replaceSucceeded = false;

        try
        {
            WritePayloadOnlyCopy(
                fullPath,
                tempPath,
                sourceTag.AudioOffset);

            ValidateId3RemovedCompletely(
                fullPath,
                tempPath,
                sourceTag);

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

    private static void WritePayloadOnlyCopy(
        string sourcePath,
        string destinationPath,
        long sourceAudioOffset)
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

        input.Position = sourceAudioOffset;
        input.CopyTo(output);

        output.Flush(
            flushToDisk: true);
    }

    private static void ValidateId3RemovedCompletely(
        string sourcePath,
        string destinationPath,
        ParsedTag sourceTag)
    {
        var destinationTag =
            ReadTag(destinationPath);

        if (destinationTag.Version is not null)
        {
            throw new InvalidDataException(
                "Der ausschließlich aus DRAnalyzer-Feldern bestehende ID3v2-Tag wurde nicht vollständig entfernt.");
        }

        ValidatePayloadAfterId3Preserved(
            sourcePath,
            sourceTag.AudioOffset,
            destinationPath,
            destinationTag.AudioOffset);
    }

    private static void RewriteSafely(
        string fullPath,
        ParsedTag sourceTag,
        int targetVersion,
        byte[] editedBody,
        Action<string> validateTemp)
    {
        var directory =
            Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException(
                "Das Dateiverzeichnis konnte nicht ermittelt werden.");

        var fileName =
            Path.GetFileName(fullPath);

        var uniqueId =
            Guid.NewGuid().ToString("N");

        var tempPath = Path.Combine(
            directory,
            $".{fileName}.{uniqueId}.dranalyzer.tmp");

        var backupPath = Path.Combine(
            directory,
            $".{fileName}.{uniqueId}.dranalyzer.backup");

        var replaceSucceeded = false;

        try
        {
            WriteModifiedCopy(
                fullPath,
                tempPath,
                sourceTag,
                targetVersion,
                editedBody);

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
        ParsedTag sourceTag,
        int targetVersion,
        byte[] editedBody)
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

        var header = new byte[TagHeaderLength];

        Id3Marker.CopyTo(
            header,
            0);

        header[3] = checked((byte)targetVersion);
        header[4] = 0;
        header[5] = 0;

        WriteSynchsafeInteger(
            header.AsSpan(6, 4),
            editedBody.Length);

        output.Write(header);
        output.Write(editedBody);

        input.Position =
            sourceTag.AudioOffset;

        input.CopyTo(output);

        output.Flush(
            flushToDisk: true);
    }

    private static void ValidateWrittenCopy(
        string sourcePath,
        string destinationPath,
        ParsedTag sourceTag,
        int targetVersion,
        int trackDynamicRange,
        int? albumDynamicRange)
    {
        var destinationTag =
            ReadTag(destinationPath);

        if (destinationTag.Version != targetVersion)
        {
            throw new InvalidDataException(
                "Die geschriebene MP3-Datei enthält nicht die erwartete ID3v2-Version.");
        }

        var trackFrames =
            destinationTag.Frames
                .Where(
                    frame =>
                        frame.OwnedField ==
                        OwnedField.Track)
                .ToArray();

        if (trackFrames.Length != 1 ||
            !string.Equals(
                trackFrames[0].TextValue,
                trackDynamicRange.ToString(),
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Der geschriebene DYNAMIC RANGE-TXXX-Frame ist ungültig.");
        }

        var destinationAlbumFrames =
            destinationTag.Frames
                .Where(
                    frame =>
                        frame.OwnedField ==
                        OwnedField.Album)
                .ToArray();

        if (albumDynamicRange is not null)
        {
            if (destinationAlbumFrames.Length != 1 ||
                !string.Equals(
                    destinationAlbumFrames[0].TextValue,
                    albumDynamicRange.Value.ToString(),
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Der geschriebene ALBUM DYNAMIC RANGE-TXXX-Frame ist ungültig.");
            }
        }
        else
        {
            var sourceAlbumFrames =
                sourceTag.Frames
                    .Where(
                        frame =>
                            frame.OwnedField ==
                            OwnedField.Album)
                    .Select(
                        frame =>
                            frame.RawBytes)
                    .ToArray();

            AssertRawFrameSequenceEqual(
                sourceAlbumFrames,
                destinationAlbumFrames
                    .Select(
                        frame =>
                            frame.RawBytes)
                    .ToArray(),
                "Vorhandene ALBUM DYNAMIC RANGE-Frames wurden bei Track-only-Write verändert.");
        }

        ValidateForeignFramesPreserved(
            sourceTag,
            destinationTag);

        ValidatePayloadAfterId3Preserved(
            sourcePath,
            sourceTag.AudioOffset,
            destinationPath,
            destinationTag.AudioOffset);
    }

    private static void ValidateRemovedCopy(
        string sourcePath,
        string destinationPath,
        ParsedTag sourceTag)
    {
        var destinationTag =
            ReadTag(destinationPath);

        if (destinationTag.Version != sourceTag.Version)
        {
            throw new InvalidDataException(
                "Die ID3v2-Version wurde beim Entfernen verändert.");
        }

        if (destinationTag.Frames.Any(
                frame =>
                    frame.OwnedField !=
                    OwnedField.None))
        {
            throw new InvalidDataException(
                "Eigene DR-TXXX-Frames sind nach Remove weiterhin vorhanden.");
        }

        ValidateForeignFramesPreserved(
            sourceTag,
            destinationTag);

        ValidatePayloadAfterId3Preserved(
            sourcePath,
            sourceTag.AudioOffset,
            destinationPath,
            destinationTag.AudioOffset);
    }

    private static void ValidateForeignFramesPreserved(
        ParsedTag sourceTag,
        ParsedTag destinationTag)
    {
        var sourceForeignFrames =
            sourceTag.Frames
                .Where(
                    frame =>
                        frame.OwnedField ==
                        OwnedField.None)
                .Select(
                    frame =>
                        frame.RawBytes)
                .ToArray();

        var destinationForeignFrames =
            destinationTag.Frames
                .Where(
                    frame =>
                        frame.OwnedField ==
                        OwnedField.None)
                .Select(
                    frame =>
                        frame.RawBytes)
                .ToArray();

        AssertRawFrameSequenceEqual(
            sourceForeignFrames,
            destinationForeignFrames,
            "Fremde ID3v2-Frames wurden verändert oder umsortiert.");
    }

    private static void AssertRawFrameSequenceEqual(
        byte[][] expected,
        byte[][] actual,
        string message)
    {
        if (expected.Length != actual.Length)
        {
            throw new InvalidDataException(message);
        }

        for (var index = 0;
             index < expected.Length;
             index++)
        {
            if (!expected[index]
                    .AsSpan()
                    .SequenceEqual(actual[index]))
            {
                throw new InvalidDataException(message);
            }
        }
    }

    private static void ValidatePayloadAfterId3Preserved(
        string sourcePath,
        long sourceOffset,
        string destinationPath,
        long destinationOffset)
    {
        using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        using var destination = new FileStream(
            destinationPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        var sourceLength =
            source.Length - sourceOffset;

        var destinationLength =
            destination.Length - destinationOffset;

        if (sourceLength != destinationLength)
        {
            throw new InvalidDataException(
                "Die Daten hinter dem ID3v2-Tag wurden in der Länge verändert.");
        }

        source.Position = sourceOffset;
        destination.Position = destinationOffset;

        var sourceBuffer = new byte[64 * 1024];
        var destinationBuffer = new byte[64 * 1024];

        while (true)
        {
            var sourceRead =
                source.Read(sourceBuffer);

            var destinationRead =
                destination.Read(destinationBuffer);

            if (sourceRead != destinationRead)
            {
                throw new InvalidDataException(
                    "Die Daten hinter dem ID3v2-Tag wurden verändert.");
            }

            if (sourceRead == 0)
                break;

            if (!sourceBuffer
                    .AsSpan(0, sourceRead)
                    .SequenceEqual(
                        destinationBuffer
                            .AsSpan(0, destinationRead)))
            {
                throw new InvalidDataException(
                    "Die Daten hinter dem ID3v2-Tag wurden byteweise verändert.");
            }
        }
    }

    private static ParsedTag ReadTag(
        string filePath)
    {
        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        if (stream.Length < 3)
        {
            return ParsedTag.WithoutId3();
        }

        Span<byte> marker = stackalloc byte[3];
        ReadExactly(
            stream,
            marker);

        if (!marker.SequenceEqual(Id3Marker))
        {
            return ParsedTag.WithoutId3();
        }

        if (stream.Length < TagHeaderLength)
        {
            throw new InvalidDataException(
                "Die MP3-Datei enthält einen abgeschnittenen ID3v2-Header.");
        }

        var header = new byte[TagHeaderLength];
        marker.CopyTo(header);

        ReadExactly(
            stream,
            header.AsSpan(3));

        var version = header[3];
        var revision = header[4];
        var flags = header[5];

        if (version is not 3 and not 4)
        {
            throw new NotSupportedException(
                $"ID3v2.{version} wird für MP3-DR-Tags nicht unterstützt. Unterstützt werden nur ID3v2.3 und ID3v2.4.");
        }

        if (revision != 0)
        {
            throw new NotSupportedException(
                $"ID3v2.{version}.{revision} wird aus Sicherheitsgründen nicht verändert.");
        }

        if (flags != 0)
        {
            throw new NotSupportedException(
                $"ID3v2.{version}-Tags mit Unsynchronisation, Extended Header, Experimental Flag oder Footer werden derzeit aus Sicherheitsgründen nicht verändert.");
        }

        var bodyLength =
            ReadSynchsafeInteger(
                header.AsSpan(6, 4));

        var audioOffset =
            (long)TagHeaderLength +
            bodyLength;

        if (audioOffset > stream.Length)
        {
            throw new InvalidDataException(
                "Die ID3v2-Größenangabe reicht über das Dateiende hinaus.");
        }

        var body = new byte[bodyLength];
        ReadExactly(
            stream,
            body);

        var frames =
            ParseFrames(
                body,
                version);

        return new ParsedTag(
            version,
            bodyLength,
            audioOffset,
            frames);
    }

    private static IReadOnlyList<ParsedFrame> ParseFrames(
        byte[] body,
        int version)
    {
        var frames = new List<ParsedFrame>();
        var offset = 0;

        while (offset < body.Length)
        {
            var remaining =
                body.Length - offset;

            if (body[offset] == 0)
            {
                for (var paddingIndex = offset;
                     paddingIndex < body.Length;
                     paddingIndex++)
                {
                    if (body[paddingIndex] != 0)
                    {
                        throw new InvalidDataException(
                            "Der ID3v2-Tag enthält ungültige Daten nach dem Padding-Beginn.");
                    }
                }

                break;
            }

            if (remaining < FrameHeaderLength)
            {
                throw new InvalidDataException(
                    "Der ID3v2-Tag enthält einen abgeschnittenen Frame-Header.");
            }

            var frameIdBytes =
                body.AsSpan(
                    offset,
                    4);

            if (!IsValidFrameId(frameIdBytes))
            {
                throw new InvalidDataException(
                    "Der ID3v2-Tag enthält eine ungültige Frame-ID.");
            }

            var frameId =
                Encoding.ASCII.GetString(
                    frameIdBytes);

            var payloadLength =
                ReadFrameSize(
                    body.AsSpan(
                        offset + 4,
                        4),
                    version);

            if (payloadLength <= 0)
            {
                throw new InvalidDataException(
                    $"Der ID3v2-Frame '{frameId}' besitzt eine ungültige Größe.");
            }

            var totalLength = checked(
                FrameHeaderLength +
                payloadLength);

            if (totalLength > remaining)
            {
                throw new InvalidDataException(
                    $"Der ID3v2-Frame '{frameId}' reicht über den Tag hinaus.");
            }

            var rawBytes =
                body.AsSpan(
                    offset,
                    totalLength)
                .ToArray();

            var ownedField = OwnedField.None;
            string? textValue = null;

            if (string.Equals(
                    frameId,
                    "TXXX",
                    StringComparison.Ordinal))
            {
                var firstFlag =
                    body[offset + 8];

                var secondFlag =
                    body[offset + 9];

                if (firstFlag != 0 ||
                    secondFlag != 0)
                {
                    throw new NotSupportedException(
                        "TXXX-Frames mit ID3v2-Frame-Flags werden aus Sicherheitsgründen nicht verändert.");
                }

                var parsedText =
                    ParseTxxxPayload(
                        body.AsSpan(
                            offset + FrameHeaderLength,
                            payloadLength),
                        version);

                if (string.Equals(
                        parsedText.Description,
                        TrackDynamicRangeField,
                        StringComparison.OrdinalIgnoreCase))
                {
                    ownedField =
                        OwnedField.Track;

                    textValue =
                        parsedText.Value;
                }
                else if (string.Equals(
                             parsedText.Description,
                             AlbumDynamicRangeField,
                             StringComparison.OrdinalIgnoreCase))
                {
                    ownedField =
                        OwnedField.Album;

                    textValue =
                        parsedText.Value;
                }
            }

            frames.Add(
                new ParsedFrame(
                    frameId,
                    rawBytes,
                    ownedField,
                    textValue));

            offset += totalLength;
        }

        return frames;
    }

    private static ParsedTxxx ParseTxxxPayload(
        ReadOnlySpan<byte> payload,
        int version)
    {
        if (payload.Length < 2)
        {
            throw new InvalidDataException(
                "Ein TXXX-Frame ist zu kurz.");
        }

        var encodingByte =
            payload[0];

        if (version == 3 &&
            encodingByte is not 0 and not 1)
        {
            throw new NotSupportedException(
                "ID3v2.3-TXXX mit nicht unterstützter Textkodierung wird aus Sicherheitsgründen nicht verändert.");
        }

        if (version == 4 &&
            encodingByte is not 0 and not 1 and not 2 and not 3)
        {
            throw new NotSupportedException(
                "ID3v2.4-TXXX mit unbekannter Textkodierung wird aus Sicherheitsgründen nicht verändert.");
        }

        var textBytes =
            payload[1..];

        return encodingByte switch
        {
            0 => ParseSingleByteTxxx(
                textBytes,
                Encoding.Latin1),

            3 => ParseSingleByteTxxx(
                textBytes,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: true)),

            1 => ParseUtf16WithBomTxxx(
                textBytes),

            2 => ParseUtf16BigEndianTxxx(
                textBytes),

            _ => throw new InvalidDataException(
                "Unbekannte TXXX-Textkodierung.")
        };
    }

    private static ParsedTxxx ParseSingleByteTxxx(
        ReadOnlySpan<byte> textBytes,
        Encoding encoding)
    {
        var terminatorIndex =
            textBytes.IndexOf((byte)0);

        if (terminatorIndex < 0)
        {
            throw new InvalidDataException(
                "Ein TXXX-Frame besitzt keine terminierte Description.");
        }

        try
        {
            var description =
                encoding.GetString(
                    textBytes[..terminatorIndex]);

            var value =
                encoding.GetString(
                    textBytes[(terminatorIndex + 1)..]);

            return new ParsedTxxx(
                description,
                value);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException(
                "Ein TXXX-Frame enthält ungültigen Text.",
                exception);
        }
    }

    private static ParsedTxxx ParseUtf16WithBomTxxx(
        ReadOnlySpan<byte> textBytes)
    {
        if (textBytes.Length < 4)
        {
            throw new InvalidDataException(
                "Ein UTF-16-TXXX-Frame ist zu kurz.");
        }

        Encoding encoding;

        if (textBytes[0] == 0xFF &&
            textBytes[1] == 0xFE)
        {
            encoding = new UnicodeEncoding(
                bigEndian: false,
                byteOrderMark: false,
                throwOnInvalidBytes: true);
        }
        else if (textBytes[0] == 0xFE &&
                 textBytes[1] == 0xFF)
        {
            encoding = new UnicodeEncoding(
                bigEndian: true,
                byteOrderMark: false,
                throwOnInvalidBytes: true);
        }
        else
        {
            throw new InvalidDataException(
                "Ein ID3v2-TXXX mit Encoding 1 besitzt keine UTF-16-BOM.");
        }

        var content =
            textBytes[2..];

        var terminatorIndex =
            FindUtf16Terminator(
                content);

        if (terminatorIndex < 0)
        {
            throw new InvalidDataException(
                "Ein UTF-16-TXXX besitzt keine terminierte Description.");
        }

        try
        {
            var description =
                encoding.GetString(
                    content[..terminatorIndex]);

            var valueBytes =
                content[(terminatorIndex + 2)..];

            if (valueBytes.Length >= 2 &&
                ((valueBytes[0] == 0xFF && valueBytes[1] == 0xFE) ||
                 (valueBytes[0] == 0xFE && valueBytes[1] == 0xFF)))
            {
                // Manche Tagger schreiben vor dem Value erneut eine BOM.
                valueBytes = valueBytes[2..];
            }

            var value =
                encoding.GetString(
                    valueBytes);

            return new ParsedTxxx(
                description,
                value);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException(
                "Ein UTF-16-TXXX enthält ungültigen Text.",
                exception);
        }
    }

    private static ParsedTxxx ParseUtf16BigEndianTxxx(
        ReadOnlySpan<byte> textBytes)
    {
        var terminatorIndex =
            FindUtf16Terminator(
                textBytes);

        if (terminatorIndex < 0)
        {
            throw new InvalidDataException(
                "Ein UTF-16BE-TXXX besitzt keine terminierte Description.");
        }

        var encoding =
            new UnicodeEncoding(
                bigEndian: true,
                byteOrderMark: false,
                throwOnInvalidBytes: true);

        try
        {
            var description =
                encoding.GetString(
                    textBytes[..terminatorIndex]);

            var value =
                encoding.GetString(
                    textBytes[(terminatorIndex + 2)..]);

            return new ParsedTxxx(
                description,
                value);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException(
                "Ein UTF-16BE-TXXX enthält ungültigen Text.",
                exception);
        }
    }

    private static int FindUtf16Terminator(
        ReadOnlySpan<byte> bytes)
    {
        for (var index = 0;
             index + 1 < bytes.Length;
             index += 2)
        {
            if (bytes[index] == 0 &&
                bytes[index + 1] == 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsValidFrameId(
        ReadOnlySpan<byte> frameId)
    {
        if (frameId.Length != 4)
            return false;

        foreach (var value in frameId)
        {
            var isUppercaseLetter =
                value is >= (byte)'A' and <= (byte)'Z';

            var isDigit =
                value is >= (byte)'0' and <= (byte)'9';

            if (!isUppercaseLetter &&
                !isDigit)
            {
                return false;
            }
        }

        return true;
    }

    private static int ReadFrameSize(
        ReadOnlySpan<byte> bytes,
        int version)
    {
        return version switch
        {
            3 => ReadBigEndianInt32(bytes),
            4 => ReadSynchsafeInteger(bytes),
            _ => throw new NotSupportedException()
        };
    }

    private static void WriteFrameSize(
        Span<byte> destination,
        int version,
        int value)
    {
        switch (version)
        {
            case 3:
                BinaryPrimitives.WriteInt32BigEndian(
                    destination,
                    value);
                break;

            case 4:
                WriteSynchsafeInteger(
                    destination,
                    value);
                break;

            default:
                throw new NotSupportedException();
        }
    }

    private static int ReadBigEndianInt32(
        ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 4)
        {
            throw new ArgumentException(
                "Es werden genau vier Bytes erwartet.",
                nameof(bytes));
        }

        var value =
            BinaryPrimitives.ReadInt32BigEndian(
                bytes);

        if (value < 0)
        {
            throw new InvalidDataException(
                "Eine ID3v2-Frame-Größe ist negativ.");
        }

        return value;
    }

    private static int ReadSynchsafeInteger(
        ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 4)
        {
            throw new ArgumentException(
                "Es werden genau vier Bytes erwartet.",
                nameof(bytes));
        }

        var value = 0;

        foreach (var current in bytes)
        {
            if ((current & 0x80) != 0)
            {
                throw new InvalidDataException(
                    "Eine ID3v2-Synchsafe-Größe ist ungültig.");
            }

            value =
                (value << 7) |
                current;
        }

        return value;
    }

    private static void WriteSynchsafeInteger(
        Span<byte> destination,
        int value)
    {
        if (destination.Length != 4)
        {
            throw new ArgumentException(
                "Es werden genau vier Bytes erwartet.",
                nameof(destination));
        }

        if (value < 0 ||
            value > MaximumSynchsafeValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value));
        }

        destination[0] =
            (byte)((value >> 21) & 0x7F);

        destination[1] =
            (byte)((value >> 14) & 0x7F);

        destination[2] =
            (byte)((value >> 7) & 0x7F);

        destination[3] =
            (byte)(value & 0x7F);
    }

    private static void ReadExactly(
        Stream stream,
        Span<byte> destination)
    {
        var totalRead = 0;

        while (totalRead < destination.Length)
        {
            var read = stream.Read(
                destination[totalRead..]);

            if (read == 0)
            {
                throw new EndOfStreamException(
                    "Unerwartetes Dateiende beim Lesen eines ID3v2-Tags.");
            }

            totalRead += read;
        }
    }

    private sealed record ParsedTag(
        int? Version,
        int BodyLength,
        long AudioOffset,
        IReadOnlyList<ParsedFrame> Frames)
    {
        public static ParsedTag WithoutId3()
        {
            return new ParsedTag(
                null,
                0,
                0,
                Array.Empty<ParsedFrame>());
        }
    }

    private sealed record ParsedFrame(
        string FrameId,
        byte[] RawBytes,
        OwnedField OwnedField,
        string? TextValue);

    private sealed record ParsedTxxx(
        string Description,
        string Value);

    private enum OwnedField
    {
        None,
        Track,
        Album
    }
}
