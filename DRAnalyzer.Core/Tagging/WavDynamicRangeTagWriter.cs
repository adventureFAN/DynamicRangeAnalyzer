using System.Buffers.Binary;
using System.Text;

namespace DRAnalyzer.Core.Tagging;

public static class WavDynamicRangeTagWriter
{
    private const string TrackDynamicRangeField = "DYNAMIC RANGE";
    private const string AlbumDynamicRangeField = "ALBUM DYNAMIC RANGE";
    private const int Id3HeaderLength = 10;
    private const int FrameHeaderLength = 10;
    private const int MaximumSynchsafeValue = 0x0FFFFFFF;

    private static readonly byte[] RiffMarker = Encoding.ASCII.GetBytes("RIFF");
    private static readonly byte[] WaveMarker = Encoding.ASCII.GetBytes("WAVE");
    private static readonly byte[] Id3Marker = Encoding.ASCII.GetBytes("ID3");

    public static void Write(string filePath, int trackDynamicRange, int? albumDynamicRange)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
            throw new FileNotFoundException("Die WAV-Datei wurde nicht gefunden.", filePath);

        if (trackDynamicRange < 0)
            throw new ArgumentOutOfRangeException(nameof(trackDynamicRange));

        if (albumDynamicRange is < 0)
            throw new ArgumentOutOfRangeException(nameof(albumDynamicRange));

        var fullPath = Path.GetFullPath(filePath);
        var source = ReadWave(fullPath);
        var sourceId3 = source.Id3Tag ?? ParsedId3Tag.WithoutId3();
        var targetVersion = sourceId3.Version ?? 4;

        var editedBody = BuildWrittenBody(
            sourceId3,
            targetVersion,
            trackDynamicRange,
            albumDynamicRange);

        var editedPayload = BuildId3Payload(targetVersion, editedBody);

