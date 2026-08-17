using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using DRAnalyzer.Core.Tagging;

namespace DRAnalyzer.Tests;

public sealed class FlacDynamicRangeTagWriterEdgeCaseTests
{
    [Fact]
    public void Write_AddsOwnedTags_WhenTheyDoNotExist()
    {
        var tempDir = CreateTempDirectory();
        var filePath = Path.Combine(tempDir, "missing-dr.flac");

        try
        {
            CreateFlac(
                filePath,
                ("artist", "Björk – 東京"),
                ("custom_test", "DO NOT TOUCH"),
                ("replaygain_track_gain", "-5.25 dB"));

            FlacDynamicRangeTagWriter.Write(
                filePath,
                trackDynamicRange: 12,
                albumDynamicRange: 13);

            var comments = ReadComments(filePath);

            Assert.Single(
                comments,
                value => IsField(value, "DYNAMIC RANGE"));

            Assert.Single(
                comments,
                value => IsField(value, "ALBUM DYNAMIC RANGE"));

            Assert.Equal(
                "12",
                GetSingleFieldValue(
                    comments,
                    "DYNAMIC RANGE"));

            Assert.Equal(
                "13",
                GetSingleFieldValue(
                    comments,
                    "ALBUM DYNAMIC RANGE"));

            Assert.Contains(
                comments,
                value =>
                    value ==
                    "custom_test=DO NOT TOUCH");

            Assert.Contains(
                comments,
                value =>
                    value ==
                    "replaygain_track_gain=-5.25 dB");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Write_WithNullAlbumDr_PreservesExistingAlbumDr()
    {
        var tempDir = CreateTempDirectory();
        var filePath = Path.Combine(tempDir, "track-only.flac");

        try
        {
            CreateFlac(
                filePath,
                ("artist", "Test Artist"),
                ("dynamic range", "7"),
                ("album dynamic range", "8"),
                ("custom_test", "DO NOT TOUCH"));

            FlacDynamicRangeTagWriter.Write(
                filePath,
                trackDynamicRange: 12,
                albumDynamicRange: null);

            var comments = ReadComments(filePath);

            Assert.Equal(
                "12",
                GetSingleFieldValue(
                    comments,
                    "DYNAMIC RANGE"));

            Assert.Equal(
                "8",
                GetSingleFieldValue(
                    comments,
                    "ALBUM DYNAMIC RANGE"));

            Assert.Contains(
                comments,
                value =>
                    value ==
                    "custom_test=DO NOT TOUCH");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Write_ReplacesDuplicateOwnedTags_WithSingleCanonicalValues()
    {
        var tempDir = CreateTempDirectory();
        var filePath = Path.Combine(tempDir, "duplicates.flac");

        try
        {
            CreateFlac(
                filePath,
                ("artist", "Test Artist"),
                ("dynamic range", "7"),
                ("album dynamic range", "8"),
                ("custom_test", "DO NOT TOUCH"));

            AppendVorbisComment(
                filePath,
                "DYNAMIC RANGE=9");

            AppendVorbisComment(
                filePath,
                "ALBUM DYNAMIC RANGE=10");

            var before = ReadComments(filePath);

            Assert.Equal(
                2,
                before.Count(
                    value =>
                        IsField(
                            value,
                            "DYNAMIC RANGE")));

            Assert.Equal(
                2,
                before.Count(
                    value =>
                        IsField(
                            value,
                            "ALBUM DYNAMIC RANGE")));

            FlacDynamicRangeTagWriter.Write(
                filePath,
                trackDynamicRange: 12,
                albumDynamicRange: 13);

            var after = ReadComments(filePath);

            Assert.Single(
                after,
                value =>
                    IsField(
                        value,
                        "DYNAMIC RANGE"));

            Assert.Single(
                after,
                value =>
                    IsField(
                        value,
                        "ALBUM DYNAMIC RANGE"));

            Assert.Equal(
                "12",
                GetSingleFieldValue(
                    after,
                    "DYNAMIC RANGE"));

            Assert.Equal(
                "13",
                GetSingleFieldValue(
                    after,
                    "ALBUM DYNAMIC RANGE"));

            Assert.Contains(
                after,
                value =>
                    value ==
                    "custom_test=DO NOT TOUCH");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Remove_RemovesDuplicateOwnedTags_AndPreservesForeignTags()
    {
        var tempDir = CreateTempDirectory();
        var filePath = Path.Combine(tempDir, "remove-duplicates.flac");

        try
        {
            CreateFlac(
                filePath,
                ("artist", "Test Artist"),
                ("dynamic range", "7"),
                ("album dynamic range", "8"),
                ("custom_test", "DO NOT TOUCH"));

            AppendVorbisComment(
                filePath,
                "DYNAMIC RANGE=9");

            AppendVorbisComment(
                filePath,
                "ALBUM DYNAMIC RANGE=10");

            FlacDynamicRangeTagWriter.Remove(
                filePath);

            var comments =
                ReadComments(
                    filePath);

            Assert.DoesNotContain(
                comments,
                value =>
                    IsField(
                        value,
                        "DYNAMIC RANGE"));

            Assert.DoesNotContain(
                comments,
                value =>
                    IsField(
                        value,
                        "ALBUM DYNAMIC RANGE"));

            Assert.Contains(
                comments,
                value =>
                    value ==
                    "custom_test=DO NOT TOUCH");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Remove_WhenNoOwnedTagsExist_LeavesFileByteExact()
    {
        var tempDir = CreateTempDirectory();
        var filePath = Path.Combine(tempDir, "remove-noop.flac");

        try
        {
            CreateFlac(
                filePath,
                ("artist", "Björk – 東京"),
                ("custom_test", "DO NOT TOUCH"),
                ("replaygain_track_gain", "-5.25 dB"));

            var before =
                SHA256.HashData(
                    File.ReadAllBytes(filePath));

            FlacDynamicRangeTagWriter.Remove(
                filePath);

            var after =
                SHA256.HashData(
                    File.ReadAllBytes(filePath));

            Assert.True(
                before.SequenceEqual(after),
                "Eine FLAC-Datei ohne DR-Tags wurde verändert.");

            Assert.Empty(
                Directory.GetFiles(
                    tempDir,
                    "*.dranalyzer.*"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Write_InvalidFlac_DoesNotModifyOriginalFile()
    {
        var tempDir = CreateTempDirectory();
        var filePath = Path.Combine(tempDir, "broken.flac");

        try
        {
            File.WriteAllBytes(
                filePath,
                Encoding.UTF8.GetBytes(
                    "THIS IS NOT A FLAC FILE – 日本語"));

            var before =
                SHA256.HashData(
                    File.ReadAllBytes(filePath));

            Assert.Throws<InvalidDataException>(
                () =>
                    FlacDynamicRangeTagWriter.Write(
                        filePath,
                        trackDynamicRange: 12,
                        albumDynamicRange: 13));

            var after =
                SHA256.HashData(
                    File.ReadAllBytes(filePath));

            Assert.True(
                before.SequenceEqual(after),
                "Die ungültige Originaldatei wurde verändert.");

            var leftovers =
                Directory.GetFiles(
                    tempDir,
                    "*.dranalyzer.*");

            Assert.Empty(leftovers);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Remove_InvalidFlac_DoesNotModifyOriginalFile()
    {
        var tempDir = CreateTempDirectory();
        var filePath = Path.Combine(tempDir, "broken-remove.flac");

        try
        {
            File.WriteAllBytes(
                filePath,
                Encoding.UTF8.GetBytes(
                    "THIS IS NOT A FLAC FILE – 日本語"));

            var before =
                SHA256.HashData(
                    File.ReadAllBytes(filePath));

            Assert.Throws<InvalidDataException>(
                () =>
                    FlacDynamicRangeTagWriter.Remove(
                        filePath));

            var after =
                SHA256.HashData(
                    File.ReadAllBytes(filePath));

            Assert.True(
                before.SequenceEqual(after),
                "Die ungültige Originaldatei wurde verändert.");

            Assert.Empty(
                Directory.GetFiles(
                    tempDir,
                    "*.dranalyzer.*"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path =
            Path.Combine(
                Path.GetTempPath(),
                $"dranalyzer-edge-{Guid.NewGuid():N}");

        Directory.CreateDirectory(path);

        return path;
    }

    private static void CreateFlac(
        string outputPath,
        params (string Name, string Value)[] metadata)
    {
        var startInfo =
            CreateProcessInfo("ffmpeg");

        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-loglevel");
        startInfo.ArgumentList.Add("error");

        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("lavfi");

        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(
            "anullsrc=r=48000:cl=stereo");

        startInfo.ArgumentList.Add("-t");
        startInfo.ArgumentList.Add("0.25");

        startInfo.ArgumentList.Add("-c:a");
        startInfo.ArgumentList.Add("flac");

        foreach (var item in metadata)
        {
            startInfo.ArgumentList.Add("-metadata");
            startInfo.ArgumentList.Add(
                $"{item.Name}={item.Value}");
        }

        startInfo.ArgumentList.Add("-y");
        startInfo.ArgumentList.Add(outputPath);

        using var process =
            Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "FFmpeg konnte nicht gestartet werden.");

        var error =
            process.StandardError.ReadToEnd();

        process.WaitForExit();

        Assert.True(
            process.ExitCode == 0,
            $"FFmpeg-Fehler: {error}");
    }

    private static List<string> ReadComments(
        string filePath)
    {
        var bytes =
            File.ReadAllBytes(filePath);

        Assert.True(
            bytes.Length >= 4);

        Assert.Equal(
            "fLaC",
            Encoding.ASCII.GetString(
                bytes,
                0,
                4));

        var offset = 4;
        var isLast = false;

        while (!isLast)
        {
            Assert.True(
                offset <= bytes.Length - 4);

            var first = bytes[offset];

            isLast =
                (first & 0x80) != 0;

            var type =
                first & 0x7F;

            var length =
                (bytes[offset + 1] << 16) |
                (bytes[offset + 2] << 8) |
                bytes[offset + 3];

            offset += 4;

            Assert.True(
                offset <=
                bytes.Length - length);

            if (type == 4)
            {
                return ParseComments(
                    bytes.AsSpan(
                            offset,
                            length)
                        .ToArray());
            }

            offset += length;
        }

        throw new InvalidDataException(
            "Kein VORBIS_COMMENT-Block vorhanden.");
    }

    private static List<string> ParseComments(
        byte[] data)
    {
        var offset = 0;

        var vendorLength =
            ReadUInt32(
                data,
                ref offset);

        offset +=
            checked((int)vendorLength);

        Assert.True(
            offset <= data.Length - 4);

        var count =
            ReadUInt32(
                data,
                ref offset);

        var result =
            new List<string>();

        for (uint index = 0;
             index < count;
             index++)
        {
            var length =
                ReadUInt32(
                    data,
                    ref offset);

            var intLength =
                checked((int)length);

            Assert.True(
                offset <=
                data.Length - intLength);

            result.Add(
                Encoding.UTF8.GetString(
                    data,
                    offset,
                    intLength));

            offset += intLength;
        }

        Assert.Equal(
            data.Length,
            offset);

        return result;
    }

    private static void AppendVorbisComment(
        string filePath,
        string newComment)
    {
        var source =
            File.ReadAllBytes(filePath);

        Assert.Equal(
            "fLaC",
            Encoding.ASCII.GetString(
                source,
                0,
                4));

        using var output =
            new MemoryStream();

        output.Write(
            source,
            0,
            4);

        var offset = 4;
        var isLast = false;
        var modified = false;

        while (!isLast)
        {
            var first =
                source[offset];

            isLast =
                (first & 0x80) != 0;

            var type =
                (byte)(first & 0x7F);

            var length =
                (source[offset + 1] << 16) |
                (source[offset + 2] << 8) |
                source[offset + 3];

            offset += 4;

            var data =
                source.AsSpan(
                        offset,
                        length)
                    .ToArray();

            offset += length;

            if (type == 4 && !modified)
            {
                data =
                    AppendCommentToBlock(
                        data,
                        newComment);

                modified = true;
            }

            Assert.True(
                data.Length <= 0xFFFFFF);

            output.WriteByte(
                (byte)(
                    type |
                    (isLast ? 0x80 : 0)));

            output.WriteByte(
                (byte)(data.Length >> 16));

            output.WriteByte(
                (byte)(data.Length >> 8));

            output.WriteByte(
                (byte)data.Length);

            output.Write(data);
        }

        Assert.True(
            modified,
            "Kein VORBIS_COMMENT-Block gefunden.");

        output.Write(
            source,
            offset,
            source.Length - offset);

        File.WriteAllBytes(
            filePath,
            output.ToArray());
    }

    private static byte[] AppendCommentToBlock(
        byte[] data,
        string newComment)
    {
        var offset = 0;

        var vendorLength =
            ReadUInt32(
                data,
                ref offset);

        offset +=
            checked((int)vendorLength);

        Assert.True(
            offset <= data.Length - 4);

        var countOffset =
            offset;

        var oldCount =
            BinaryPrimitives
                .ReadUInt32LittleEndian(
                    data.AsSpan(
                        countOffset,
                        4));

        var encoded =
            Encoding.UTF8.GetBytes(
                newComment);

        using var output =
            new MemoryStream();

        output.Write(
            data,
            0,
            countOffset);

        WriteUInt32(
            output,
            checked(oldCount + 1));

        output.Write(
            data,
            countOffset + 4,
            data.Length -
            (countOffset + 4));

        WriteUInt32(
            output,
            checked((uint)encoded.Length));

        output.Write(encoded);

        return output.ToArray();
    }

    private static string GetSingleFieldValue(
        IReadOnlyList<string> comments,
        string fieldName)
    {
        var value =
            Assert.Single(
                comments,
                comment =>
                    IsField(
                        comment,
                        fieldName));

        var equalsIndex =
            value.IndexOf('=');

        Assert.True(
            equalsIndex >= 0);

        return value[
            (equalsIndex + 1)..];
    }

    private static bool IsField(
        string comment,
        string fieldName)
    {
        var equalsIndex =
            comment.IndexOf('=');

        if (equalsIndex <= 0)
            return false;

        return string.Equals(
            comment[..equalsIndex],
            fieldName,
            StringComparison.OrdinalIgnoreCase);
    }

    private static uint ReadUInt32(
        byte[] data,
        ref int offset)
    {
        Assert.True(
            offset <= data.Length - 4);

        var value =
            BinaryPrimitives
                .ReadUInt32LittleEndian(
                    data.AsSpan(
                        offset,
                        4));

        offset += 4;

        return value;
    }

    private static void WriteUInt32(
        Stream stream,
        uint value)
    {
        Span<byte> buffer =
            stackalloc byte[4];

        BinaryPrimitives
            .WriteUInt32LittleEndian(
                buffer,
                value);

        stream.Write(buffer);
    }

    private static ProcessStartInfo CreateProcessInfo(
        string fileName)
    {
        return new ProcessStartInfo
        {
            FileName = fileName,

            RedirectStandardOutput = true,
            RedirectStandardError = true,

            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,

            UseShellExecute = false,
            CreateNoWindow = true
        };
    }
}

