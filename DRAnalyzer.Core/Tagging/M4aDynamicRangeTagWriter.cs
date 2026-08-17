using System.Buffers.Binary;
using System.Text;

namespace DRAnalyzer.Core.Tagging;

public static class M4aDynamicRangeTagWriter
{
    private const string TrackDynamicRangeField =
        "DYNAMIC RANGE";

    private const string AlbumDynamicRangeField =
        "ALBUM DYNAMIC RANGE";

    private const string FreeformMean =
        "com.apple.iTunes";

    private static readonly UTF8Encoding StrictUtf8 =
        new(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);

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
                "Die M4A-Datei wurde nicht gefunden.",
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

        RewriteSafely(
            fullPath,
            ilst =>
                EditIlstForWrite(
                    ilst,
                    trackDynamicRange,
                    albumDynamicRange),
            validation =>
                ValidateWrittenTags(
                    validation.TargetIlst,
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
                "Die M4A-Datei wurde nicht gefunden.",
                filePath);
        }

        var fullPath =
            Path.GetFullPath(filePath);

        var source =
            ReadFileLayout(fullPath);

        var sourceIlst =
            LocateStandardIlst(
                source.MoovBytes);

        if (!IlstContainsOwnedTags(sourceIlst.BoxBytes))
        {
            return;
        }

