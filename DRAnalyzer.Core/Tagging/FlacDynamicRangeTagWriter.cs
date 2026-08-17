using System.Buffers.Binary;
using System.Text;

namespace DRAnalyzer.Core.Tagging;

public static class FlacDynamicRangeTagWriter
{
    private const byte VorbisCommentBlockType = 4;

    private const int MaximumMetadataBlockLength =
        0xFFFFFF;

    private const string TrackDynamicRangeField =
        "DYNAMIC RANGE";

    private const string AlbumDynamicRangeField =
        "ALBUM DYNAMIC RANGE";

    private static readonly byte[] FlacMarker =
        Encoding.ASCII.GetBytes("fLaC");

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
                "Die FLAC-Datei wurde nicht gefunden.",
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

        var fullPath =
            Path.GetFullPath(filePath);

        var directory =
            Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException(
                "Das Dateiverzeichnis konnte nicht ermittelt werden.");

        var fileName =
            Path.GetFileName(fullPath);

        var uniqueId =
            Guid.NewGuid().ToString("N");

        var tempPath =
            Path.Combine(
                directory,
                $".{fileName}.{uniqueId}.dranalyzer.tmp");

        var backupPath =
            Path.Combine(
                directory,
                $".{fileName}.{uniqueId}.dranalyzer.backup");

        var replaceSucceeded = false;

