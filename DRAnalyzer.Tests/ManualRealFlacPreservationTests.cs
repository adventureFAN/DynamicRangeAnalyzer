using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using DRAnalyzer.Core.Tagging;

namespace DRAnalyzer.Tests;

[Trait("Category", "ExternalReference")]
public sealed class ManualRealFlacPreservationTests
{
    private const byte VorbisCommentBlockType = 4;

    [Fact]
    public void ModifiedRealFlac_PreservesEverythingExceptOwnedDrTags()
    {
        var originalPath =
            Environment.GetEnvironmentVariable(
                "DRANALYZER_MANUAL_FLAC_ORIGINAL");

        var modifiedPath =
            Environment.GetEnvironmentVariable(
                "DRANALYZER_MANUAL_FLAC_COPY");

        Assert.False(
            string.IsNullOrWhiteSpace(originalPath),
            "DRANALYZER_MANUAL_FLAC_ORIGINAL ist nicht gesetzt.");

        Assert.False(
            string.IsNullOrWhiteSpace(modifiedPath),
            "DRANALYZER_MANUAL_FLAC_COPY ist nicht gesetzt.");

        Assert.True(
            File.Exists(originalPath),
            $"Original-FLAC fehlt: {originalPath}");

        Assert.True(
            File.Exists(modifiedPath),
            $"FLAC-Testkopie fehlt: {modifiedPath}");

        var requiredOriginalPath = originalPath!;
        var requiredModifiedPath = modifiedPath!;

        var before = ReadSnapshot(requiredOriginalPath);
        var after = ReadSnapshot(requiredModifiedPath);

        // Die eigentlichen FLAC-Audioframes müssen
        // bytegenau identisch geblieben sein.
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
            var beforeBlock = before.Blocks[index];
            var afterBlock = after.Blocks[index];

            Assert.Equal(
                beforeBlock.Type,
                afterBlock.Type);

            // Nur der Vorbis-Comment-Block darf
            // aufgrund unserer DR-Tags abweichen.
            if (beforeBlock.Type ==
                VorbisCommentBlockType)
            {
                continue;
            }

            Assert.True(
                beforeBlock.Data
                    .AsSpan()
                    .SequenceEqual(
                        afterBlock.Data),
                $"FLAC-Metadatenblock {index} " +
                $"vom Typ {beforeBlock.Type} " +
                "wurde verändert.");
        }

        var beforeCommentBlock =
            Assert.Single(
                before.Blocks,
                block =>
                    block.Type ==
                    VorbisCommentBlockType);

        var afterCommentBlock =
            Assert.Single(
                after.Blocks,
                block =>
                    block.Type ==
                    VorbisCommentBlockType);

        var beforeComments =
            ParseVorbisComment(
                beforeCommentBlock.Data);

        var afterComments =
            ParseVorbisComment(
                afterCommentBlock.Data);

        // Auch der Vendor-String muss
        // bytegenau erhalten bleiben.
        Assert.True(
            beforeComments.Vendor
                .AsSpan()
                .SequenceEqual(
                    afterComments.Vendor),
            "Der Vorbis-Vendor wurde verändert.");

        var beforeForeignComments =
            beforeComments.Comments
                .Where(
                    comment =>
                        !IsOwnedDrField(comment))
                .ToArray();

        var afterForeignComments =
            afterComments.Comments
                .Where(
                    comment =>
                        !IsOwnedDrField(comment))
                .ToArray();

        Assert.Equal(
            beforeForeignComments.Length,
            afterForeignComments.Length);

        // Fremde Tags müssen nicht nur denselben
        // Text besitzen, sondern bytegenau gleich
        // und in derselben Reihenfolge vorliegen.
        for (var index = 0;
             index < beforeForeignComments.Length;
             index++)
        {
            Assert.True(
                beforeForeignComments[index]
                    .AsSpan()
                    .SequenceEqual(
                        afterForeignComments[index]),
                $"Fremder Vorbis-Comment {index} " +
                "wurde verändert.");
        }

        Assert.Equal(
            "20",
            GetSingleFieldValue(
                afterComments.Comments,
                "DYNAMIC RANGE"));

        Assert.Equal(
            "21",
            GetSingleFieldValue(
                afterComments.Comments,
                "ALBUM DYNAMIC RANGE"));
    }

    [Fact]
    public void RemoveCopy_PreservesRealFlacExceptOwnedDrTags()
    {
        var originalPath =
            Environment.GetEnvironmentVariable(
                "DRANALYZER_MANUAL_FLAC_ORIGINAL");

        Assert.False(
            string.IsNullOrWhiteSpace(originalPath),
            "DRANALYZER_MANUAL_FLAC_ORIGINAL ist nicht gesetzt.");

        Assert.True(
            File.Exists(originalPath),
            $"FLAC-Original fehlt: {originalPath}");

        var originalHash =
            Convert.ToHexString(
                SHA256.HashData(
                    File.ReadAllBytes(
                        originalPath)));

        var tempDirectory =
            Path.Combine(
                Path.GetTempPath(),
                "DRAnalyzer-FlacRemove-" +
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(
            tempDirectory);

        var copyPath =
            Path.Combine(
                tempDirectory,
                Path.GetFileName(
                    originalPath));

        try
        {
            File.Copy(
                originalPath,
                copyPath);

            var before =
                ReadSnapshot(
                    originalPath);

            FlacDynamicRangeTagWriter.Write(
                copyPath,
                trackDynamicRange: 20,
                albumDynamicRange: 21);

            FlacDynamicRangeTagWriter.Remove(
                copyPath);

            var after =
                ReadSnapshot(
                    copyPath);

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
                    beforeBlock.Data
                        .AsSpan()
                        .SequenceEqual(
                            afterBlock.Data),
                    $"FLAC-Metadatenblock {index} " +
                    "wurde verändert.");
            }

            var beforeCommentBlock =
                Assert.Single(
                    before.Blocks,
                    block =>
                        block.Type ==
                        VorbisCommentBlockType);

            var afterCommentBlock =
                Assert.Single(
                    after.Blocks,
                    block =>
                        block.Type ==
                        VorbisCommentBlockType);

            var beforeComments =
                ParseVorbisComment(
                    beforeCommentBlock.Data);

            var afterComments =
                ParseVorbisComment(
                    afterCommentBlock.Data);

            Assert.True(
                beforeComments.Vendor
                    .AsSpan()
                    .SequenceEqual(
                        afterComments.Vendor),
                "Der Vorbis-Vendor wurde verändert.");

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
                    beforeForeign[index]
                        .AsSpan()
                        .SequenceEqual(
                            afterForeign[index]),
                    $"Fremder Vorbis-Comment {index} " +
                    "wurde verändert.");
            }

            Assert.DoesNotContain(
                afterComments.Comments,
                IsOwnedDrField);

            var originalHashAfter =
                Convert.ToHexString(
                    SHA256.HashData(
                        File.ReadAllBytes(
                            originalPath)));

            Assert.Equal(
                originalHash,
                originalHashAfter);
        }
        finally
        {
            if (Directory.Exists(
                    tempDirectory))
            {
                Directory.Delete(
                    tempDirectory,
                    recursive: true);
            }
        }
    }

    private static FlacSnapshot ReadSnapshot(
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

        var actualName =
            Encoding.ASCII.GetString(
                comment,
                0,
                equalsIndex);

        return string.Equals(
            actualName,
            fieldName,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string GetSingleFieldValue(
        IReadOnlyList<byte[]> comments,
        string fieldName)
    {
        var comment =
            Assert.Single(
                comments,
                value =>
                    IsField(
                        value,
                        fieldName));

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
