using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using DRAnalyzer.Core.Metadata;
using DRAnalyzer.Core.Tagging;

namespace DRAnalyzer.Tests;

[Trait("Category", "ExternalReference")]
public sealed class ManualRealMp3PreservationTests
{
    [Fact]
    public void InvincibleReferenceCopy_WriteAndRemove_PreservesRealMp3()
    {
        var albumDirectory =
            Environment.GetEnvironmentVariable(
                "DRANALYZER_REFERENCE_INVINCIBLE_MP3_DIR");

        Assert.False(
            string.IsNullOrWhiteSpace(albumDirectory),
            "DRANALYZER_REFERENCE_INVINCIBLE_MP3_DIR ist nicht gesetzt.");

        Assert.True(
            Directory.Exists(albumDirectory),
            $"MP3-Referenzordner fehlt: {albumDirectory}");

        var originalPath =
            Directory
                .EnumerateFiles(
                    albumDirectory!,
                    "*.mp3",
                    SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

        Assert.False(
            string.IsNullOrWhiteSpace(originalPath),
            "Im Invincible-Referenzordner wurde keine MP3-Datei gefunden.");

        var originalHashBefore =
            CalculateSha256(originalPath!);

        var beforeTag =
            ReadTagSnapshot(originalPath!);

        Assert.True(
            beforeTag.Version is 3 or 4,
            $"Die gewählte Realfile-MP3 verwendet ID3v2.{beforeTag.Version?.ToString() ?? "<none>"}; für diesen Test werden ID3v2.3 oder ID3v2.4 erwartet.");

        var beforeMetadata =
            AudioMetadataReader.Read(originalPath!);

        var tempDirectory =
            Path.Combine(
                Path.GetTempPath(),
                "DRAnalyzer-Mp3Preservation-" +
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

            Mp3DynamicRangeTagWriter.Write(
                copyPath,
                trackDynamicRange: 20,
                albumDynamicRange: 21);

            var afterWriteTag =
                ReadTagSnapshot(copyPath);

            var afterWriteMetadata =
                AudioMetadataReader.Read(copyPath);

            Assert.Equal(
                beforeTag.Version,
                afterWriteTag.Version);

            AssertForeignFramesEqual(
                beforeTag,
                afterWriteTag);

            AssertPayloadEqual(
                originalPath!,
                beforeTag.PayloadOffset,
                copyPath,
                afterWriteTag.PayloadOffset);

            AssertForeignMetadataEqual(
                beforeMetadata.Tags,
                afterWriteMetadata.Tags);

            Assert.Equal(
                "20",
                afterWriteMetadata.DynamicRange);

            Assert.Equal(
                "21",
                afterWriteMetadata.AlbumDynamicRange);

            Assert.Equal(
                1,
                CountOwnedFrames(
                    afterWriteTag,
                    "DYNAMIC RANGE"));

            Assert.Equal(
                1,
                CountOwnedFrames(
                    afterWriteTag,
                    "ALBUM DYNAMIC RANGE"));

            Mp3DynamicRangeTagWriter.Remove(copyPath);

            var afterRemoveTag =
                ReadTagSnapshot(copyPath);

            var afterRemoveMetadata =
                AudioMetadataReader.Read(copyPath);

            Assert.Equal(
                beforeTag.Version,
                afterRemoveTag.Version);

            AssertForeignFramesEqual(
                beforeTag,
                afterRemoveTag);

            AssertPayloadEqual(
                originalPath!,
                beforeTag.PayloadOffset,
                copyPath,
                afterRemoveTag.PayloadOffset);

            AssertForeignMetadataEqual(
                beforeMetadata.Tags,
                afterRemoveMetadata.Tags);

            Assert.Equal(
                0,
                CountOwnedFrames(
                    afterRemoveTag,
                    "DYNAMIC RANGE"));

            Assert.Equal(
                0,
                CountOwnedFrames(
                    afterRemoveTag,
                    "ALBUM DYNAMIC RANGE"));

            Assert.True(
                string.IsNullOrWhiteSpace(
                    afterRemoveMetadata.DynamicRange));

            Assert.True(
                string.IsNullOrWhiteSpace(
                    afterRemoveMetadata.AlbumDynamicRange));

            var originalHashAfter =
                CalculateSha256(originalPath!);

            Assert.Equal(
                originalHashBefore,
                originalHashAfter);

            Console.WriteLine(
                $"MP3 realfile: {Path.GetFileName(originalPath)}");

            Console.WriteLine(
                $"ID3v2 version: 2.{beforeTag.Version}");

            Console.WriteLine(
                $"Foreign frames preserved: {GetForeignFrames(beforeTag).Length}");

            Console.WriteLine(
                "Payload after ID3v2: byte-identical");

            Console.WriteLine(
                "Write DR: 20 / Album DR: 21");

            Console.WriteLine(
                "Remove: owned DR frames removed");

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

    private static void AssertForeignFramesEqual(
        TagSnapshot before,
        TagSnapshot after)
    {
        var beforeForeign =
            GetForeignFrames(before);

        var afterForeign =
            GetForeignFrames(after);

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
                $"Fremder ID3v2-Frame {index} wurde verändert oder umsortiert.");
        }
    }

    private static byte[][] GetForeignFrames(
        TagSnapshot tag)
    {
        return tag.Frames
            .Where(
                frame =>
                    !IsOwnedFrame(
                        frame,
                        tag.Version!.Value,
                        "DYNAMIC RANGE") &&
                    !IsOwnedFrame(
                        frame,
                        tag.Version!.Value,
                        "ALBUM DYNAMIC RANGE"))
            .Select(frame => frame.RawBytes)
            .ToArray();
    }

    private static int CountOwnedFrames(
        TagSnapshot tag,
        string description)
    {
        return tag.Frames.Count(
            frame =>
                IsOwnedFrame(
                    frame,
                    tag.Version!.Value,
                    description));
    }

    private static bool IsOwnedFrame(
        FrameSnapshot frame,
        int version,
        string description)
    {
        if (!string.Equals(
                frame.Id,
                "TXXX",
                StringComparison.Ordinal))
        {
            return false;
        }

        var parsed =
            ParseTxxx(
                frame.RawBytes.AsSpan(10),
                version);

        return string.Equals(
            parsed.Description,
            description,
            StringComparison.OrdinalIgnoreCase);
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
        return string.Equals(
                   key,
                   "DYNAMIC RANGE",
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   key,
                   "ALBUM DYNAMIC RANGE",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertPayloadEqual(
        string beforePath,
        long beforeOffset,
        string afterPath,
        long afterOffset)
    {
        using var before = new FileStream(
            beforePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        using var after = new FileStream(
            afterPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        Assert.Equal(
            before.Length - beforeOffset,
            after.Length - afterOffset);

        before.Position = beforeOffset;
        after.Position = afterOffset;

        var beforeBuffer = new byte[64 * 1024];
        var afterBuffer = new byte[64 * 1024];

        while (true)
        {
            var beforeRead = before.Read(beforeBuffer);
            var afterRead = after.Read(afterBuffer);

            Assert.Equal(
                beforeRead,
                afterRead);

            if (beforeRead == 0)
                break;

            Assert.True(
                beforeBuffer
                    .AsSpan(0, beforeRead)
                    .SequenceEqual(
                        afterBuffer.AsSpan(0, afterRead)),
                "MPEG-Audiodaten oder Trailer hinter ID3v2 wurden verändert.");
        }
    }

    private static TagSnapshot ReadTagSnapshot(
        string filePath)
    {
        var file = File.ReadAllBytes(filePath);

        if (file.Length < 10 ||
            file[0] != (byte)'I' ||
            file[1] != (byte)'D' ||
            file[2] != (byte)'3')
        {
            return new TagSnapshot(
                Version: null,
                PayloadOffset: 0,
                Frames: Array.Empty<FrameSnapshot>());
        }

        var version = file[3];
        var revision = file[4];
        var flags = file[5];

        Assert.True(
            version is 3 or 4,
            $"Realfile-Testparser unterstützt nur ID3v2.3/v2.4, gefunden wurde v2.{version}.");

        Assert.Equal(
            0,
            revision);

        Assert.Equal(
            0,
            flags);

        var bodyLength =
            ReadSynchsafe(file.AsSpan(6, 4));

        var payloadOffset =
            10 + bodyLength;

        Assert.InRange(
            payloadOffset,
            10,
            file.Length);

        var frames =
            new List<FrameSnapshot>();

        var offset = 10;

        while (offset < payloadOffset)
        {
            if (file[offset] == 0)
                break;

            Assert.True(
                offset + 10 <= payloadOffset,
                "Abgeschnittener ID3v2-Frame-Header im Realfile.");

            var id =
                Encoding.ASCII.GetString(
                    file,
                    offset,
                    4);

            var payloadLength =
                version == 3
                    ? BinaryPrimitives.ReadInt32BigEndian(
                        file.AsSpan(
                            offset + 4,
                            4))
                    : ReadSynchsafe(
                        file.AsSpan(
                            offset + 4,
                            4));

            Assert.True(
                payloadLength > 0,
                $"Ungültige Frame-Größe bei '{id}'.");

            var totalLength =
                10 + payloadLength;

            Assert.True(
                offset + totalLength <= payloadOffset,
                $"ID3v2-Frame '{id}' reicht über den Tag hinaus.");

            frames.Add(
                new FrameSnapshot(
                    id,
                    file.AsSpan(
                            offset,
                            totalLength)
                        .ToArray()));

            offset += totalLength;
        }

        for (var index = offset;
             index < payloadOffset;
             index++)
        {
            Assert.Equal(
                0,
                file[index]);
        }

        return new TagSnapshot(
            version,
            payloadOffset,
            frames);
    }

    private static ParsedTxxx ParseTxxx(
        ReadOnlySpan<byte> payload,
        int version)
    {
        Assert.True(
            payload.Length >= 2,
            "TXXX-Frame ist zu kurz.");

        var encodingByte = payload[0];
        var content = payload[1..];

        return encodingByte switch
        {
            0 => ParseSingleByteTxxx(
                content,
                Encoding.Latin1),

            3 when version == 4 => ParseSingleByteTxxx(
                content,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: true)),

            1 => ParseUtf16Txxx(
                content,
                bigEndianWithoutBom: false),

            2 when version == 4 => ParseUtf16Txxx(
                content,
                bigEndianWithoutBom: true),

            _ => throw new InvalidDataException(
                $"Nicht unterstützte TXXX-Textkodierung {encodingByte} im Realfile-Testparser.")
        };
    }

    private static ParsedTxxx ParseSingleByteTxxx(
        ReadOnlySpan<byte> content,
        Encoding encoding)
    {
        var separator =
            content.IndexOf((byte)0);

        Assert.True(
            separator >= 0,
            "TXXX-Beschreibung besitzt keinen Null-Separator.");

        return new ParsedTxxx(
            encoding.GetString(
                content[..separator]),
            encoding.GetString(
                content[(separator + 1)..])
                .TrimEnd('\0'));
    }

    private static ParsedTxxx ParseUtf16Txxx(
        ReadOnlySpan<byte> content,
        bool bigEndianWithoutBom)
    {
        Assert.True(
            content.Length >= 2,
            "UTF-16-TXXX ist zu kurz.");

        var separator = -1;

        for (var index = 0;
             index + 1 < content.Length;
             index += 2)
        {
            if (content[index] == 0 &&
                content[index + 1] == 0)
            {
                separator = index;
                break;
            }
        }

        Assert.True(
            separator >= 0,
            "UTF-16-TXXX-Beschreibung besitzt keinen Null-Separator.");

        var descriptionBytes =
            content[..separator];

        var valueBytes =
            content[(separator + 2)..];

        var encoding = bigEndianWithoutBom
            ? Encoding.BigEndianUnicode
            : GetUtf16EncodingFromBom(descriptionBytes);

        var descriptionPayload =
            StripUtf16Bom(descriptionBytes);

        var valuePayload =
            StripUtf16Bom(valueBytes);

        return new ParsedTxxx(
            encoding.GetString(descriptionPayload),
            encoding.GetString(valuePayload)
                .TrimEnd('\0'));
    }

    private static Encoding GetUtf16EncodingFromBom(
        ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 2 &&
            bytes[0] == 0xFE &&
            bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode;
        }

        return Encoding.Unicode;
    }

    private static ReadOnlySpan<byte> StripUtf16Bom(
        ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 2 &&
            ((bytes[0] == 0xFF && bytes[1] == 0xFE) ||
             (bytes[0] == 0xFE && bytes[1] == 0xFF)))
        {
            return bytes[2..];
        }

        return bytes;
    }

    private static int ReadSynchsafe(
        ReadOnlySpan<byte> bytes)
    {
        Assert.Equal(
            4,
            bytes.Length);

        foreach (var value in bytes)
        {
            Assert.True(
                (value & 0x80) == 0,
                "Ungültige synchsafe ID3-Größe.");
        }

        return
            (bytes[0] << 21) |
            (bytes[1] << 14) |
            (bytes[2] << 7) |
            bytes[3];
    }

    private static string CalculateSha256(
        string filePath)
    {
        using var stream = File.OpenRead(filePath);

        return Convert.ToHexString(
            SHA256.HashData(stream));
    }

    private sealed record TagSnapshot(
        int? Version,
        int PayloadOffset,
        IReadOnlyList<FrameSnapshot> Frames);

    private sealed record FrameSnapshot(
        string Id,
        byte[] RawBytes);

    private sealed record ParsedTxxx(
        string Description,
        string Value);
}