        try
        {
            WriteModifiedCopy(
                fullPath,
                tempPath,
                trackDynamicRange,
                albumDynamicRange);

            // Bevor die Originaldatei ersetzt wird,
            // prüfen wir die neu erzeugte Datei selbst noch einmal.
            ValidateWrittenCopy(
                fullPath,
                tempPath,
                trackDynamicRange,
                albumDynamicRange);

            // Original wird erst jetzt ersetzt.
            // File.Replace hält währenddessen eine Backup-Kopie.
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

    public static void Remove(
        string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            filePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException(
                "Die FLAC-Datei wurde nicht gefunden.",
                filePath);
        }

        var fullPath =
            Path.GetFullPath(filePath);

        if (!HasOwnedDynamicRangeTags(
                fullPath))
        {
            return;
        }

        var directory =
            Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException(
                "Das Dateiverzeichnis konnte nicht ermittelt werden.");

        var fileName =
            Path.GetFileName(fullPath);

        var uniqueId =
            Guid.NewGuid().ToString("N");

        var tempPath =
            Path.Combine(
                directory,
                $".{fileName}.{uniqueId}.dranalyzer.tmp");

        var backupPath =
            Path.Combine(
                directory,
                $".{fileName}.{uniqueId}.dranalyzer.backup");

        var replaceSucceeded = false;

        try
        {
            WriteRemovedCopy(
                fullPath,
                tempPath);

            ValidateRemovedCopy(
                fullPath,
                tempPath);

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

    private static bool HasOwnedDynamicRangeTags(
        string filePath)
    {
        using var stream =
            new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

        var blocks =
            ReadMetadataBlocks(
                stream);

        var commentBlocks =
            blocks
                .Where(
                    block =>
                        block.Type ==
                        VorbisCommentBlockType)
                .ToArray();

        if (commentBlocks.Length > 1)
        {
            throw new InvalidDataException(
                "Die FLAC-Datei enthält mehrere " +
                "VORBIS_COMMENT-Blöcke. " +
                "Aus Sicherheitsgründen wird sie nicht verändert.");
        }

        if (commentBlocks.Length == 0)
            return false;

        var parsed =
            ParseVorbisComment(
                commentBlocks[0].Data);

        return
            parsed.Comments.Any(
                IsOwnedDynamicRangeField);
    }

    private static void WriteRemovedCopy(
        string sourcePath,
        string destinationPath)
    {
        using var input =
            new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

        var blocks =
            ReadMetadataBlocks(
                input);

        var commentBlockIndexes =
            blocks
                .Select(
                    (block, index) =>
                        new
                        {
                            Block = block,
                            Index = index
                        })
                .Where(
                    item =>
                        item.Block.Type ==
                        VorbisCommentBlockType)
                .Select(
                    item =>
                        item.Index)
                .ToArray();

        if (commentBlockIndexes.Length != 1)
        {
            throw new InvalidDataException(
                "Der zu bearbeitende FLAC-VORBIS_COMMENT-Block " +
                "ist nicht eindeutig vorhanden.");
        }

        var index =
            commentBlockIndexes[0];

        var current =
            blocks[index];

        blocks[index] =
            current with
            {
                Data =
                    RemoveOwnedDynamicRangeTags(
                        current.Data)
            };

        using var output =
            new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);

        output.Write(
            FlacMarker);

        for (var blockIndex = 0;
             blockIndex < blocks.Count;
             blockIndex++)
        {
            WriteMetadataBlock(
                output,
                blocks[blockIndex],
                isLast:
                    blockIndex ==
                    blocks.Count - 1);
        }

        input.CopyTo(
            output);

        output.Flush(
            flushToDisk: true);
    }

    private static byte[] RemoveOwnedDynamicRangeTags(
        byte[] data)
    {
        var parsed =
            ParseVorbisComment(
                data);

        var comments =
            parsed.Comments
                .Where(
                    comment =>
                        !IsOwnedDynamicRangeField(
                            comment))
                .ToArray();

        return BuildVorbisComment(
            parsed.Vendor,
            comments);
    }

    private static bool IsOwnedDynamicRangeField(
        byte[] comment)
    {
        return
            IsField(
                comment,
                TrackDynamicRangeField) ||
            IsField(
                comment,
                AlbumDynamicRangeField);
    }

    private static void ValidateRemovedCopy(
        string sourcePath,
        string modifiedPath)
    {
        using var source =
            new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

        using var modified =
            new FileStream(
                modifiedPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

        var beforeBlocks =
            ReadMetadataBlocks(
                source);

        var afterBlocks =
            ReadMetadataBlocks(
                modified);

        if (beforeBlocks.Count !=
            afterBlocks.Count)
        {
            throw new InvalidDataException(
                "Die Anzahl der FLAC-Metadatenblöcke " +
                "wurde beim Entfernen der DR-Tags verändert.");
        }

        for (var index = 0;
             index < beforeBlocks.Count;
             index++)
        {
            var beforeBlock =
                beforeBlocks[index];

            var afterBlock =
                afterBlocks[index];

            if (beforeBlock.Type !=
                afterBlock.Type)
            {
                throw new InvalidDataException(
                    "Die Reihenfolge oder der Typ eines " +
                    "FLAC-Metadatenblocks wurde verändert.");
            }

            if (beforeBlock.Type ==
                VorbisCommentBlockType)
            {
                continue;
            }

            if (!beforeBlock.Data
                    .AsSpan()
                    .SequenceEqual(
                        afterBlock.Data))
            {
                throw new InvalidDataException(
                    $"FLAC-Metadatenblock {index} " +
                    "wurde beim Entfernen der DR-Tags verändert.");
            }
        }

        var beforeCommentBlock =
            beforeBlocks.Single(
                block =>
                    block.Type ==
                    VorbisCommentBlockType);

        var afterCommentBlock =
            afterBlocks.Single(
                block =>
                    block.Type ==
                    VorbisCommentBlockType);

        var beforeComments =
            ParseVorbisComment(
                beforeCommentBlock.Data);

        var afterComments =
            ParseVorbisComment(
                afterCommentBlock.Data);

        if (!beforeComments.Vendor
                .AsSpan()
                .SequenceEqual(
                    afterComments.Vendor))
        {
            throw new InvalidDataException(
                "Der FLAC-Vorbis-Vendor wurde verändert.");
        }

        var beforeProtected =
            beforeComments.Comments
                .Where(
                    comment =>
                        !IsOwnedDynamicRangeField(
                            comment))
                .ToArray();

        var afterProtected =
            afterComments.Comments
                .Where(
                    comment =>
                        !IsOwnedDynamicRangeField(
                            comment))
                .ToArray();

        if (beforeProtected.Length !=
            afterProtected.Length)
        {
            throw new InvalidDataException(
                "Die Anzahl fremder FLAC-Vorbis-Kommentare " +
                "wurde verändert.");
        }

        for (var index = 0;
             index < beforeProtected.Length;
             index++)
        {
            if (!beforeProtected[index]
                    .AsSpan()
                    .SequenceEqual(
                        afterProtected[index]))
            {
                throw new InvalidDataException(
                    $"Geschützter FLAC-Vorbis-Kommentar {index} " +
                    "wurde verändert.");
            }
        }

        if (afterComments.Comments.Any(
                IsOwnedDynamicRangeField))
        {
            throw new InvalidDataException(
                "Mindestens ein DR-Tag ist nach dem Entfernen " +
                "noch in der FLAC-Datei vorhanden.");
        }

        if (!StreamsEqual(
                source,
                modified))
        {
            throw new InvalidDataException(
                "Die FLAC-Audioframes wurden beim Entfernen " +
                "der DR-Tags verändert.");
        }
    }

    private static bool StreamsEqual(
        Stream left,
        Stream right)
    {
        const int BufferSize =
            1024 * 1024;

        var leftBuffer =
            new byte[BufferSize];

        var rightBuffer =
            new byte[BufferSize];

        while (true)
        {
            var leftRead =
                left.Read(
                    leftBuffer,
                    0,
                    leftBuffer.Length);

            var rightRead =
                right.Read(
                    rightBuffer,
                    0,
                    rightBuffer.Length);

            if (leftRead !=
                rightRead)
            {
                return false;
            }

            if (leftRead == 0)
                return true;

            if (!leftBuffer
                    .AsSpan(
                        0,
                        leftRead)
                    .SequenceEqual(
                        rightBuffer.AsSpan(
                            0,
                            rightRead)))
            {
                return false;
            }
        }
    }

    private static void WriteModifiedCopy(
        string sourcePath,
        string destinationPath,
        int trackDynamicRange,
        int? albumDynamicRange)
    {
        using var input =
            new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

        var blocks =
            ReadMetadataBlocks(input);

        var commentBlockIndexes =
            blocks
                .Select(
                    (block, index) =>
                        new
                        {
                            Block = block,
                            Index = index
                        })
                .Where(
                    item =>
                        item.Block.Type ==
                        VorbisCommentBlockType)
                .Select(item => item.Index)
                .ToArray();

        if (commentBlockIndexes.Length > 1)
        {
            throw new InvalidDataException(
                "Die FLAC-Datei enthält mehrere " +
                "VORBIS_COMMENT-Blöcke. " +
                "Aus Sicherheitsgründen wird sie nicht verändert.");
        }

        if (commentBlockIndexes.Length == 1)
        {
            var index =
                commentBlockIndexes[0];

            var current =
                blocks[index];

            blocks[index] =
                current with
                {
                    Data =
                        UpdateVorbisComment(
                            current.Data,
                            trackDynamicRange,
                            albumDynamicRange)
                };
        }
        else
        {
            blocks.Add(
                new MetadataBlock(
                    VorbisCommentBlockType,
                    CreateVorbisComment(
                        trackDynamicRange,
                        albumDynamicRange)));
        }

        using var output =
            new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);

        output.Write(FlacMarker);

        for (var index = 0;
             index < blocks.Count;
             index++)
        {
            WriteMetadataBlock(
                output,
                blocks[index],
                isLast:
                    index == blocks.Count - 1);
        }

        // input steht hier bereits exakt am Beginn
        // der FLAC-Audioframes.
        //
        // Die Audioframes werden unverändert kopiert.
        input.CopyTo(output);

        output.Flush(
            flushToDisk: true);
    }

    private static List<MetadataBlock>
        ReadMetadataBlocks(
            Stream stream)
    {
        Span<byte> marker =
            stackalloc byte[4];

        stream.ReadExactly(marker);

        if (!marker.SequenceEqual(FlacMarker))
        {
            throw new InvalidDataException(
                "Die Datei ist keine unterstützte native FLAC-Datei.");
        }

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

            var blockType =
                (byte)(header[0] & 0x7F);

            var blockLength =
                (header[1] << 16) |
                (header[2] << 8) |
                header[3];

            var data =
                new byte[blockLength];

            stream.ReadExactly(data);

            blocks.Add(
                new MetadataBlock(
                    blockType,
                    data));
        }

        return blocks;
    }

