using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using DRAnalyzer.Core.Analysis;
using DRAnalyzer.Core.Metadata;
using DRAnalyzer.Core.Tagging;

namespace DRAnalyzer.Tests;

[Trait("Category", "ExternalReference")]
public sealed class ManualRealAiffPreservationTests
{
    [Fact]
    public void ReferenceCopy_WriteAndRemove_PreservesRealAiffFile()
    {
        var albumDirectory =
            Environment.GetEnvironmentVariable(
                "DRANALYZER_REFERENCE_AIFF_DIR");

        Assert.False(
            string.IsNullOrWhiteSpace(albumDirectory),
            "DRANALYZER_REFERENCE_AIFF_DIR ist nicht gesetzt.");

        Assert.True(
            Directory.Exists(albumDirectory),
            $"AIFF-Referenzordner fehlt: {albumDirectory}");

        var originalPath =
            FindFirstSupportedAiff(albumDirectory!);

        Assert.False(
            string.IsNullOrWhiteSpace(originalPath),
            "Im Referenzordner wurde keine vom aktuellen AIFF-Writer unterstützte .aiff/.aif-Datei gefunden.");

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
            GetOwnedValues(beforeSnapshot.Id3Frames);

        var tempDirectory =
            Path.Combine(
                Path.GetTempPath(),
                "DRAnalyzer-AiffPreservation-" +
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

            AiffDynamicRangeTagWriter.Write(
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

            AssertForeignChunksPreserved(
                beforeSnapshot,
                afterWriteSnapshot);

            AssertForeignId3FramesPreserved(
                beforeSnapshot,
                afterWriteSnapshot);

            var afterWriteOwned =
                GetOwnedValues(afterWriteSnapshot.Id3Frames);

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

            AiffDynamicRangeTagWriter.Remove(copyPath);

            AssertNoWriterResidues(tempDirectory);

            var afterRemoveBytes =
                File.ReadAllBytes(copyPath);

            var afterRemoveSnapshot =
                ReadSnapshot(afterRemoveBytes);

            var afterRemoveMetadata =
                AudioMetadataReader.Read(copyPath);

            var afterRemoveAnalysis =
                DynamicRangeAnalyzer.Analyze(copyPath);

            AssertForeignChunksPreserved(
                beforeSnapshot,
                afterRemoveSnapshot);

            AssertForeignId3FramesPreserved(
                beforeSnapshot,
                afterRemoveSnapshot);

            var afterRemoveOwned =
                GetOwnedValues(afterRemoveSnapshot.Id3Frames);

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
                beforeOwned.Album.Count == 0 &&
                beforeSnapshot.Id3Chunk is null)
            {
                Assert.True(
                    originalBytes
                        .AsSpan()
                        .SequenceEqual(afterRemoveBytes),
                    "Write -> Remove hat eine ursprünglich ID3-/DR-taglose AIFF-Datei nicht bytegenau wiederhergestellt.");
            }

            var originalHashAfter =
                CalculateSha256(originalPath!);

            Assert.Equal(
                originalHashBefore,
                originalHashAfter);

            var ssndChunk =
                Assert.Single(
                    beforeSnapshot.Chunks,
                    chunk => chunk.Id == "SSND");

            Console.WriteLine(
                $"AIFF realfile: {Path.GetFileName(originalPath)}");

            Console.WriteLine(
                $"Channels: {beforeAnalysis.Channels}");

            Console.WriteLine(
                $"SSND payload bytes preserved: {ssndChunk.PayloadLength}");

            Console.WriteLine(
                $"Foreign IFF chunks preserved: {beforeSnapshot.Chunks.Count(chunk => !chunk.IsId3)}");

            Console.WriteLine(
                $"Foreign ID3 frames preserved: {GetForeignFrames(beforeSnapshot.Id3Frames).Length}");

            Console.WriteLine(
                "Write DR: 20 / Album DR: 21");

            Console.WriteLine(
                "Remove: owned DR TXXX frames removed");

            Console.WriteLine(
                "Re-analysis after Write and Remove successful");

            if (beforeSnapshot.Id3Chunk is null)
            {
                Console.WriteLine(
                    "Write -> Remove restored the complete originally ID3-free test copy byte-exactly");
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

    private static string? FindFirstSupportedAiff(
        string albumDirectory)
    {
        foreach (var path in Directory
                     .EnumerateFiles(
                         albumDirectory,
                         "*",
                         SearchOption.AllDirectories)
                     .Where(path =>
                         string.Equals(Path.GetExtension(path), ".aiff", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(Path.GetExtension(path), ".aif", StringComparison.OrdinalIgnoreCase))
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

    private static AiffSnapshot ReadSnapshot(
        byte[] bytes)
    {
        if (bytes.Length < 12)
            throw new InvalidDataException("AIFF-Datei ist zu kurz.");

        if (!bytes.AsSpan(0, 4).SequenceEqual(Encoding.ASCII.GetBytes("FORM")) ||
            !bytes.AsSpan(8, 4).SequenceEqual(Encoding.ASCII.GetBytes("AIFF")))
        {
            throw new NotSupportedException("Nur klassisches FORM/AIFF wird für diesen Test akzeptiert.");
        }

        var declaredSize =
            BinaryPrimitives.ReadUInt32BigEndian(
                bytes.AsSpan(4, 4));

        if ((long)declaredSize + 8 != bytes.Length)
            throw new InvalidDataException("FORM-Größe stimmt nicht mit der Dateigröße überein.");

        var chunks =
            new List<IffChunkSnapshot>();

        IffChunkSnapshot? id3Chunk = null;
        var id3Frames = Array.Empty<Id3FrameSnapshot>();
        var sawComm = false;
        var sawSsnd = false;
        var offset = 12;

        while (offset < bytes.Length)
        {
            if (bytes.Length - offset < 8)
                throw new InvalidDataException("Abgeschnittener IFF-Chunk-Header.");

            var id =
                Encoding.ASCII.GetString(
                    bytes,
                    offset,
                    4);

            var payloadLength =
                checked((int)BinaryPrimitives.ReadUInt32BigEndian(
                    bytes.AsSpan(offset + 4, 4)));

            var paddedPayloadLength =
                checked(payloadLength + (payloadLength & 1));

            var totalLength =
                checked(8 + paddedPayloadLength);

            if (offset + totalLength > bytes.Length)
                throw new InvalidDataException($"IFF-Chunk '{id}' reicht über das Dateiende hinaus.");

            var raw =
                bytes.AsSpan(
                        offset,
                        totalLength)
                    .ToArray();

            var isId3 =
                id is "ID3 " or "id3 ";

            var chunk =
                new IffChunkSnapshot(
                    id,
                    payloadLength,
                    raw,
                    isId3);

            chunks.Add(chunk);

            if (id == "COMM")
                sawComm = true;

            if (id == "SSND")
                sawSsnd = true;

            if (isId3)
            {
                if (id3Chunk is not null)
                    throw new NotSupportedException("Mehrere ID3-Chunks werden nicht unterstützt.");

                id3Chunk = chunk;
                id3Frames = ParseId3Frames(raw.AsSpan(8, payloadLength).ToArray());
            }

            offset += totalLength;
        }

        if (!sawComm || !sawSsnd)
            throw new InvalidDataException("AIFF benötigt COMM- und SSND-Chunk.");

        return new AiffSnapshot(
            chunks,
            id3Chunk,
            id3Frames);
    }

    private static Id3FrameSnapshot[] ParseId3Frames(
        byte[] payload)
    {
        if (payload.Length < 10 ||
            !payload.AsSpan(0, 3).SequenceEqual(Encoding.ASCII.GetBytes("ID3")))
        {
            throw new InvalidDataException("Ungültiger ID3v2-Header.");
        }

        var version = payload[3];
        if (version is not 3 and not 4)
            throw new NotSupportedException($"ID3v2.{version} wird nicht unterstützt.");

        if (payload[4] != 0 || payload[5] != 0)
            throw new NotSupportedException("ID3v2-Sonderversion/-flags werden nicht unterstützt.");

        var bodyLength =
            ReadSynchsafe(payload.AsSpan(6, 4));

        var declaredLength =
            checked(10 + bodyLength);

        if (declaredLength > payload.Length)
            throw new InvalidDataException("Die ID3v2-Größenangabe reicht über den AIFF-ID3-Chunk hinaus.");

        var trailingChunkPadding =
            payload.AsSpan(declaredLength);

        if (!trailingChunkPadding.ToArray().All(value => value == 0))
            throw new InvalidDataException("Der AIFF-ID3-Chunk enthält nach dem deklarierten ID3v2-Tag nicht-null Padding-Daten.");

        var frames =
            new List<Id3FrameSnapshot>();

        var body =
            payload.AsSpan(10, bodyLength);

        var offset = 0;

        while (offset < body.Length)
        {
            if (body[offset] == 0)
            {
                if (!body[offset..].ToArray().All(value => value == 0))
                    throw new InvalidDataException("Ungültiges ID3-Padding.");

                break;
            }

            if (body.Length - offset < 10)
                throw new InvalidDataException("Abgeschnittener ID3-Frame-Header.");

            var id =
                Encoding.ASCII.GetString(
                    body.Slice(offset, 4));

            if (!id.All(ch => ch is >= 'A' and <= 'Z' or >= '0' and <= '9'))
                throw new InvalidDataException($"Ungültige ID3-Frame-ID '{id}'.");

            var frameSize =
                version == 3
                    ? checked((int)BinaryPrimitives.ReadUInt32BigEndian(body.Slice(offset + 4, 4)))
                    : ReadSynchsafe(body.Slice(offset + 4, 4));

            if (frameSize < 0 || body.Length - offset - 10 < frameSize)
                throw new InvalidDataException($"ID3-Frame '{id}' ist abgeschnitten.");

            var raw =
                body.Slice(
                        offset,
                        10 + frameSize)
                    .ToArray();

            var owned =
                id == "TXXX"
                    ? ReadOwnedTxxx(
                        raw.AsSpan(10, frameSize),
                        version)
                    : null;

            frames.Add(
                new Id3FrameSnapshot(
                    id,
                    raw,
                    owned?.Field,
                    owned?.Value));

            offset += 10 + frameSize;
        }

        return frames.ToArray();
    }

    private static OwnedTxxx? ReadOwnedTxxx(
        ReadOnlySpan<byte> payload,
        int version)
    {
        if (payload.Length < 2)
            return null;

        string description;
        string value;

        switch (payload[0])
        {
            case 0:
            case 3:
            {
                var bytes = payload[1..];
                var terminator = bytes.IndexOf((byte)0);
                if (terminator < 0)
                    return null;

                var encoding = payload[0] == 0 ? Encoding.Latin1 : Encoding.UTF8;
                description = encoding.GetString(bytes[..terminator]);
                value = encoding.GetString(bytes[(terminator + 1)..]).TrimEnd('\0');
                break;
            }

            case 1:
            case 2:
            {
                var bytes = payload[1..];
                var terminator = FindUtf16Terminator(bytes);
                if (terminator < 0)
                    return null;

                Encoding encoding;
                var descriptionBytes = bytes[..terminator];
                var valueBytes = bytes[(terminator + 2)..];

                if (payload[0] == 2)
                {
                    encoding = Encoding.BigEndianUnicode;
                }
                else if (descriptionBytes.Length >= 2 &&
                         descriptionBytes[0] == 0xFE &&
                         descriptionBytes[1] == 0xFF)
                {
                    encoding = Encoding.BigEndianUnicode;
                    descriptionBytes = descriptionBytes[2..];
                }
                else
                {
                    encoding = Encoding.Unicode;
                    if (descriptionBytes.Length >= 2 &&
                        descriptionBytes[0] == 0xFF &&
                        descriptionBytes[1] == 0xFE)
                    {
                        descriptionBytes = descriptionBytes[2..];
                    }
                }

                description = encoding.GetString(descriptionBytes);
                value = encoding.GetString(valueBytes).TrimEnd('\0');
                break;
            }

            default:
                return null;
        }

        if (string.Equals(
                description,
                "DYNAMIC RANGE",
                StringComparison.OrdinalIgnoreCase))
        {
            return new OwnedTxxx("Track", value);
        }

        if (string.Equals(
                description,
                "ALBUM DYNAMIC RANGE",
                StringComparison.OrdinalIgnoreCase))
        {
            return new OwnedTxxx("Album", value);
        }

        return null;
    }

    private static int FindUtf16Terminator(
        ReadOnlySpan<byte> bytes)
    {
        for (var index = 0;
             index + 1 < bytes.Length;
             index += 2)
        {
            if (bytes[index] == 0 && bytes[index + 1] == 0)
                return index;
        }

        return -1;
    }

    private static void AssertForeignChunksPreserved(
        AiffSnapshot before,
        AiffSnapshot after)
    {
        var beforeForeign =
            before.Chunks
                .Where(chunk => !chunk.IsId3)
                .ToArray();

        var afterForeign =
            after.Chunks
                .Where(chunk => !chunk.IsId3)
                .ToArray();

        Assert.Equal(
            beforeForeign.Length,
            afterForeign.Length);

        for (var index = 0;
             index < beforeForeign.Length;
             index++)
        {
            Assert.Equal(
                beforeForeign[index].Id,
                afterForeign[index].Id);

            Assert.True(
                beforeForeign[index].RawBytes
                    .AsSpan()
                    .SequenceEqual(afterForeign[index].RawBytes),
                $"IFF-Chunk '{beforeForeign[index].Id}' an Position {index} wurde verändert oder umsortiert.");
        }
    }

    private static void AssertForeignId3FramesPreserved(
        AiffSnapshot before,
        AiffSnapshot after)
    {
        var beforeForeign =
            GetForeignFrames(before.Id3Frames);

        var afterForeign =
            GetForeignFrames(after.Id3Frames);

        Assert.Equal(
            beforeForeign.Length,
            afterForeign.Length);

        for (var index = 0;
             index < beforeForeign.Length;
             index++)
        {
            Assert.True(
                beforeForeign[index].RawBytes
                    .AsSpan()
                    .SequenceEqual(afterForeign[index].RawBytes),
                $"Fremder ID3-Frame {index} wurde verändert oder umsortiert.");
        }
    }

    private static Id3FrameSnapshot[] GetForeignFrames(
        IReadOnlyList<Id3FrameSnapshot> frames)
    {
        return frames
            .Where(frame => frame.OwnedField is null)
            .Select(frame => frame with { RawBytes = frame.RawBytes.ToArray() })
            .ToArray();
    }

    private static OwnedValues GetOwnedValues(
        IReadOnlyList<Id3FrameSnapshot> frames)
    {
        return new OwnedValues(
            frames
                .Where(frame => frame.OwnedField == "Track")
                .Select(frame => frame.OwnedValue ?? string.Empty)
                .ToArray(),
            frames
                .Where(frame => frame.OwnedField == "Album")
                .Select(frame => frame.OwnedValue ?? string.Empty)
                .ToArray());
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

    private static int ReadSynchsafe(
        ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 4)
            throw new InvalidDataException("Ungültige synchsafe ID3-Größe.");

        for (var index = 0; index < bytes.Length; index++)
        {
            if ((bytes[index] & 0x80) != 0)
                throw new InvalidDataException("Ungültige synchsafe ID3-Größe.");
        }

        return
            (bytes[0] << 21) |
            (bytes[1] << 14) |
            (bytes[2] << 7) |
            bytes[3];
    }

    private static string CalculateSha256(
        string path)
    {
        using var stream =
            File.OpenRead(path);

        return Convert.ToHexString(
            SHA256.HashData(stream));
    }

    private sealed record AiffSnapshot(
        IReadOnlyList<IffChunkSnapshot> Chunks,
        IffChunkSnapshot? Id3Chunk,
        IReadOnlyList<Id3FrameSnapshot> Id3Frames);

    private sealed record IffChunkSnapshot(
        string Id,
        int PayloadLength,
        byte[] RawBytes,
        bool IsId3);

    private sealed record Id3FrameSnapshot(
        string Id,
        byte[] RawBytes,
        string? OwnedField,
        string? OwnedValue);

    private sealed record OwnedTxxx(
        string Field,
        string Value);

    private sealed record OwnedValues(
        IReadOnlyList<string> Track,
        IReadOnlyList<string> Album);
}