        RewriteSafely(
            fullPath,
            source,
            editedPayload,
            removeId3Chunk: false,
            tempPath => ValidateWrittenCopy(
                fullPath,
                tempPath,
                source,
                targetVersion,
                trackDynamicRange,
                albumDynamicRange));
    }

    public static void Remove(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
            throw new FileNotFoundException("Die WAV-Datei wurde nicht gefunden.", filePath);

        var fullPath = Path.GetFullPath(filePath);
        var source = ReadWave(fullPath);
        var sourceId3 = source.Id3Tag;

        if (sourceId3 is null ||
            !sourceId3.Frames.Any(frame => frame.OwnedField != OwnedField.None))
        {
            return;
        }

        var hasForeignFrames = sourceId3.Frames.Any(frame => frame.OwnedField == OwnedField.None);

        if (!hasForeignFrames)
        {
            RewriteSafely(
                fullPath,
                source,
                editedId3Payload: null,
                removeId3Chunk: true,
                tempPath => ValidateRemovedChunkCopy(
                    fullPath,
                    tempPath,
                    source));

            return;
        }

        var editedBody = BuildRemovedBody(sourceId3);
        var editedPayload = BuildId3Payload(sourceId3.Version!.Value, editedBody);

        RewriteSafely(
            fullPath,
            source,
            editedPayload,
            removeId3Chunk: false,
            tempPath => ValidateRemovedFramesCopy(
                fullPath,
                tempPath,
                source));
    }

    private static byte[] BuildWrittenBody(
        ParsedId3Tag source,
        int targetVersion,
        int trackDynamicRange,
        int? albumDynamicRange)
    {
        var output = new List<byte>();
        var trackWritten = false;
        var albumWritten = false;

        foreach (var frame in source.Frames)
        {
            switch (frame.OwnedField)
            {
                case OwnedField.Track:
                    if (!trackWritten)
                    {
                        output.AddRange(BuildTxxxFrame(
                            targetVersion,
                            TrackDynamicRangeField,
                            trackDynamicRange.ToString()));
                        trackWritten = true;
                    }
                    break;

                case OwnedField.Album:
                    if (albumDynamicRange is null)
                    {
                        output.AddRange(frame.RawBytes);
                    }
                    else if (!albumWritten)
                    {
                        output.AddRange(BuildTxxxFrame(
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
            output.AddRange(BuildTxxxFrame(
                targetVersion,
                TrackDynamicRangeField,
                trackDynamicRange.ToString()));
        }

        if (albumDynamicRange is not null && !albumWritten)
        {
            output.AddRange(BuildTxxxFrame(
                targetVersion,
                AlbumDynamicRangeField,
                albumDynamicRange.Value.ToString()));
        }

        return AddPadding(output, source.BodyLength);
    }

    private static byte[] BuildRemovedBody(ParsedId3Tag source)
    {
        var output = new List<byte>();

        foreach (var frame in source.Frames)
        {
            if (frame.OwnedField == OwnedField.None)
                output.AddRange(frame.RawBytes);
        }

        return AddPadding(output, source.BodyLength);
    }

    private static byte[] AddPadding(List<byte> content, int originalBodyLength)
    {
        if (content.Count > MaximumSynchsafeValue)
            throw new InvalidDataException("Der resultierende ID3v2-Tag ist zu groß.");

        var targetLength = Math.Max(content.Count, originalBodyLength);

        if (targetLength > MaximumSynchsafeValue)
            throw new InvalidDataException("Der resultierende ID3v2-Tag ist zu groß.");

        if (targetLength == content.Count)
            return content.ToArray();

        var result = new byte[targetLength];
        content.CopyTo(result, 0);
        return result;
    }

    private static byte[] BuildId3Payload(int version, byte[] body)
    {
        var payload = new byte[Id3HeaderLength + body.Length];
        Id3Marker.CopyTo(payload, 0);
        payload[3] = checked((byte)version);
        payload[4] = 0;
        payload[5] = 0;
        WriteSynchsafeInteger(payload.AsSpan(6, 4), body.Length);
        body.CopyTo(payload, Id3HeaderLength);
        return payload;
    }

    private static byte[] BuildTxxxFrame(int version, string description, string value)
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
                encoding = new UTF8Encoding(false, true);
                break;
            default:
                throw new NotSupportedException($"ID3v2.{version} wird nicht unterstützt.");
        }

        var descriptionBytes = encoding.GetBytes(description);
        var valueBytes = encoding.GetBytes(value);
        var framePayload = new byte[1 + descriptionBytes.Length + 1 + valueBytes.Length];
        framePayload[0] = encodingByte;
        descriptionBytes.CopyTo(framePayload, 1);
        valueBytes.CopyTo(framePayload, 1 + descriptionBytes.Length + 1);

        var frame = new byte[FrameHeaderLength + framePayload.Length];
        Encoding.ASCII.GetBytes("TXXX").CopyTo(frame, 0);
        WriteFrameSize(frame.AsSpan(4, 4), version, framePayload.Length);
        framePayload.CopyTo(frame, FrameHeaderLength);
        return frame;
    }

    private static void RewriteSafely(
        string fullPath,
        ParsedWave source,
        byte[]? editedId3Payload,
        bool removeId3Chunk,
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
            WriteModifiedCopy(fullPath, tempPath, source, editedId3Payload, removeId3Chunk);
            validateTemp(tempPath);

            File.Replace(tempPath, fullPath, backupPath, ignoreMetadataErrors: true);
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
        ParsedWave source,
        byte[]? editedId3Payload,
        bool removeId3Chunk)
    {
        using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var output = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);

        output.Write(RiffMarker);
        output.Write(new byte[4]); // RIFF size is patched after all chunks are written.
        output.Write(WaveMarker);

        var wroteId3 = false;

        foreach (var chunk in source.Chunks)
        {
            if (chunk.IsId3)
            {
                if (removeId3Chunk)
                    continue;

                if (editedId3Payload is null)
                    throw new InvalidOperationException("Es fehlt der neue ID3v2-Inhalt.");

                WriteChunk(output, chunk.Id, editedId3Payload);
                wroteId3 = true;
                continue;
            }

            CopyRange(input, output, chunk.HeaderOffset, chunk.TotalLength);
        }

        if (!removeId3Chunk && !wroteId3)
        {
            if (editedId3Payload is null)
                throw new InvalidOperationException("Es fehlt der neue ID3v2-Inhalt.");

            WriteChunk(output, "ID3 ", editedId3Payload);
        }

        var riffSize = output.Length - 8;
        if (riffSize > uint.MaxValue)
            throw new NotSupportedException("WAV-Dateien, die nach dem Tagging die RIFF-4-GB-Grenze überschreiten, werden derzeit nicht unterstützt.");

        output.Position = 4;
        Span<byte> riffSizeBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(riffSizeBytes, checked((uint)riffSize));
        output.Write(riffSizeBytes);
        output.Flush(flushToDisk: true);
    }

    private static void WriteChunk(Stream output, string id, byte[] payload)
    {
        Span<byte> header = stackalloc byte[8];
        Encoding.ASCII.GetBytes(id).AsSpan().CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], checked((uint)payload.Length));
        output.Write(header);
        output.Write(payload);
        if ((payload.Length & 1) != 0)
            output.WriteByte(0);
    }

    private static void CopyRange(Stream input, Stream output, long offset, long length)
    {
        input.Position = offset;
        var buffer = new byte[64 * 1024];
        var remaining = length;

        while (remaining > 0)
        {
            var wanted = (int)Math.Min(buffer.Length, remaining);
            var read = input.Read(buffer, 0, wanted);
            if (read == 0)
                throw new EndOfStreamException("Unerwartetes Dateiende beim Kopieren eines RIFF-Chunks.");
            output.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    private static void ValidateWrittenCopy(
        string sourcePath,
        string destinationPath,
        ParsedWave source,
        int targetVersion,
        int trackDynamicRange,
        int? albumDynamicRange)
    {
        var destination = ReadWave(destinationPath);
        var destinationId3 = destination.Id3Tag
            ?? throw new InvalidDataException("Der geschriebene WAV enthält keinen ID3v2-Chunk.");

        if (destinationId3.Version != targetVersion)
            throw new InvalidDataException("Der geschriebene WAV enthält nicht die erwartete ID3v2-Version.");

        var trackFrames = destinationId3.Frames.Where(frame => frame.OwnedField == OwnedField.Track).ToArray();
        if (trackFrames.Length != 1 ||
            !string.Equals(trackFrames[0].TextValue, trackDynamicRange.ToString(), StringComparison.Ordinal))
        {
            throw new InvalidDataException("Der geschriebene DYNAMIC RANGE-TXXX-Frame ist ungültig.");
        }

        var albumFrames = destinationId3.Frames.Where(frame => frame.OwnedField == OwnedField.Album).ToArray();
        if (albumDynamicRange is not null)
        {
            if (albumFrames.Length != 1 ||
                !string.Equals(albumFrames[0].TextValue, albumDynamicRange.Value.ToString(), StringComparison.Ordinal))
            {
                throw new InvalidDataException("Der geschriebene ALBUM DYNAMIC RANGE-TXXX-Frame ist ungültig.");
            }
        }
        else
        {
            var sourceAlbum = (source.Id3Tag?.Frames ?? Array.Empty<ParsedFrame>())
                .Where(frame => frame.OwnedField == OwnedField.Album)
                .Select(frame => frame.RawBytes)
                .ToArray();
            var destinationAlbum = albumFrames.Select(frame => frame.RawBytes).ToArray();
            AssertRawFrameSequenceEqual(sourceAlbum, destinationAlbum,
                "Vorhandene ALBUM DYNAMIC RANGE-Frames wurden bei Track-only-Write verändert.");
        }

        ValidateForeignId3FramesPreserved(source.Id3Tag, destinationId3);
        ValidateNonId3ChunksPreserved(sourcePath, destinationPath, source, destination);
    }

    private static void ValidateRemovedFramesCopy(
        string sourcePath,
        string destinationPath,
        ParsedWave source)
    {
        var destination = ReadWave(destinationPath);
        var destinationId3 = destination.Id3Tag
            ?? throw new InvalidDataException("Der bestehende fremde ID3v2-Chunk wurde beim Remove entfernt.");

        if (destinationId3.Frames.Any(frame => frame.OwnedField != OwnedField.None))
            throw new InvalidDataException("Eigene DR-TXXX-Frames sind nach Remove weiterhin vorhanden.");

        ValidateForeignId3FramesPreserved(source.Id3Tag, destinationId3);
        ValidateNonId3ChunksPreserved(sourcePath, destinationPath, source, destination);
    }

    private static void ValidateRemovedChunkCopy(
        string sourcePath,
        string destinationPath,
        ParsedWave source)
    {
        var destination = ReadWave(destinationPath);
        if (destination.Id3Tag is not null)
            throw new InvalidDataException("Der ausschließlich aus DRAnalyzer-Feldern bestehende ID3v2-Chunk wurde nicht vollständig entfernt.");

        ValidateNonId3ChunksPreserved(sourcePath, destinationPath, source, destination);
    }

    private static void ValidateForeignId3FramesPreserved(ParsedId3Tag? source, ParsedId3Tag destination)
    {
        var sourceFrames = (source?.Frames ?? Array.Empty<ParsedFrame>())
            .Where(frame => frame.OwnedField == OwnedField.None)
            .Select(frame => frame.RawBytes)
            .ToArray();
        var destinationFrames = destination.Frames
            .Where(frame => frame.OwnedField == OwnedField.None)
            .Select(frame => frame.RawBytes)
            .ToArray();

        AssertRawFrameSequenceEqual(sourceFrames, destinationFrames,
            "Fremde ID3v2-Frames im WAV wurden verändert oder umsortiert.");
    }

    private static void AssertRawFrameSequenceEqual(byte[][] expected, byte[][] actual, string message)
    {
        if (expected.Length != actual.Length)
            throw new InvalidDataException(message);

        for (var i = 0; i < expected.Length; i++)
        {
            if (!expected[i].AsSpan().SequenceEqual(actual[i]))
                throw new InvalidDataException(message);
        }
    }

    private static void ValidateNonId3ChunksPreserved(
        string sourcePath,
        string destinationPath,
        ParsedWave source,
        ParsedWave destination)
    {
        var sourceChunks = source.Chunks.Where(chunk => !chunk.IsId3).ToArray();
        var destinationChunks = destination.Chunks.Where(chunk => !chunk.IsId3).ToArray();

        if (sourceChunks.Length != destinationChunks.Length)
            throw new InvalidDataException("Fremde RIFF-Chunks wurden hinzugefügt oder entfernt.");

        using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var destinationStream = new FileStream(destinationPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        for (var i = 0; i < sourceChunks.Length; i++)
        {
            if (!string.Equals(sourceChunks[i].Id, destinationChunks[i].Id, StringComparison.Ordinal) ||
                sourceChunks[i].TotalLength != destinationChunks[i].TotalLength)
            {
                throw new InvalidDataException("Fremde RIFF-Chunks wurden verändert oder umsortiert.");
            }

            AssertRangesEqual(
                sourceStream,
                sourceChunks[i].HeaderOffset,
                destinationStream,
                destinationChunks[i].HeaderOffset,
                sourceChunks[i].TotalLength,
                "Ein fremder RIFF-Chunk wurde byteweise verändert.");
        }
    }

    private static void AssertRangesEqual(
        Stream first,
        long firstOffset,
        Stream second,
        long secondOffset,
        long length,
        string message)
    {
        first.Position = firstOffset;
        second.Position = secondOffset;
        var firstBuffer = new byte[64 * 1024];
        var secondBuffer = new byte[64 * 1024];
        var remaining = length;

        while (remaining > 0)
        {
            var wanted = (int)Math.Min(firstBuffer.Length, remaining);
            var firstRead = first.Read(firstBuffer, 0, wanted);
            var secondRead = second.Read(secondBuffer, 0, wanted);

            if (firstRead != secondRead || firstRead == 0)
                throw new InvalidDataException(message);

            if (!firstBuffer.AsSpan(0, firstRead).SequenceEqual(secondBuffer.AsSpan(0, secondRead)))
                throw new InvalidDataException(message);

            remaining -= firstRead;
        }
    }

    private static ParsedWave ReadWave(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        if (stream.Length < 12)
            throw new InvalidDataException("Die WAV-Datei ist zu kurz für einen RIFF/WAVE-Header.");

        Span<byte> header = stackalloc byte[12];
        ReadExactly(stream, header);

        var container = Encoding.ASCII.GetString(header[..4]);
        if (!string.Equals(container, "RIFF", StringComparison.Ordinal))
        {
            if (container is "RF64" or "BW64" or "RIFX")
                throw new NotSupportedException($"{container}-WAV wird für DR-Tagging derzeit nicht unterstützt; unterstützt wird nur klassisches RIFF/WAVE.");

            throw new InvalidDataException("Die Datei besitzt keinen RIFF/WAVE-Header.");
        }

        if (!header[8..12].SequenceEqual(WaveMarker))
            throw new InvalidDataException("Der RIFF-Container ist keine WAVE-Datei.");

        var declaredRiffSize = BinaryPrimitives.ReadUInt32LittleEndian(header[4..8]);
        if ((long)declaredRiffSize + 8 != stream.Length)
            throw new InvalidDataException("Die RIFF-Größenangabe entspricht nicht exakt der Dateigröße.");

        var chunks = new List<RiffChunk>();
        ParsedId3Tag? id3Tag = null;
        var gotFmt = false;
        var gotData = false;
        Span<byte> chunkHeader = stackalloc byte[8];

        while (stream.Position < stream.Length)
        {
            var headerOffset = stream.Position;
            if (stream.Length - headerOffset < 8)
                throw new InvalidDataException("Die WAV-Datei enthält einen abgeschnittenen RIFF-Chunk-Header.");

            ReadExactly(stream, chunkHeader);
            var id = Encoding.ASCII.GetString(chunkHeader[..4]);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader[4..8]);
            var dataOffset = stream.Position;
            var paddedSize = checked((long)size + (size & 1));
            var totalLength = checked(8L + paddedSize);

            if (dataOffset + paddedSize > stream.Length)
                throw new InvalidDataException($"Der RIFF-Chunk '{id}' reicht über das Dateiende hinaus.");

            if (id == "fmt ")
                gotFmt = true;
            else if (id == "data")
                gotData = true;

            var isId3 = id is "ID3 " or "id3 ";
            if (isId3)
            {
                if (id3Tag is not null)
                    throw new NotSupportedException("WAV-Dateien mit mehreren ID3v2-Chunks werden aus Sicherheitsgründen nicht verändert.");

                if (size > int.MaxValue)
                    throw new NotSupportedException("Der ID3v2-Chunk ist zu groß.");

                var payload = new byte[(int)size];
                ReadExactly(stream, payload);
                id3Tag = ParseId3Payload(payload);
            }
            else
            {
                stream.Position = dataOffset + size;
            }

            if ((size & 1) != 0)
            {
                var pad = stream.ReadByte();
                if (pad < 0)
                    throw new EndOfStreamException("Der RIFF-Chunk besitzt kein erforderliches Padding-Byte.");
            }

            chunks.Add(new RiffChunk(id, headerOffset, dataOffset, size, totalLength, isId3));
        }

        if (!gotFmt)
            throw new InvalidDataException("Die WAV-Datei enthält keinen 'fmt '-Chunk.");
        if (!gotData)
            throw new InvalidDataException("Die WAV-Datei enthält keinen 'data'-Chunk.");

        return new ParsedWave(chunks, id3Tag);
    }

    private static ParsedId3Tag ParseId3Payload(byte[] payload)
    {
        if (payload.Length < Id3HeaderLength || !payload.AsSpan(0, 3).SequenceEqual(Id3Marker))
            throw new InvalidDataException("Der WAV-ID3-Chunk enthält keinen gültigen ID3v2-Header.");

        var version = payload[3];
        var revision = payload[4];
        var flags = payload[5];

        if (version is not 3 and not 4)
            throw new NotSupportedException($"ID3v2.{version} wird für WAV-DR-Tags nicht unterstützt. Unterstützt werden nur ID3v2.3 und ID3v2.4.");
        if (revision != 0)
            throw new NotSupportedException($"ID3v2.{version}.{revision} wird aus Sicherheitsgründen nicht verändert.");
        if (flags != 0)
            throw new NotSupportedException($"ID3v2.{version}-Tags mit Sonderflags werden derzeit aus Sicherheitsgründen nicht verändert.");

        var bodyLength = ReadSynchsafeInteger(payload.AsSpan(6, 4));
        if (Id3HeaderLength + bodyLength != payload.Length)
            throw new InvalidDataException("Die ID3v2-Größenangabe entspricht nicht exakt der Größe des WAV-ID3-Chunks.");

        var body = payload.AsSpan(Id3HeaderLength, bodyLength).ToArray();
        var frames = ParseFrames(body, version);
        return new ParsedId3Tag(version, bodyLength, frames);
    }

    private static IReadOnlyList<ParsedFrame> ParseFrames(byte[] body, int version)
    {
        var frames = new List<ParsedFrame>();
        var offset = 0;

        while (offset < body.Length)
        {
            var remaining = body.Length - offset;
            if (body[offset] == 0)
            {
                for (var i = offset; i < body.Length; i++)
                {
                    if (body[i] != 0)
                        throw new InvalidDataException("Der ID3v2-Tag enthält ungültige Daten nach dem Padding-Beginn.");
                }
                break;
            }

            if (remaining < FrameHeaderLength)
                throw new InvalidDataException("Der ID3v2-Tag enthält einen abgeschnittenen Frame-Header.");

            var frameIdBytes = body.AsSpan(offset, 4);
            if (!IsValidFrameId(frameIdBytes))
                throw new InvalidDataException("Der ID3v2-Tag enthält eine ungültige Frame-ID.");

            var frameId = Encoding.ASCII.GetString(frameIdBytes);
            var payloadLength = ReadFrameSize(body.AsSpan(offset + 4, 4), version);
            if (payloadLength <= 0)
                throw new InvalidDataException($"Der ID3v2-Frame '{frameId}' besitzt eine ungültige Größe.");

            var totalLength = checked(FrameHeaderLength + payloadLength);
            if (totalLength > remaining)
                throw new InvalidDataException($"Der ID3v2-Frame '{frameId}' reicht über den Tag hinaus.");

            var rawBytes = body.AsSpan(offset, totalLength).ToArray();
            var owned = OwnedField.None;
            string? textValue = null;

            if (string.Equals(frameId, "TXXX", StringComparison.Ordinal))
            {
                if (body[offset + 8] != 0 || body[offset + 9] != 0)
                    throw new NotSupportedException("TXXX-Frames mit ID3v2-Frame-Flags werden aus Sicherheitsgründen nicht verändert.");

                var parsed = ParseTxxxPayload(body.AsSpan(offset + FrameHeaderLength, payloadLength), version);
                if (string.Equals(parsed.Description, TrackDynamicRangeField, StringComparison.OrdinalIgnoreCase))
                {
                    owned = OwnedField.Track;
                    textValue = parsed.Value;
                }
                else if (string.Equals(parsed.Description, AlbumDynamicRangeField, StringComparison.OrdinalIgnoreCase))
                {
                    owned = OwnedField.Album;
                    textValue = parsed.Value;
                }
            }

            frames.Add(new ParsedFrame(frameId, rawBytes, owned, textValue));
            offset += totalLength;
        }

        return frames;
    }

    private static ParsedTxxx ParseTxxxPayload(ReadOnlySpan<byte> payload, int version)
    {
        if (payload.Length < 2)
            throw new InvalidDataException("Ein TXXX-Frame ist zu kurz.");

        var encodingByte = payload[0];
        if (version == 3 && encodingByte is not 0 and not 1)
            throw new NotSupportedException("ID3v2.3-TXXX mit nicht unterstützter Textkodierung wird aus Sicherheitsgründen nicht verändert.");
        if (version == 4 && encodingByte is not 0 and not 1 and not 2 and not 3)
            throw new NotSupportedException("ID3v2.4-TXXX mit unbekannter Textkodierung wird aus Sicherheitsgründen nicht verändert.");

        var textBytes = payload[1..];
        return encodingByte switch
        {
            0 => ParseSingleByteTxxx(textBytes, Encoding.Latin1),
            3 => ParseSingleByteTxxx(textBytes, new UTF8Encoding(false, true)),
            1 => ParseUtf16WithBomTxxx(textBytes),
            2 => ParseUtf16BigEndianTxxx(textBytes),
            _ => throw new InvalidDataException("Unbekannte TXXX-Textkodierung.")
        };
    }

    private static ParsedTxxx ParseSingleByteTxxx(ReadOnlySpan<byte> textBytes, Encoding encoding)
    {
        var separator = textBytes.IndexOf((byte)0);
        if (separator < 0)
            throw new InvalidDataException("Ein TXXX-Frame besitzt keine terminierte Description.");

        try
        {
            return new ParsedTxxx(
                encoding.GetString(textBytes[..separator]),
                encoding.GetString(textBytes[(separator + 1)..]));
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("Ein TXXX-Frame enthält ungültigen Text.", exception);
        }
    }

    private static ParsedTxxx ParseUtf16WithBomTxxx(ReadOnlySpan<byte> textBytes)
    {
        if (textBytes.Length < 4)
            throw new InvalidDataException("Ein UTF-16-TXXX-Frame ist zu kurz.");

        Encoding encoding;
        if (textBytes[0] == 0xFF && textBytes[1] == 0xFE)
            encoding = new UnicodeEncoding(false, false, true);
        else if (textBytes[0] == 0xFE && textBytes[1] == 0xFF)
            encoding = new UnicodeEncoding(true, false, true);
        else
            throw new InvalidDataException("Ein ID3v2-TXXX mit Encoding 1 besitzt keine UTF-16-BOM.");

        var content = textBytes[2..];
        var separator = FindUtf16Terminator(content);
        if (separator < 0)
            throw new InvalidDataException("Ein UTF-16-TXXX besitzt keine terminierte Description.");

        try
        {
            var valueBytes = content[(separator + 2)..];
            if (valueBytes.Length >= 2 &&
                ((valueBytes[0] == 0xFF && valueBytes[1] == 0xFE) ||
                 (valueBytes[0] == 0xFE && valueBytes[1] == 0xFF)))
            {
                valueBytes = valueBytes[2..];
            }

            return new ParsedTxxx(
                encoding.GetString(content[..separator]),
                encoding.GetString(valueBytes));
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("Ein UTF-16-TXXX enthält ungültigen Text.", exception);
        }
    }

    private static ParsedTxxx ParseUtf16BigEndianTxxx(ReadOnlySpan<byte> textBytes)
    {
        var separator = FindUtf16Terminator(textBytes);
        if (separator < 0)
            throw new InvalidDataException("Ein UTF-16BE-TXXX besitzt keine terminierte Description.");

        var encoding = new UnicodeEncoding(true, false, true);
        try
        {
            return new ParsedTxxx(
                encoding.GetString(textBytes[..separator]),
                encoding.GetString(textBytes[(separator + 2)..]));
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("Ein UTF-16BE-TXXX enthält ungültigen Text.", exception);
        }
    }

    private static int FindUtf16Terminator(ReadOnlySpan<byte> bytes)
    {
        for (var i = 0; i + 1 < bytes.Length; i += 2)
        {
            if (bytes[i] == 0 && bytes[i + 1] == 0)
                return i;
        }
        return -1;
    }

    private static bool IsValidFrameId(ReadOnlySpan<byte> frameId)
    {
        if (frameId.Length != 4)
            return false;

        foreach (var value in frameId)
        {
            var letter = value is >= (byte)'A' and <= (byte)'Z';
            var digit = value is >= (byte)'0' and <= (byte)'9';
            if (!letter && !digit)
                return false;
        }
        return true;
    }

    private static int ReadFrameSize(ReadOnlySpan<byte> bytes, int version) => version switch
    {
        3 => ReadBigEndianInt32(bytes),
        4 => ReadSynchsafeInteger(bytes),
        _ => throw new NotSupportedException()
    };

    private static void WriteFrameSize(Span<byte> destination, int version, int value)
    {
        switch (version)
        {
            case 3:
                BinaryPrimitives.WriteInt32BigEndian(destination, value);
                break;
            case 4:
                WriteSynchsafeInteger(destination, value);
                break;
            default:
                throw new NotSupportedException();
        }
    }

    private static int ReadBigEndianInt32(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 4)
            throw new ArgumentException("Es werden genau vier Bytes erwartet.", nameof(bytes));

        var value = BinaryPrimitives.ReadInt32BigEndian(bytes);
        if (value < 0)
            throw new InvalidDataException("Eine ID3v2-Frame-Größe ist negativ.");
        return value;
    }

    private static int ReadSynchsafeInteger(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 4)
            throw new ArgumentException("Es werden genau vier Bytes erwartet.", nameof(bytes));

        var value = 0;
        foreach (var current in bytes)
        {
            if ((current & 0x80) != 0)
                throw new InvalidDataException("Eine ID3v2-Synchsafe-Größe ist ungültig.");
            value = (value << 7) | current;
        }
        return value;
    }

    private static void WriteSynchsafeInteger(Span<byte> destination, int value)
    {
        if (destination.Length != 4)
            throw new ArgumentException("Es werden genau vier Bytes erwartet.", nameof(destination));
        if (value < 0 || value > MaximumSynchsafeValue)
            throw new ArgumentOutOfRangeException(nameof(value));

        destination[0] = (byte)((value >> 21) & 0x7F);
        destination[1] = (byte)((value >> 14) & 0x7F);
        destination[2] = (byte)((value >> 7) & 0x7F);
        destination[3] = (byte)(value & 0x7F);
    }

    private static void ReadExactly(Stream stream, Span<byte> destination)
    {
        var total = 0;
        while (total < destination.Length)
        {
            var read = stream.Read(destination[total..]);
            if (read == 0)
                throw new EndOfStreamException("Unerwartetes Dateiende beim Lesen der WAV-Datei.");
            total += read;
        }
    }

    private static void ReadExactly(Stream stream, byte[] destination) => ReadExactly(stream, destination.AsSpan());

    private sealed record ParsedWave(IReadOnlyList<RiffChunk> Chunks, ParsedId3Tag? Id3Tag);

    private sealed record RiffChunk(
        string Id,
        long HeaderOffset,
        long DataOffset,
        uint DataLength,
        long TotalLength,
        bool IsId3);

    private sealed record ParsedId3Tag(
        int? Version,
        int BodyLength,
        IReadOnlyList<ParsedFrame> Frames)
    {
        public static ParsedId3Tag WithoutId3() => new(null, 0, Array.Empty<ParsedFrame>());
    }

    private sealed record ParsedFrame(
        string FrameId,
        byte[] RawBytes,
        OwnedField OwnedField,
        string? TextValue);

    private sealed record ParsedTxxx(string Description, string Value);

    private enum OwnedField
    {
        None,
        Track,
        Album
    }
}
