using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using DRAnalyzer.Core.Analysis;

namespace DRAnalyzer.Tests;

[Trait("Category", "ExternalReference")]
public sealed class ManualRealFlacAlbumPreservationTests
{
    private const byte VorbisCommentBlockType = 4;

    [Fact]
    public void ModifiedDiscoveryAlbum_PreservesEverythingExceptOwnedDrTags()
    {
        var originalDirectory =
            Environment.GetEnvironmentVariable(
                "DRANALYZER_MANUAL_FLAC_ALBUM_ORIGINAL");

        var modifiedDirectory =
            Environment.GetEnvironmentVariable(
                "DRANALYZER_MANUAL_FLAC_ALBUM_COPY");

        Assert.False(
            string.IsNullOrWhiteSpace(originalDirectory),
            "DRANALYZER_MANUAL_FLAC_ALBUM_ORIGINAL ist nicht gesetzt.");

        Assert.False(
            string.IsNullOrWhiteSpace(modifiedDirectory),
            "DRANALYZER_MANUAL_FLAC_ALBUM_COPY ist nicht gesetzt.");

        Assert.True(
            Directory.Exists(originalDirectory),
            $"Originalordner fehlt: {originalDirectory}");

        Assert.True(
            Directory.Exists(modifiedDirectory),
            $"Testordner fehlt: {modifiedDirectory}");

        var requiredOriginalDirectory = originalDirectory!;
        var requiredModifiedDirectory = modifiedDirectory!;

        var originalFiles =
            Directory.GetFiles(
                    requiredOriginalDirectory,
                    "*.flac",
                    SearchOption.AllDirectories)
                .OrderBy(
                    path => path,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        Assert.Equal(
            14,
            originalFiles.Length);

        var trackResults =
            originalFiles
                .Select(
                    path =>
                        new
                        {
                            Path = path,
                            Result =
                                DynamicRangeAnalyzer.Analyze(path)
                        })
                .ToArray();

        var expectedAlbumDr =
            AlbumDynamicRangeCalculator.Calculate(
                trackResults.Select(
                    item =>
                        item.Result.RoundedDynamicRange));

        Assert.Equal(
            13,
            expectedAlbumDr);

        foreach (var item in trackResults)
        {
            var relativePath =
                Path.GetRelativePath(
                    requiredOriginalDirectory,
                    item.Path);

            var modifiedPath =
                Path.Combine(
                    requiredModifiedDirectory,
                    relativePath);

            Assert.True(
                File.Exists(modifiedPath),
                $"Testkopie fehlt: {relativePath}");

            CompareFiles(
                item.Path,
                modifiedPath,
                item.Result.RoundedDynamicRange,
                expectedAlbumDr);
        }

        var modifiedFiles =
            Directory.GetFiles(
                modifiedDirectory,
                "*.flac",
                SearchOption.AllDirectories);

        Assert.Equal(
            originalFiles.Length,
            modifiedFiles.Length);
    }

    private static void CompareFiles(
        string originalPath,
        string modifiedPath,
        int expectedTrackDr,
        int expectedAlbumDr)
    {
        var before =
            ReadSnapshot(originalPath);

        var after =
            ReadSnapshot(modifiedPath);

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
                $"{Path.GetFileName(modifiedPath)}: " +
                $"Metadatenblock {index} " +
                $"vom Typ {beforeBlock.Type} wurde verändert.");
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
            $"{Path.GetFileName(modifiedPath)}: " +
            "Vorbis-Vendor wurde verändert.");

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
                $"{Path.GetFileName(modifiedPath)}: " +
                $"Fremder Vorbis-Comment {index} wurde verändert.");
        }

        Assert.Equal(
            expectedTrackDr.ToString(),
            GetSingleFieldValue(
                afterComments.Comments,
                "DYNAMIC RANGE"));

        Assert.Equal(
            expectedAlbumDr.ToString(),
            GetSingleFieldValue(
                afterComments.Comments,
                "ALBUM DYNAMIC RANGE"));
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

    private static ParsedVorbisComment ParseVorbisComment(
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