    private static void WriteMetadataBlock(
        Stream stream,
        MetadataBlock block,
        bool isLast)
    {
        if (block.Data.Length >
            MaximumMetadataBlockLength)
        {
            throw new InvalidDataException(
                "Ein FLAC-Metadatenblock ist zu groß.");
        }

        Span<byte> header =
            stackalloc byte[4];

        header[0] =
            (byte)(
                block.Type |
                (isLast ? 0x80 : 0x00));

        header[1] =
            (byte)(
                block.Data.Length >> 16);

        header[2] =
            (byte)(
                block.Data.Length >> 8);

        header[3] =
            (byte)block.Data.Length;

        stream.Write(header);
        stream.Write(block.Data);
    }

    private static byte[] UpdateVorbisComment(
        byte[] data,
        int trackDynamicRange,
        int? albumDynamicRange)
    {
        var parsed =
            ParseVorbisComment(data);

        var comments =
            new List<byte[]>();

        foreach (var comment in parsed.Comments)
        {
            // Nur unser eigener Track-DR-Tag
            // darf ersetzt werden.
            if (IsField(
                    comment,
                    TrackDynamicRangeField))
            {
                continue;
            }

            // Album-DR nur anfassen,
            // wenn tatsächlich ein neuer Albumwert
            // geschrieben werden soll.
            if (albumDynamicRange.HasValue &&
                IsField(
                    comment,
                    AlbumDynamicRangeField))
            {
                continue;
            }

            // Alle fremden Kommentare werden
            // bytegenau übernommen.
            comments.Add(comment);
        }

        comments.Add(
            CreateComment(
                TrackDynamicRangeField,
                trackDynamicRange));

        if (albumDynamicRange.HasValue)
        {
            comments.Add(
                CreateComment(
                    AlbumDynamicRangeField,
                    albumDynamicRange.Value));
        }

        return BuildVorbisComment(
            parsed.Vendor,
            comments);
    }

