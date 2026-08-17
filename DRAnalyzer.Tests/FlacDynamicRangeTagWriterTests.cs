using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using DRAnalyzer.Core.Tagging;

namespace DRAnalyzer.Tests;

public sealed class FlacDynamicRangeTagWriterTests
{
    private const byte VorbisCommentBlockType = 4;
    private const byte PictureBlockType = 6;

    [Fact]
    public void Write_ChangesOnlyOwnedDrTags()
    {
        var tempDir =
            Path.Combine(
                Path.GetTempPath(),
                $"dranalyzer-tagwriter-{Guid.NewGuid():N}");

        Directory.CreateDirectory(tempDir);

        var coverPath =
            Path.Combine(tempDir, "cover.png");

        var audioPath =
            Path.Combine(tempDir, "metadata-reference.flac");

        try
        {
            CreateCover(coverPath);
            CreateReferenceFlac(audioPath, coverPath);

            var before =
                ReadFlacSnapshot(audioPath);

            FlacDynamicRangeTagWriter.Write(
                audioPath,
                trackDynamicRange: 12,
                albumDynamicRange: 13);

            var after =
                ReadFlacSnapshot(audioPath);

            Assert.Equal(
                before.AudioHash,
                after.AudioHash);

            Assert.Equal(
                before.Blocks.Count,
                after.Blocks.Count);

            for (var index = 0;
                 index < before.Blocks.Count;
                 index++)
            {
                var beforeBlock =
                    before.Blocks[index];

                var afterBlock =
                    after.Blocks[index];

                Assert.Equal(
                    beforeBlock.Type,
                    afterBlock.Type);

                if (beforeBlock.Type ==
                    VorbisCommentBlockType)
                {
                    continue;
                }

                Assert.True(
                    beforeBlock.Data.SequenceEqual(
                        afterBlock.Data),
                    $"FLAC-Metadatenblock {index} " +
                    $"vom Typ {beforeBlock.Type} " +
                    "wurde verändert.");
            }

            var beforePicture =
                before.Blocks.Single(
                    block =>
                        block.Type ==
                        PictureBlockType);

            var afterPicture =
                after.Blocks.Single(
                    block =>
                        block.Type ==
                        PictureBlockType);

            Assert.True(
                beforePicture.Data.SequenceEqual(
                    afterPicture.Data),
                "Das eingebettete Cover wurde verändert.");

            var beforeComments =
                ParseVorbisComment(
                    before.Blocks.Single(
                        block =>
                            block.Type ==
                            VorbisCommentBlockType).Data);

            var afterComments =
                ParseVorbisComment(
                    after.Blocks.Single(
                        block =>
                            block.Type ==
                            VorbisCommentBlockType).Data);

            Assert.True(
                beforeComments.Vendor.SequenceEqual(
                    afterComments.Vendor),
                "Der Vorbis-Vendor-String wurde verändert.");

            var beforeForeign =
                beforeComments.Comments
                    .Where(
                        comment =>
                            !IsOwnedDrField(comment))
                    .ToArray();

            var afterForeign =
                afterComments.Comments
                    .Where(
                        comment =>
                            !IsOwnedDrField(comment))
                    .ToArray();

            Assert.Equal(
                beforeForeign.Length,
                afterForeign.Length);

            for (var index = 0;
                 index < beforeForeign.Length;
                 index++)
            {
                Assert.True(
                    beforeForeign[index].SequenceEqual(
                        afterForeign[index]),
                    $"Fremder Vorbis-Comment {index} " +
                    "wurde verändert.");
            }

            Assert.Single(
                afterComments.Comments,
                comment =>
                    IsField(
                        comment,
                        "DYNAMIC RANGE"));

            Assert.Single(
                afterComments.Comments,
                comment =>
                    IsField(
                        comment,
                        "ALBUM DYNAMIC RANGE"));

            Assert.Equal(
                "12",
                GetFieldValue(
                    afterComments.Comments.Single(
                        comment =>
                            IsField(
                                comment,
                                "DYNAMIC RANGE"))));

            Assert.Equal(
                "13",
                GetFieldValue(
                    afterComments.Comments.Single(
                        comment =>
                            IsField(
                                comment,
                                "ALBUM DYNAMIC RANGE"))));

            ValidateWithFfprobe(audioPath);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(
                    tempDir,
                    recursive: true);
            }
        }
    }

    [Fact]
    public void Remove_RemovesOnlyOwnedDrTags()
    {
        var tempDir =
            Path.Combine(
                Path.GetTempPath(),
                $"dranalyzer-tagremover-{Guid.NewGuid():N}");

        Directory.CreateDirectory(tempDir);

        var coverPath =
            Path.Combine(tempDir, "cover.png");

        var audioPath =
            Path.Combine(tempDir, "metadata-reference.flac");

        try
        {
            CreateCover(coverPath);
            CreateReferenceFlac(audioPath, coverPath);

            var before =
                ReadFlacSnapshot(audioPath);

            FlacDynamicRangeTagWriter.Remove(
                audioPath);

            var after =
                ReadFlacSnapshot(audioPath);

            Assert.Equal(
                before.AudioHash,
                after.AudioHash);

            Assert.Equal(
                before.Blocks.Count,
                after.Blocks.Count);

            for (var index = 0;
                 index < before.Blocks.Count;
                 index++)
            {
                var beforeBlock =
                    before.Blocks[index];

                var afterBlock =
                    after.Blocks[index];

                Assert.Equal(
                    beforeBlock.Type,
                    afterBlock.Type);

                if (beforeBlock.Type ==
                    VorbisCommentBlockType)
                {
                    continue;
                }

                Assert.True(
                    beforeBlock.Data.SequenceEqual(
                        afterBlock.Data),
                    $"FLAC-Metadatenblock {index} " +
                    $"vom Typ {beforeBlock.Type} " +
                    "wurde verändert.");
            }

            var beforePicture =
                before.Blocks.Single(
                    block =>
                        block.Type ==
                        PictureBlockType);

            var afterPicture =
                after.Blocks.Single(
                    block =>
                        block.Type ==
                        PictureBlockType);

            Assert.True(
                beforePicture.Data.SequenceEqual(
                    afterPicture.Data),
                "Das eingebettete Cover wurde verändert.");

            var beforeComments =
                ParseVorbisComment(
                    before.Blocks.Single(
                        block =>
                            block.Type ==
                            VorbisCommentBlockType).Data);

            var afterComments =
                ParseVorbisComment(
                    after.Blocks.Single(
                        block =>
                            block.Type ==
                            VorbisCommentBlockType).Data);

            Assert.True(
                beforeComments.Vendor.SequenceEqual(
                    afterComments.Vendor),
                "Der Vorbis-Vendor-String wurde verändert.");

            var beforeForeign =
                beforeComments.Comments
                    .Where(
                        comment =>
                            !IsOwnedDrField(comment))
                    .ToArray();

            var afterForeign =
                afterComments.Comments
                    .Where(
                        comment =>
                            !IsOwnedDrField(comment))
                    .ToArray();

            Assert.Equal(
                beforeForeign.Length,
                afterForeign.Length);

            for (var index = 0;
                 index < beforeForeign.Length;
                 index++)
            {
                Assert.True(
                    beforeForeign[index].SequenceEqual(
                        afterForeign[index]),
                    $"Fremder Vorbis-Comment {index} " +
                    "wurde verändert.");
            }

            Assert.DoesNotContain(
                afterComments.Comments,
                IsOwnedDrField);

            ValidateWithFfprobe(
                audioPath,
                expectOwnedDrTags: false);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(
                    tempDir,
                    recursive: true);
            }
        }
    }

    private static void CreateCover(
        string outputPath)
    {
        RunFfmpeg(
            "-f", "lavfi",
            "-i", "testsrc=size=96x96:rate=1",
            "-frames:v", "1",
            "-y",
            outputPath);
    }

    private static void CreateReferenceFlac(
        string outputPath,
        string coverPath)
    {
        RunFfmpeg(
            "-f", "lavfi",
            "-i", "sine=frequency=997:sample_rate=48000",

            "-i", coverPath,

            "-t", "1",

            "-map", "0:a:0",
            "-map", "1:v:0",

            "-c:a", "flac",
            "-c:v", "png",

            "-disposition:v:0", "attached_pic",

            "-metadata",
            "artist=Björk – Official髭男dism – Москва",

            "-metadata",
            "album=Café – 日本語 – العربية",

            "-metadata",
            "album_artist=Various Ärtists",

            "-metadata",
            "title=Straße – ミックスナッツ – 한국어",

            "-metadata",
            "track=01/12",

            "-metadata",
            "disc=2/3",

            "-metadata",
            "date=2026",

            "-metadata",
            "genre=Électronique – 音楽",

            "-metadata",
            "composer=François – 山田太郎",

            "-metadata",
            "comment=DRAnalyzer metadata safety test",

            "-metadata",
            "replaygain_track_gain=-5.25 dB",

            "-metadata",
            "replaygain_album_gain=-4.75 dB",

            "-metadata",
            "replaygain_track_peak=0.987654",

            "-metadata",
            "custom_test=DO NOT TOUCH – 日本語 – 🎵",

            "-metadata",
            "dynamic range=7",

            "-metadata",
            "album dynamic range=8",

            "-y",
            outputPath);
    }

    private static FlacSnapshot ReadFlacSnapshot(
        string filePath)
    {
        using var stream =
            new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

        Span<byte> marker =
            stackalloc byte[4];

        stream.ReadExactly(marker);

        Assert.True(
            marker.SequenceEqual(
                Encoding.ASCII.GetBytes("fLaC")));

        var blocks =
            new List<MetadataBlock>();

        var isLast = false;

        Span<byte> header =
            stackalloc byte[4];

        while (!isLast)
        {
            stream.ReadExactly(header);

            isLast =
                (header[0] & 0x80) != 0;

            var type =
                (byte)(header[0] & 0x7F);

            var length =
                (header[1] << 16) |
                (header[2] << 8) |
                header[3];

            var data =
                new byte[length];

            stream.ReadExactly(data);

            blocks.Add(
                new MetadataBlock(
                    type,
                    data));
        }

        using var sha256 =
            SHA256.Create();

        var audioHash =
            Convert.ToHexString(
                sha256.ComputeHash(stream));

        return new FlacSnapshot(
            blocks,
            audioHash);
    }

    private static ParsedVorbisComment
        ParseVorbisComment(
            byte[] data)
    {
        var offset = 0;

        var vendorLength =
            ReadUInt32(
                data,
                ref offset);

        var vendor =
            ReadBytes(
                data,
                ref offset,
                vendorLength);

        var commentCount =
            ReadUInt32(
                data,
                ref offset);

        var comments =
            new List<byte[]>();

        for (uint index = 0;
             index < commentCount;
             index++)
        {
            var length =
                ReadUInt32(
                    data,
                    ref offset);

            comments.Add(
                ReadBytes(
                    data,
                    ref offset,
                    length));
        }

        Assert.Equal(
            data.Length,
            offset);

        return new ParsedVorbisComment(
            vendor,
            comments);
    }

    private static bool IsOwnedDrField(
        byte[] comment)
    {
        return
            IsField(
                comment,
                "DYNAMIC RANGE") ||
            IsField(
                comment,
                "ALBUM DYNAMIC RANGE");
    }

    private static bool IsField(
        byte[] comment,
        string fieldName)
    {
        var equalsIndex =
            Array.IndexOf(
                comment,
                (byte)'=');

        if (equalsIndex <= 0)
            return false;

        var currentName =
            Encoding.ASCII.GetString(
                comment,
                0,
                equalsIndex);

        return string.Equals(
            currentName,
            fieldName,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string GetFieldValue(
        byte[] comment)
    {
        var equalsIndex =
            Array.IndexOf(
                comment,
                (byte)'=');

        Assert.True(
            equalsIndex >= 0);

        return Encoding.UTF8.GetString(
            comment,
            equalsIndex + 1,
            comment.Length - equalsIndex - 1);
    }

    private static uint ReadUInt32(
        byte[] data,
        ref int offset)
    {
        Assert.True(
            offset <= data.Length - 4);

        var value =
            BinaryPrimitives.ReadUInt32LittleEndian(
                data.AsSpan(
                    offset,
                    4));

        offset += 4;

        return value;
    }

    private static byte[] ReadBytes(
        byte[] data,
        ref int offset,
        uint length)
    {
        Assert.True(
            length <= int.MaxValue);

        var intLength =
            (int)length;

        Assert.True(
            offset <=
            data.Length - intLength);

        var result =
            data.AsSpan(
                    offset,
                    intLength)
                .ToArray();

        offset += intLength;

        return result;
    }

    private static void ValidateWithFfprobe(
        string filePath,
        bool expectOwnedDrTags = true)
    {
        var startInfo =
            CreateProcessInfo("ffprobe");

        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("error");

        startInfo.ArgumentList.Add("-show_streams");
        startInfo.ArgumentList.Add("-show_format");

        startInfo.ArgumentList.Add("-of");
        startInfo.ArgumentList.Add("json");

        startInfo.ArgumentList.Add(filePath);

        using var process =
            Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "ffprobe konnte nicht gestartet werden.");

        var output =
            process.StandardOutput.ReadToEnd();

        var error =
            process.StandardError.ReadToEnd();

        process.WaitForExit();

        Assert.True(
            process.ExitCode == 0,
            $"ffprobe konnte die geschriebene FLAC " +
            $"nicht lesen: {error}");

        if (expectOwnedDrTags)
        {
            Assert.Contains(
                "DYNAMIC RANGE",
                output,
                StringComparison.OrdinalIgnoreCase);

            Assert.Contains(
                "ALBUM DYNAMIC RANGE",
                output,
                StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.DoesNotContain(
                "DYNAMIC RANGE",
                output,
                StringComparison.OrdinalIgnoreCase);

            Assert.DoesNotContain(
                "ALBUM DYNAMIC RANGE",
                output,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void RunFfmpeg(
        params string[] arguments)
    {
        var startInfo =
            CreateProcessInfo("ffmpeg");

        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-loglevel");
        startInfo.ArgumentList.Add("error");

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

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

    private static ProcessStartInfo
        CreateProcessInfo(
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

    private sealed record MetadataBlock(
        byte Type,
        byte[] Data);

    private sealed record FlacSnapshot(
        IReadOnlyList<MetadataBlock> Blocks,
        string AudioHash);

    private sealed record ParsedVorbisComment(
        byte[] Vendor,
        IReadOnlyList<byte[]> Comments);
}