        RewriteSafely(
            fullPath,
            EditIlstForRemove,
            validation =>
                ValidateRemovedTags(
                    validation.TargetIlst));
    }

    private static byte[] EditIlstForWrite(
        byte[] ilstBox,
        int trackDynamicRange,
        int? albumDynamicRange)
    {
        var ilst = ParseBoxAt(
            ilstBox,
            0,
            ilstBox.Length,
            allowSizeZero: false);

        EnsureType(
            ilst,
            "ilst");

        var children =
            ParseChildBoxes(
                ilstBox,
                ilst.PayloadOffset,
                ilst.EndOffset,
                allowSizeZero: false);

        var outputChildren =
            new List<byte[]>();

        var trackWritten = false;
        var albumWritten = false;

        foreach (var child in children)
        {
            var raw =
                Slice(
                    ilstBox,
                    child.Offset,
                    child.Size);

            if (!TypeEquals(child, "----"))
            {
                outputChildren.Add(raw);
                continue;
            }

            var ownedField =
                GetOwnedFieldFromFreeform(raw);

            switch (ownedField)
            {
                case OwnedField.Track:
                    if (!trackWritten)
                    {
                        outputChildren.Add(
                            BuildFreeformItem(
                                TrackDynamicRangeField,
                                trackDynamicRange.ToString()));

                        trackWritten = true;
                    }

                    break;

                case OwnedField.Album:
                    if (albumDynamicRange is null)
                    {
                        // Track-only write: vorhandenen Album-DR
                        // einschließlich etwaiger Sonderstruktur bytegenau erhalten.
                        outputChildren.Add(raw);
                    }
                    else if (!albumWritten)
                    {
                        outputChildren.Add(
                            BuildFreeformItem(
                                AlbumDynamicRangeField,
                                albumDynamicRange.Value.ToString()));

                        albumWritten = true;
                    }

                    break;

                default:
                    outputChildren.Add(raw);
                    break;
            }
        }

        if (!trackWritten)
        {
            outputChildren.Add(
                BuildFreeformItem(
                    TrackDynamicRangeField,
                    trackDynamicRange.ToString()));
        }

        if (albumDynamicRange is not null &&
            !albumWritten)
        {
            outputChildren.Add(
                BuildFreeformItem(
                    AlbumDynamicRangeField,
                    albumDynamicRange.Value.ToString()));
        }

        return RebuildBoxWithPayload(
            ilstBox,
            ilst,
            Concat(outputChildren));
    }

    private static byte[] EditIlstForRemove(
        byte[] ilstBox)
    {
        var ilst = ParseBoxAt(
            ilstBox,
            0,
            ilstBox.Length,
            allowSizeZero: false);

        EnsureType(
            ilst,
            "ilst");

        var children =
            ParseChildBoxes(
                ilstBox,
                ilst.PayloadOffset,
                ilst.EndOffset,
                allowSizeZero: false);

        var outputChildren =
            new List<byte[]>();

        foreach (var child in children)
        {
            var raw =
                Slice(
                    ilstBox,
                    child.Offset,
                    child.Size);

            if (TypeEquals(child, "----") &&
                GetOwnedFieldFromFreeform(raw) != OwnedField.None)
            {
                continue;
            }

            outputChildren.Add(raw);
        }

        return RebuildBoxWithPayload(
            ilstBox,
            ilst,
            Concat(outputChildren));
    }

    private static bool IlstContainsOwnedTags(
        byte[] ilstBox)
    {
        var ilst = ParseBoxAt(
            ilstBox,
            0,
            ilstBox.Length,
            allowSizeZero: false);

        var children =
            ParseChildBoxes(
                ilstBox,
                ilst.PayloadOffset,
                ilst.EndOffset,
                allowSizeZero: false);

        foreach (var child in children)
        {
            if (!TypeEquals(child, "----"))
                continue;

            var raw =
                Slice(
                    ilstBox,
                    child.Offset,
                    child.Size);

            if (GetOwnedFieldFromFreeform(raw) != OwnedField.None)
                return true;
        }

        return false;
    }

    private static OwnedField GetOwnedFieldFromFreeform(
        byte[] freeformBox)
    {
        var root = ParseBoxAt(
            freeformBox,
            0,
            freeformBox.Length,
            allowSizeZero: false);

        if (!TypeEquals(root, "----"))
            return OwnedField.None;

        var children =
            ParseChildBoxes(
                freeformBox,
                root.PayloadOffset,
                root.EndOffset,
                allowSizeZero: false);

        BoxInfo? nameBox = null;

        foreach (var child in children)
        {
            if (!TypeEquals(child, "name"))
                continue;

            if (nameBox is not null)
            {
                // Mehrdeutige freie Metadaten nicht als unser Feld behandeln.
                return OwnedField.None;
            }

            nameBox = child;
        }

        if (nameBox is null)
            return OwnedField.None;

        var value =
            ReadFullBoxUtf8Text(
                freeformBox,
                nameBox.Value);

        if (string.Equals(
                value,
                TrackDynamicRangeField,
                StringComparison.OrdinalIgnoreCase))
        {
            return OwnedField.Track;
        }

        if (string.Equals(
                value,
                AlbumDynamicRangeField,
                StringComparison.OrdinalIgnoreCase))
        {
            return OwnedField.Album;
        }

        return OwnedField.None;
    }

    private static string ReadFullBoxUtf8Text(
        byte[] bytes,
        BoxInfo box)
    {
        if (box.PayloadLength < 4)
        {
            throw new InvalidDataException(
                $"Der MP4-Atom '{box.Type}' ist zu kurz.");
        }

        var textOffset =
            checked(box.PayloadOffset + 4);

        var textLength =
            checked(box.PayloadLength - 4);

        try
        {
            return StrictUtf8.GetString(
                bytes,
                textOffset,
                textLength);
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidDataException(
                $"Der MP4-Atom '{box.Type}' enthält ungültiges UTF-8.",
                ex);
        }
    }

    private static byte[] BuildFreeformItem(
        string name,
        string value)
    {
        var meanBytes =
            StrictUtf8.GetBytes(FreeformMean);

        var nameBytes =
            StrictUtf8.GetBytes(name);

        var valueBytes =
            StrictUtf8.GetBytes(value);

        var mean =
            Build32BitBox(
                "mean",
                Concat(
                    new byte[4],
                    meanBytes));

        var nameBox =
            Build32BitBox(
                "name",
                Concat(
                    new byte[4],
                    nameBytes));

        // data: 32-bit type indicator 1 = UTF-8 string,
        // danach 32-bit locale/reserved, danach Nutzdaten.
        var dataPrefix =
            new byte[8];

        BinaryPrimitives.WriteUInt32BigEndian(
            dataPrefix.AsSpan(0, 4),
            1);

        var data =
            Build32BitBox(
                "data",
                Concat(
                    dataPrefix,
                    valueBytes));

        return Build32BitBox(
            "----",
            Concat(
                mean,
                nameBox,
                data));
    }

    private static void RewriteSafely(
        string fullPath,
        Func<byte[], byte[]> editIlst,
        Action<ValidationContext> validateTagState)
    {
        var source =
            ReadFileLayout(fullPath);

        var sourceIlst =
            LocateStandardIlst(
                source.MoovBytes);

        var editedIlst =
            editIlst(
                sourceIlst.BoxBytes);

        var editedMoov =
            ReplaceIlstInMoov(
                source.MoovBytes,
                sourceIlst,
                editedIlst);

        var delta =
            checked(
                (long)editedMoov.Length -
                source.MoovBytes.Length);

        if (delta != 0)
        {
            AdjustChunkOffsets(
                editedMoov,
                source.MoovTopLevel.Offset,
                checked(
                    source.MoovTopLevel.Offset +
                    source.MoovTopLevel.Size),
                delta);
        }

        var fullDirectory =
            Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException(
                "Das Dateiverzeichnis konnte nicht ermittelt werden.");

        var fileName =
            Path.GetFileName(fullPath);

        var uniqueId =
            Guid.NewGuid().ToString("N");

        var tempPath =
            Path.Combine(
                fullDirectory,
                $".{fileName}.{uniqueId}.dranalyzer.tmp");

        var backupPath =
            Path.Combine(
                fullDirectory,
                $".{fileName}.{uniqueId}.dranalyzer.backup");

        var replaceSucceeded = false;

        try
        {
            WriteEditedFile(
                fullPath,
                tempPath,
                source,
                editedMoov);

            ValidateNonMoovTopLevelBoxesPreserved(
                fullPath,
                tempPath);

            var target =
                ReadFileLayout(tempPath);

            var targetIlst =
                LocateStandardIlst(
                    target.MoovBytes);

            validateTagState(
                new ValidationContext(
                    targetIlst.BoxBytes));

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

    private static void WriteEditedFile(
        string sourcePath,
        string destinationPath,
        FileLayout source,
        byte[] editedMoov)
    {
        using var input =
            new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

        using var output =
            new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);

        foreach (var box in source.TopLevelBoxes)
        {
            if (box.Offset == source.MoovTopLevel.Offset)
            {
                output.Write(editedMoov);
                continue;
            }

            input.Position = box.Offset;

            CopyExactly(
                input,
                output,
                box.Size);
        }

        output.Flush(
            flushToDisk: true);
    }

    private static void ValidateNonMoovTopLevelBoxesPreserved(
        string sourcePath,
        string targetPath)
    {
        var source =
            ReadFileLayout(sourcePath);

        var target =
            ReadFileLayout(targetPath);

        if (source.TopLevelBoxes.Count !=
            target.TopLevelBoxes.Count)
        {
            throw new InvalidDataException(
                "Die Anzahl der MP4-Top-Level-Atome hat sich unerwartet verändert.");
        }

        using var sourceStream =
            new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

        using var targetStream =
            new FileStream(
                targetPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

        for (var i = 0;
             i < source.TopLevelBoxes.Count;
             i++)
        {
            var left = source.TopLevelBoxes[i];
            var right = target.TopLevelBoxes[i];

            if (!string.Equals(
                    left.Type,
                    right.Type,
                    StringComparison.Ordinal) ||
                (left.Type != "moov" &&
                 left.Size != right.Size))
            {
                throw new InvalidDataException(
                    "Die MP4-Top-Level-Struktur hat sich unerwartet verändert.");
            }

            if (left.Type == "moov")
                continue;

            if (!RangesEqual(
                    sourceStream,
                    left.Offset,
                    targetStream,
                    right.Offset,
                    left.Size))
            {
                throw new InvalidDataException(
                    $"Der MP4-Atom '{left.Type}' wurde unerwartet verändert.");
            }
        }
    }

    private static FileLayout ReadFileLayout(
        string filePath)
    {
        using var stream =
            new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

        var boxes =
            ReadTopLevelBoxes(stream);

        if (boxes.Count == 0 ||
            boxes[0].Type != "ftyp")
        {
            throw new InvalidDataException(
                "Die Datei besitzt keinen erwarteten MP4/M4A-ftyp-Atom am Anfang.");
        }

        if (boxes.Any(
                box =>
                    box.Type is
                        "moof" or
                        "mfra" or
                        "sidx"))
        {
            throw new NotSupportedException(
                "Fragmentierte MP4/M4A-Dateien werden aus Sicherheitsgründen noch nicht unterstützt.");
        }

        var moovBoxes =
            boxes
                .Where(
                    box => box.Type == "moov")
                .ToArray();

        if (moovBoxes.Length != 1)
        {
            throw new InvalidDataException(
                "Die M4A-Datei muss genau einen moov-Atom besitzen.");
        }

        if (!boxes.Any(
                box => box.Type == "mdat"))
        {
            throw new InvalidDataException(
                "Die M4A-Datei besitzt keinen mdat-Atom.");
        }

        var moov =
            moovBoxes[0];

        if (moov.Size > int.MaxValue)
        {
            throw new NotSupportedException(
                "Ein moov-Atom größer als 2 GiB wird nicht unterstützt.");
        }

        stream.Position =
            moov.Offset;

        var moovBytes =
            new byte[(int)moov.Size];

        ReadExactly(
            stream,
            moovBytes);

        return new FileLayout(
            boxes,
            moov,
            moovBytes);
    }

    private static List<TopLevelBox> ReadTopLevelBoxes(
        FileStream stream)
    {
        var result =
            new List<TopLevelBox>();

        var fileLength =
            stream.Length;

        long offset = 0;

        while (offset < fileLength)
        {
            if (fileLength - offset < 8)
            {
                throw new InvalidDataException(
                    "Die MP4-Datei endet innerhalb eines Atom-Headers.");
            }

            stream.Position = offset;

            var header =
                new byte[16];

            ReadExactly(
                stream,
                header.AsSpan(0, 8));

            var size32 =
                BinaryPrimitives.ReadUInt32BigEndian(
                    header.AsSpan(0, 4));

            var type =
                FourCc(
                    header.AsSpan(4, 4));

            long size;
            int headerSize;

            if (size32 == 1)
            {
                ReadExactly(
                    stream,
                    header.AsSpan(8, 8));

                var size64 =
                    BinaryPrimitives.ReadUInt64BigEndian(
                        header.AsSpan(8, 8));

                if (size64 > long.MaxValue)
                {
                    throw new InvalidDataException(
                        "Ein MP4-Atom ist zu groß.");
                }

                size = (long)size64;
                headerSize = 16;
            }
            else if (size32 == 0)
            {
                throw new NotSupportedException(
                    $"Ein Top-Level-MP4-Atom mit size=0 ('{type}') wird aus Sicherheitsgründen noch nicht unterstützt.");
            }
            else
            {
                size = size32;
                headerSize = 8;
            }

            if (size < headerSize ||
                size > fileLength - offset)
            {
                throw new InvalidDataException(
                    $"Der MP4-Atom '{type}' besitzt eine ungültige Größe.");
            }

            result.Add(
                new TopLevelBox(
                    type,
                    offset,
                    size,
                    headerSize));

            offset =
                checked(offset + size);
        }

        return result;
    }

    private static LocatedIlst LocateStandardIlst(
        byte[] moovBytes)
    {
        var moov = ParseBoxAt(
            moovBytes,
            0,
            moovBytes.Length,
            allowSizeZero: false);

        EnsureType(
            moov,
            "moov");

        var moovChildren =
            ParseChildBoxes(
                moovBytes,
                moov.PayloadOffset,
                moov.EndOffset,
                allowSizeZero: false);

        var udtas =
            moovChildren
                .Where(
                    box => TypeEquals(box, "udta"))
                .ToArray();

        if (udtas.Length != 1)
        {
            throw new NotSupportedException(
                "Für M4A-Schreiben wird derzeit genau ein vorhandener moov/udta-Metadatenbereich benötigt.");
        }

        var udta =
            udtas[0];

        var udtaChildren =
            ParseChildBoxes(
                moovBytes,
                udta.PayloadOffset,
                udta.EndOffset,
                allowSizeZero: false);

        var metas =
            udtaChildren
                .Where(
                    box => TypeEquals(box, "meta"))
                .ToArray();

        if (metas.Length != 1)
        {
            throw new NotSupportedException(
                "Für M4A-Schreiben wird derzeit genau ein vorhandener iTunes-style udta/meta-Bereich benötigt.");
        }

        var meta =
            metas[0];

        if (meta.PayloadLength < 4)
        {
            throw new InvalidDataException(
                "Der M4A-meta-Atom ist zu kurz.");
        }

        // meta ist ein FullBox: vier Bytes Version/Flags vor den Kindatomen.
        var metaChildrenStart =
            checked(meta.PayloadOffset + 4);

        var metaChildren =
            ParseChildBoxes(
                moovBytes,
                metaChildrenStart,
                meta.EndOffset,
                allowSizeZero: false);

        var ilsts =
            metaChildren
                .Where(
                    box => TypeEquals(box, "ilst"))
                .ToArray();

        if (ilsts.Length != 1)
        {
            throw new NotSupportedException(
                "Für M4A-Schreiben wird derzeit ein vorhandener iTunes-style ilst-Metadatenatom benötigt.");
        }

        var ilst =
            ilsts[0];

        return new LocatedIlst(
            udta,
            meta,
            ilst,
            Slice(
                moovBytes,
                ilst.Offset,
                ilst.Size));
    }

    private static byte[] ReplaceIlstInMoov(
        byte[] sourceMoov,
        LocatedIlst location,
        byte[] editedIlst)
    {
        var editedMeta =
            ReplaceRangeAndRebuildContainer(
                sourceMoov,
                location.Meta,
                location.Ilst.Offset,
                location.Ilst.Size,
                editedIlst,
                prefixLength: 4);

        var editedUdta =
            ReplaceChildInRebuiltParent(
                sourceMoov,
                location.Udta,
                location.Meta,
                editedMeta,
                prefixLength: 0);

        var moov = ParseBoxAt(
            sourceMoov,
            0,
            sourceMoov.Length,
            allowSizeZero: false);

        return ReplaceChildInRebuiltParent(
            sourceMoov,
            moov,
            location.Udta,
            editedUdta,
            prefixLength: 0);
    }

    private static byte[] ReplaceChildInRebuiltParent(
        byte[] source,
        BoxInfo parent,
        BoxInfo child,
        byte[] replacement,
        int prefixLength)
    {
        return ReplaceRangeAndRebuildContainer(
            source,
            parent,
            child.Offset,
            child.Size,
            replacement,
            prefixLength);
    }

    private static byte[] ReplaceRangeAndRebuildContainer(
        byte[] source,
        BoxInfo container,
        int replaceOffset,
        int replaceLength,
        byte[] replacement,
        int prefixLength)
    {
        if (replaceOffset < container.PayloadOffset + prefixLength ||
            replaceOffset + replaceLength > container.EndOffset)
        {
            throw new InvalidOperationException(
                "Der zu ersetzende MP4-Bereich liegt außerhalb seines Containers.");
        }

        var payloadStart =
            container.PayloadOffset;

        var beforeLength =
            checked(replaceOffset - payloadStart);

        var afterStart =
            checked(replaceOffset + replaceLength);

        var afterLength =
            checked(container.EndOffset - afterStart);

        var newPayload =
            new byte[
                checked(
                    beforeLength +
                    replacement.Length +
                    afterLength)];

        Buffer.BlockCopy(
            source,
            payloadStart,
            newPayload,
            0,
            beforeLength);

        Buffer.BlockCopy(
            replacement,
            0,
            newPayload,
            beforeLength,
            replacement.Length);

        Buffer.BlockCopy(
            source,
            afterStart,
            newPayload,
            beforeLength + replacement.Length,
            afterLength);

        return RebuildBoxWithPayload(
            source,
            container,
            newPayload);
    }

    private static byte[] RebuildBoxWithPayload(
        byte[] source,
        BoxInfo original,
        byte[] payload)
    {
        var newSize =
            checked((long)original.HeaderSize + payload.Length);

        if (original.HeaderSize == 8 &&
            newSize > uint.MaxValue)
        {
            throw new NotSupportedException(
                $"Der MP4-Atom '{original.Type}' würde die 32-Bit-Größengrenze überschreiten.");
        }

        if (newSize > int.MaxValue)
        {
            throw new NotSupportedException(
                $"Der MP4-Atom '{original.Type}' ist zu groß.");
        }

        var result =
            new byte[(int)newSize];

        if (original.HeaderSize == 8)
        {
            BinaryPrimitives.WriteUInt32BigEndian(
                result.AsSpan(0, 4),
                checked((uint)newSize));

            Buffer.BlockCopy(
                source,
                original.Offset + 4,
                result,
                4,
                4);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(
                result.AsSpan(0, 4),
                1);

            Buffer.BlockCopy(
                source,
                original.Offset + 4,
                result,
                4,
                4);

            BinaryPrimitives.WriteUInt64BigEndian(
                result.AsSpan(8, 8),
                checked((ulong)newSize));
        }

        Buffer.BlockCopy(
            payload,
            0,
            result,
            original.HeaderSize,
            payload.Length);

        return result;
    }

    private static void AdjustChunkOffsets(
        byte[] moovBytes,
        long sourceMoovStart,
        long sourceMoovEnd,
        long delta)
    {
        var moov = ParseBoxAt(
            moovBytes,
            0,
            moovBytes.Length,
            allowSizeZero: false);

        foreach (var trak in FindChildren(
                     moovBytes,
                     moov,
                     "trak"))
        {
            foreach (var mdia in FindChildren(
                         moovBytes,
                         trak,
                         "mdia"))
            {
                foreach (var minf in FindChildren(
                             moovBytes,
                             mdia,
                             "minf"))
                {
                    foreach (var stbl in FindChildren(
                                 moovBytes,
                                 minf,
                                 "stbl"))
                    {
                        foreach (var child in ParseChildBoxes(
                                     moovBytes,
                                     stbl.PayloadOffset,
                                     stbl.EndOffset,
                                     allowSizeZero: false))
                        {
                            if (TypeEquals(child, "stco"))
                            {
                                AdjustStco(
                                    moovBytes,
                                    child,
                                    sourceMoovStart,
                                    sourceMoovEnd,
                                    delta);
                            }
                            else if (TypeEquals(child, "co64"))
                            {
                                AdjustCo64(
                                    moovBytes,
                                    child,
                                    sourceMoovStart,
                                    sourceMoovEnd,
                                    delta);
                            }
                        }
                    }
                }
            }
        }
    }

    private static IEnumerable<BoxInfo> FindChildren(
        byte[] bytes,
        BoxInfo parent,
        string type)
    {
        return ParseChildBoxes(
                bytes,
                parent.PayloadOffset,
                parent.EndOffset,
                allowSizeZero: false)
            .Where(
                box => TypeEquals(box, type));
    }

    private static void AdjustStco(
        byte[] bytes,
        BoxInfo box,
        long sourceMoovStart,
        long sourceMoovEnd,
        long delta)
    {
        if (box.PayloadLength < 8)
        {
            throw new InvalidDataException(
                "Ein stco-Atom ist zu kurz.");
        }

        var count =
            BinaryPrimitives.ReadUInt32BigEndian(
                bytes.AsSpan(
                    box.PayloadOffset + 4,
                    4));

        var expectedLength =
            checked(8L + count * 4L);

        if (expectedLength != box.PayloadLength)
        {
            throw new InvalidDataException(
                "Ein stco-Atom besitzt eine unerwartete Größe.");
        }

        var offset =
            box.PayloadOffset + 8;

        for (uint i = 0; i < count; i++)
        {
            var current =
                BinaryPrimitives.ReadUInt32BigEndian(
                    bytes.AsSpan(offset, 4));

            var adjusted =
                AdjustAbsoluteOffset(
                    current,
                    sourceMoovStart,
                    sourceMoovEnd,
                    delta);

            if (adjusted > uint.MaxValue)
            {
                throw new NotSupportedException(
                    "Ein stco-Chunk-Offset würde 32 Bit überschreiten.");
            }

            BinaryPrimitives.WriteUInt32BigEndian(
                bytes.AsSpan(offset, 4),
                checked((uint)adjusted));

            offset += 4;
        }
    }

    private static void AdjustCo64(
        byte[] bytes,
        BoxInfo box,
        long sourceMoovStart,
        long sourceMoovEnd,
        long delta)
    {
        if (box.PayloadLength < 8)
        {
            throw new InvalidDataException(
                "Ein co64-Atom ist zu kurz.");
        }

        var count =
            BinaryPrimitives.ReadUInt32BigEndian(
                bytes.AsSpan(
                    box.PayloadOffset + 4,
                    4));

        var expectedLength =
            checked(8L + count * 8L);

        if (expectedLength != box.PayloadLength)
        {
            throw new InvalidDataException(
                "Ein co64-Atom besitzt eine unerwartete Größe.");
        }

        var offset =
            box.PayloadOffset + 8;

        for (uint i = 0; i < count; i++)
        {
            var current =
                BinaryPrimitives.ReadUInt64BigEndian(
                    bytes.AsSpan(offset, 8));

            if (current > long.MaxValue)
            {
                throw new NotSupportedException(
                    "Ein co64-Chunk-Offset ist größer als Int64.MaxValue.");
            }

            var adjusted =
                AdjustAbsoluteOffset(
                    (long)current,
                    sourceMoovStart,
                    sourceMoovEnd,
                    delta);

            BinaryPrimitives.WriteUInt64BigEndian(
                bytes.AsSpan(offset, 8),
                checked((ulong)adjusted));

            offset += 8;
        }
    }

    private static long AdjustAbsoluteOffset(
        long current,
        long sourceMoovStart,
        long sourceMoovEnd,
        long delta)
    {
        if (current >= sourceMoovStart &&
            current < sourceMoovEnd)
        {
            throw new InvalidDataException(
                "Ein Media-Chunk-Offset zeigt unerwartet in den moov-Atom.");
        }

        if (current < sourceMoovEnd)
            return current;

        var adjusted =
            checked(current + delta);

        if (adjusted < 0)
        {
            throw new InvalidDataException(
                "Ein angepasster Media-Chunk-Offset wäre negativ.");
        }

        return adjusted;
    }

    private static void ValidateWrittenTags(
        byte[] ilstBox,
        int trackDynamicRange,
        int? albumDynamicRange)
    {
        var values =
            ReadOwnedValues(ilstBox);

        if (values.Track.Count != 1 ||
            values.Track[0] != trackDynamicRange.ToString())
        {
            throw new InvalidDataException(
                "Der geschriebene M4A-Track-DR konnte nicht eindeutig validiert werden.");
        }

        if (albumDynamicRange is null)
            return;

        if (values.Album.Count != 1 ||
            values.Album[0] != albumDynamicRange.Value.ToString())
        {
            throw new InvalidDataException(
                "Der geschriebene M4A-Album-DR konnte nicht eindeutig validiert werden.");
        }
    }

    private static void ValidateRemovedTags(
        byte[] ilstBox)
    {
        var values =
            ReadOwnedValues(ilstBox);

        if (values.Track.Count != 0 ||
            values.Album.Count != 0)
        {
            throw new InvalidDataException(
                "Die M4A-DR-Tags wurden nicht vollständig entfernt.");
        }
    }

    private static OwnedValues ReadOwnedValues(
        byte[] ilstBox)
    {
        var track =
            new List<string>();

        var album =
            new List<string>();

        var ilst = ParseBoxAt(
            ilstBox,
            0,
            ilstBox.Length,
            allowSizeZero: false);

        foreach (var child in ParseChildBoxes(
                     ilstBox,
                     ilst.PayloadOffset,
                     ilst.EndOffset,
                     allowSizeZero: false))
        {
            if (!TypeEquals(child, "----"))
                continue;

            var raw =
                Slice(
                    ilstBox,
                    child.Offset,
                    child.Size);

            var owned =
                GetOwnedFieldFromFreeform(raw);

            if (owned == OwnedField.None)
                continue;

            var value =
                ReadFreeformDataValue(raw);

            if (owned == OwnedField.Track)
                track.Add(value);
            else
                album.Add(value);
        }

        return new OwnedValues(
            track,
            album);
    }

    private static string ReadFreeformDataValue(
        byte[] freeformBox)
    {
        var root = ParseBoxAt(
            freeformBox,
            0,
            freeformBox.Length,
            allowSizeZero: false);

        var dataBoxes =
            ParseChildBoxes(
                freeformBox,
                root.PayloadOffset,
                root.EndOffset,
                allowSizeZero: false)
            .Where(
                box => TypeEquals(box, "data"))
            .ToArray();

        if (dataBoxes.Length != 1 ||
            dataBoxes[0].PayloadLength < 8)
        {
            throw new InvalidDataException(
                "Ein DR-freeform-Metadatenatom besitzt keinen eindeutigen data-Atom.");
        }

        var data =
            dataBoxes[0];

        var dataType =
            BinaryPrimitives.ReadUInt32BigEndian(
                freeformBox.AsSpan(
                    data.PayloadOffset,
                    4));

        if (dataType != 1)
        {
            throw new InvalidDataException(
                "Ein DR-freeform-Metadatenatom besitzt keinen UTF-8-String-Datentyp.");
        }

        try
        {
            return StrictUtf8.GetString(
                freeformBox,
                data.PayloadOffset + 8,
                data.PayloadLength - 8);
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidDataException(
                "Ein DR-freeform-Metadatenwert enthält ungültiges UTF-8.",
                ex);
        }
    }

    private static List<BoxInfo> ParseChildBoxes(
        byte[] bytes,
        int start,
        int end,
        bool allowSizeZero)
    {
        if (start < 0 ||
            end < start ||
            end > bytes.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(start));
        }

        var result =
            new List<BoxInfo>();

        var offset = start;

        while (offset < end)
        {
            var box =
                ParseBoxAt(
                    bytes,
                    offset,
                    end,
                    allowSizeZero);

            result.Add(box);

            offset =
                box.EndOffset;
        }

        if (offset != end)
        {
            throw new InvalidDataException(
                "Die MP4-Kindatome enden nicht exakt am Containerende.");
        }

        return result;
    }

    private static BoxInfo ParseBoxAt(
        byte[] bytes,
        int offset,
        int parentEnd,
        bool allowSizeZero)
    {
        if (offset < 0 ||
            parentEnd > bytes.Length ||
            parentEnd - offset < 8)
        {
            throw new InvalidDataException(
                "Ein MP4-Atom-Header ist unvollständig.");
        }

        var size32 =
            BinaryPrimitives.ReadUInt32BigEndian(
                bytes.AsSpan(offset, 4));

        var type =
            FourCc(
                bytes.AsSpan(offset + 4, 4));

        long size;
        int headerSize;

        if (size32 == 1)
        {
            if (parentEnd - offset < 16)
            {
                throw new InvalidDataException(
                    $"Der MP4-Atom '{type}' besitzt keinen vollständigen 64-Bit-Header.");
            }

            var size64 =
                BinaryPrimitives.ReadUInt64BigEndian(
                    bytes.AsSpan(offset + 8, 8));

            if (size64 > int.MaxValue)
            {
                throw new NotSupportedException(
                    $"Der MP4-Atom '{type}' ist zu groß.");
            }

            size = (long)size64;
            headerSize = 16;
        }
        else if (size32 == 0)
        {
            if (!allowSizeZero)
            {
                throw new NotSupportedException(
                    $"Ein size=0-MP4-Atom ('{type}') wird in dieser Struktur nicht unterstützt.");
            }

            size = parentEnd - offset;
            headerSize = 8;
        }
        else
        {
            size = size32;
            headerSize = 8;
        }

        if (size < headerSize ||
            size > parentEnd - offset)
        {
            throw new InvalidDataException(
                $"Der MP4-Atom '{type}' besitzt eine ungültige Größe.");
        }

        return new BoxInfo(
            type,
            offset,
            checked((int)size),
            headerSize);
    }

    private static void EnsureType(
        BoxInfo box,
        string expected)
    {
        if (!TypeEquals(box, expected))
        {
            throw new InvalidDataException(
                $"Erwartet wurde MP4-Atom '{expected}', gefunden wurde '{box.Type}'.");
        }
    }

    private static bool TypeEquals(
        BoxInfo box,
        string expected)
    {
        return string.Equals(
            box.Type,
            expected,
            StringComparison.Ordinal);
    }

    private static string FourCc(
        ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 4)
            throw new ArgumentException(
                "Ein FourCC muss genau vier Bytes lang sein.",
                nameof(bytes));

        return Encoding.Latin1.GetString(bytes);
    }

    private static byte[] Build32BitBox(
        string type,
        byte[] payload)
    {
        if (type.Length != 4)
        {
            throw new ArgumentException(
                "Ein MP4-Atomtyp muss genau vier Zeichen besitzen.",
                nameof(type));
        }

        var typeBytes =
            Encoding.Latin1.GetBytes(type);

        if (typeBytes.Length != 4)
        {
            throw new ArgumentException(
                "Ein MP4-Atomtyp muss in vier Latin-1-Bytes passen.",
                nameof(type));
        }

        var size =
            checked(8 + payload.Length);

        var result =
            new byte[size];

        BinaryPrimitives.WriteUInt32BigEndian(
            result.AsSpan(0, 4),
            checked((uint)size));

        Buffer.BlockCopy(
            typeBytes,
            0,
            result,
            4,
            4);

        Buffer.BlockCopy(
            payload,
            0,
            result,
            8,
            payload.Length);

        return result;
    }

    private static byte[] Slice(
        byte[] source,
        int offset,
        int length)
    {
        var result =
            new byte[length];

        Buffer.BlockCopy(
            source,
            offset,
            result,
            0,
            length);

        return result;
    }

    private static byte[] Concat(
        params byte[][] parts)
    {
        return Concat(
            (IEnumerable<byte[]>)parts);
    }

    private static byte[] Concat(
        IEnumerable<byte[]> parts)
    {
        var materialized =
            parts.ToArray();

        var length =
            materialized.Sum(
                part => (long)part.Length);

        if (length > int.MaxValue)
        {
            throw new NotSupportedException(
                "Der resultierende MP4-Metadatenbereich ist zu groß.");
        }

        var result =
            new byte[(int)length];

        var offset = 0;

        foreach (var part in materialized)
        {
            Buffer.BlockCopy(
                part,
                0,
                result,
                offset,
                part.Length);

            offset += part.Length;
        }

        return result;
    }

    private static void ReadExactly(
        Stream stream,
        byte[] buffer)
    {
        ReadExactly(
            stream,
            buffer.AsSpan());
    }

    private static void ReadExactly(
        Stream stream,
        Span<byte> buffer)
    {
        var total = 0;

        while (total < buffer.Length)
        {
            var read =
                stream.Read(
                    buffer[total..]);

            if (read == 0)
            {
                throw new EndOfStreamException(
                    "Unerwartetes Dateiende beim Lesen eines MP4-Atoms.");
            }

            total += read;
        }
    }

    private static void CopyExactly(
        Stream input,
        Stream output,
        long length)
    {
        var buffer =
            new byte[128 * 1024];

        var remaining = length;

        while (remaining > 0)
        {
            var wanted =
                (int)Math.Min(
                    buffer.Length,
                    remaining);

            var read =
                input.Read(
                    buffer,
                    0,
                    wanted);

            if (read == 0)
            {
                throw new EndOfStreamException(
                    "Unerwartetes Dateiende beim Kopieren eines MP4-Atoms.");
            }

            output.Write(
                buffer,
                0,
                read);

            remaining -= read;
        }
    }

    private static bool RangesEqual(
        FileStream left,
        long leftOffset,
        FileStream right,
        long rightOffset,
        long length)
    {
        var leftBuffer =
            new byte[128 * 1024];

        var rightBuffer =
            new byte[128 * 1024];

        left.Position = leftOffset;
        right.Position = rightOffset;

        var remaining = length;

        while (remaining > 0)
        {
            var wanted =
                (int)Math.Min(
                    leftBuffer.Length,
                    remaining);

            var leftRead =
                left.Read(
                    leftBuffer,
                    0,
                    wanted);

            var rightRead =
                right.Read(
                    rightBuffer,
                    0,
                    wanted);

            if (leftRead != wanted ||
                rightRead != wanted)
            {
                throw new EndOfStreamException(
                    "Unerwartetes Dateiende beim MP4-Preservation-Vergleich.");
            }

            if (!leftBuffer
                    .AsSpan(0, wanted)
                    .SequenceEqual(
                        rightBuffer.AsSpan(0, wanted)))
            {
                return false;
            }

            remaining -= wanted;
        }

        return true;
    }

    private enum OwnedField
    {
        None,
        Track,
        Album
    }

    private readonly record struct BoxInfo(
        string Type,
        int Offset,
        int Size,
        int HeaderSize)
    {
        public int PayloadOffset =>
            checked(Offset + HeaderSize);

        public int PayloadLength =>
            checked(Size - HeaderSize);

        public int EndOffset =>
            checked(Offset + Size);
    }

    private readonly record struct TopLevelBox(
        string Type,
        long Offset,
        long Size,
        int HeaderSize);

    private sealed record FileLayout(
        IReadOnlyList<TopLevelBox> TopLevelBoxes,
        TopLevelBox MoovTopLevel,
        byte[] MoovBytes);

    private sealed record LocatedIlst(
        BoxInfo Udta,
        BoxInfo Meta,
        BoxInfo Ilst,
        byte[] BoxBytes);

    private sealed record ValidationContext(
        byte[] TargetIlst);

    private sealed record OwnedValues(
        IReadOnlyList<string> Track,
        IReadOnlyList<string> Album);
}