    private static byte[] CreateVorbisComment(
        int trackDynamicRange,
        int? albumDynamicRange)
    {
        var comments =
            new List<byte[]>
            {
                CreateComment(
                    TrackDynamicRangeField,
                    trackDynamicRange)
            };

        if (albumDynamicRange.HasValue)
        {
            comments.Add(
                CreateComment(
                    AlbumDynamicRangeField,
                    albumDynamicRange.Value));
        }

        return BuildVorbisComment(
            Encoding.UTF8.GetBytes(
                "DRAnalyzer"),
            comments);
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
            var commentLength =
                ReadUInt32(
                    data,
                    ref offset);

            comments.Add(
                ReadBytes(
                    data,
                    ref offset,
                    commentLength));
        }

        if (offset != data.Length)
        {
            throw new InvalidDataException(
                "Der VORBIS_COMMENT-Block enthält " +
                "unerwartete zusätzliche Daten.");
        }

        return new ParsedVorbisComment(
            vendor,
            comments);
    }

    private static byte[] BuildVorbisComment(
        byte[] vendor,
        IReadOnlyList<byte[]> comments)
    {
        using var stream =
            new MemoryStream();

        WriteUInt32(
            stream,
            checked((uint)vendor.Length));

        stream.Write(vendor);

        WriteUInt32(
            stream,
            checked((uint)comments.Count));

        foreach (var comment in comments)
        {
            WriteUInt32(
                stream,
                checked((uint)comment.Length));

            stream.Write(comment);
        }

        if (stream.Length >
            MaximumMetadataBlockLength)
        {
            throw new InvalidDataException(
                "Der resultierende VORBIS_COMMENT-Block " +
                "ist für FLAC zu groß.");
        }

        return stream.ToArray();
    }

    private static byte[] CreateComment(
        string fieldName,
        int value)
    {
        return Encoding.UTF8.GetBytes(
            $"{fieldName}={value}");
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

        var currentField =
            Encoding.ASCII.GetString(
                comment,
                0,
                equalsIndex);

        return string.Equals(
            currentField,
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

        if (equalsIndex < 0)
            return "";

        return Encoding.UTF8.GetString(
            comment,
            equalsIndex + 1,
            comment.Length - equalsIndex - 1);
    }

    private static void ValidateWrittenCopy(
        string sourcePath,
        string modifiedPath,
        int expectedTrackDynamicRange,
        int? expectedAlbumDynamicRange)
    {
        using var source =
            new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

        using var modified =
            new FileStream(
                modifiedPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

        var beforeBlocks =
            ReadMetadataBlocks(source);

        var afterBlocks =
            ReadMetadataBlocks(modified);

        var beforeCommentIndexes =
            beforeBlocks
                .Select(
                    (block, index) =>
                        new
                        {
                            Block = block,
                            Index = index
                        })
                .Where(
                    item =>
                        item.Block.Type ==
                        VorbisCommentBlockType)
                .Select(item => item.Index)
                .ToArray();

        var afterCommentIndexes =
            afterBlocks
                .Select(
                    (block, index) =>
                        new
                        {
                            Block = block,
                            Index = index
                        })
                .Where(
                    item =>
                        item.Block.Type ==
                        VorbisCommentBlockType)
                .Select(item => item.Index)
                .ToArray();

        if (beforeCommentIndexes.Length > 1 ||
            afterCommentIndexes.Length != 1)
        {
            throw new InvalidDataException(
                "Der FLAC-VORBIS_COMMENT-Block konnte beim Schreiben " +
                "nicht eindeutig verifiziert werden.");
        }

        if (beforeCommentIndexes.Length == 1)
        {
            if (beforeBlocks.Count !=
                afterBlocks.Count)
            {
                throw new InvalidDataException(
                    "Die Anzahl der FLAC-Metadatenblöcke " +
                    "wurde beim Schreiben der DR-Tags verändert.");
            }

            for (var index = 0;
                 index < beforeBlocks.Count;
                 index++)
            {
                var beforeBlock =
                    beforeBlocks[index];

                var afterBlock =
                    afterBlocks[index];

                if (beforeBlock.Type !=
                    afterBlock.Type)
                {
                    throw new InvalidDataException(
                        "Die Reihenfolge oder der Typ eines " +
                        "FLAC-Metadatenblocks wurde beim Schreiben verändert.");
                }

                if (beforeBlock.Type ==
                    VorbisCommentBlockType)
                {
                    continue;
                }

                if (!beforeBlock.Data
                        .AsSpan()
                        .SequenceEqual(
                            afterBlock.Data))
                {
                    throw new InvalidDataException(
                        $"FLAC-Metadatenblock {index} " +
                        "wurde beim Schreiben der DR-Tags verändert.");
                }
            }
        }
        else
        {
            if (afterBlocks.Count !=
                beforeBlocks.Count + 1)
            {
                throw new InvalidDataException(
                    "Beim Erzeugen des FLAC-VORBIS_COMMENT-Blocks " +
                    "wurde die Metadatenblock-Struktur unerwartet verändert.");
            }

            for (var index = 0;
                 index < beforeBlocks.Count;
                 index++)
            {
                var beforeBlock =
                    beforeBlocks[index];

                var afterBlock =
                    afterBlocks[index];

                if (beforeBlock.Type !=
                        afterBlock.Type ||
                    !beforeBlock.Data
                        .AsSpan()
                        .SequenceEqual(
                            afterBlock.Data))
                {
                    throw new InvalidDataException(
                        $"FLAC-Metadatenblock {index} " +
                        "wurde beim Erzeugen des DR-Kommentarblocks verändert.");
                }
            }

            if (afterBlocks[^1].Type !=
                VorbisCommentBlockType)
            {
                throw new InvalidDataException(
                    "Der neue FLAC-VORBIS_COMMENT-Block wurde nicht " +
                    "an der erwarteten Position erzeugt.");
            }
        }

        ParsedVorbisComment? beforeComments =
            beforeCommentIndexes.Length == 1
                ? ParseVorbisComment(
                    beforeBlocks[
                        beforeCommentIndexes[0]].Data)
                : null;

        var afterComments =
            ParseVorbisComment(
                afterBlocks[
                    afterCommentIndexes[0]].Data);

        if (beforeComments is not null &&
            !beforeComments.Vendor
                .AsSpan()
                .SequenceEqual(
                    afterComments.Vendor))
        {
            throw new InvalidDataException(
                "Der FLAC-Vorbis-Vendor wurde beim Schreiben verändert.");
        }

        var beforeProtected =
            beforeComments?.Comments
                .Where(
                    comment =>
                        ShouldPreserveOnWrite(
                            comment,
                            expectedAlbumDynamicRange.HasValue))
                .ToArray()
            ?? Array.Empty<byte[]>();

        var afterProtected =
            afterComments.Comments
                .Where(
                    comment =>
                        ShouldPreserveOnWrite(
                            comment,
                            expectedAlbumDynamicRange.HasValue))
                .ToArray();

        if (beforeProtected.Length !=
            afterProtected.Length)
        {
            throw new InvalidDataException(
                "Die Anzahl geschützter FLAC-Vorbis-Kommentare " +
                "wurde beim Schreiben verändert.");
        }

        for (var index = 0;
             index < beforeProtected.Length;
             index++)
        {
            if (!beforeProtected[index]
                    .AsSpan()
                    .SequenceEqual(
                        afterProtected[index]))
            {
                throw new InvalidDataException(
                    $"Geschützter FLAC-Vorbis-Kommentar {index} " +
                    "wurde beim Schreiben verändert.");
            }
        }

        var trackValues =
            afterComments.Comments
                .Where(
                    comment =>
                        IsField(
                            comment,
                            TrackDynamicRangeField))
                .Select(GetFieldValue)
                .ToArray();

        var expectedTrack =
            expectedTrackDynamicRange.ToString();

        if (trackValues.Length != 1 ||
            trackValues[0] != expectedTrack)
        {
            throw new InvalidDataException(
                "Der Track-DR-Tag konnte nicht " +
                "sicher verifiziert werden.");
        }

        if (expectedAlbumDynamicRange.HasValue)
        {
            var albumValues =
                afterComments.Comments
                    .Where(
                        comment =>
                            IsField(
                                comment,
                                AlbumDynamicRangeField))
                    .Select(GetFieldValue)
                    .ToArray();

            var expectedAlbum =
                expectedAlbumDynamicRange.Value.ToString();

            if (albumValues.Length != 1 ||
                albumValues[0] != expectedAlbum)
            {
                throw new InvalidDataException(
                    "Der Album-DR-Tag konnte nicht " +
                    "sicher verifiziert werden.");
            }
        }

        if (!StreamsEqual(
                source,
                modified))
        {
            throw new InvalidDataException(
                "Die FLAC-Audioframes wurden beim Schreiben " +
                "der DR-Tags verändert.");
        }
    }

    private static bool ShouldPreserveOnWrite(
        byte[] comment,
        bool replaceAlbumDynamicRange)
    {
        if (IsField(
                comment,
                TrackDynamicRangeField))
        {
            return false;
        }

        if (replaceAlbumDynamicRange &&
            IsField(
                comment,
                AlbumDynamicRangeField))
        {
            return false;
        }

        return true;
    }

    private static uint ReadUInt32(
        byte[] data,
        ref int offset)
    {
        if (offset > data.Length - 4)
        {
            throw new InvalidDataException(
                "Ungültiger VORBIS_COMMENT-Block.");
        }

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
        if (length > int.MaxValue)
        {
            throw new InvalidDataException(
                "Ungültige VORBIS_COMMENT-Länge.");
        }

        var intLength =
            (int)length;

        if (offset > data.Length - intLength)
        {
            throw new InvalidDataException(
                "Der VORBIS_COMMENT-Block ist beschädigt.");
        }

        var result =
            data
                .AsSpan(
                    offset,
                    intLength)
                .ToArray();

        offset += intLength;

        return result;
    }

    private static void WriteUInt32(
        Stream stream,
        uint value)
    {
        Span<byte> buffer =
            stackalloc byte[4];

        BinaryPrimitives.WriteUInt32LittleEndian(
            buffer,
            value);

        stream.Write(buffer);
    }

    private sealed record MetadataBlock(
        byte Type,
        byte[] Data);

    private sealed record ParsedVorbisComment(
        byte[] Vendor,
        IReadOnlyList<byte[]> Comments);
}

