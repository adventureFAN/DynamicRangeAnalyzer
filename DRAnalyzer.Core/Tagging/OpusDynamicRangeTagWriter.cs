using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace DRAnalyzer.Core.Tagging;

public static class OpusDynamicRangeTagWriter
{
    private const int MaximumCommentHeaderBytes =
        125_829_120;

    private const string TrackDynamicRangeField =
        "DYNAMIC RANGE";

    private const string AlbumDynamicRangeField =
        "ALBUM DYNAMIC RANGE";

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
                "Die Opus-Datei wurde nicht gefunden.",
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
                packet =>
                    OpusTagsEditor.UpdateDynamicRangeTags(
                        packet,
                        trackDynamicRange,
                        albumDynamicRange));

            // Noch bevor das Original ersetzt wird:
            //
            // - CRC aller Seiten prüfen
            // - OpusHead bytegenau vergleichen
            // - fremde Tags bytegenau vergleichen
            // - unbekannte OpusTags-Zusatzdaten vergleichen
            // - sämtliche Audioseiten außer Sequenz/CRC
            //   bytegenau vergleichen
            // - neue DR-Tags verifizieren
            ValidateModifiedCopy(
                fullPath,
                tempPath,
                (beforePacket, afterPacket) =>
                    ValidateTagPreservation(
                        beforePacket,
                        afterPacket,
                        trackDynamicRange,
                        albumDynamicRange));

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
                "Die Opus-Datei wurde nicht gefunden.",
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
            WriteModifiedCopy(
                fullPath,
                tempPath,
                OpusTagsEditor.RemoveDynamicRangeTags);

            ValidateModifiedCopy(
                fullPath,
                tempPath,
                ValidateTagRemovalPreservation);

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
        using var input =
            new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

        var header =
            ReadHeader(
                input);

        return
            OpusTagsEditor.HasDynamicRangeTags(
                header.OpusTagsPacket);
    }

    private static void WriteModifiedCopy(
        string sourcePath,
        string destinationPath,
        Func<byte[], byte[]> editTags)
    {
        using var input =
            new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

        var sourceHeader =
            ReadHeader(input);

        var modifiedTags =
            editTags(
                sourceHeader.OpusTagsPacket);

        var newCommentPages =
            OggOpusCommentPageBuilder.Build(
                modifiedTags,
                sourceHeader.StreamSerial,
                sourceHeader.FirstCommentSequence);

        using var output =
            new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);

        // OpusHead inklusive Ogg-Seitenheader und CRC
        // bleibt vollständig bytegenau.
        output.Write(
            sourceHeader.IdentificationPage);

        foreach (var page in newCommentPages)
        {
            output.Write(page);
        }

        var expectedSourceSequence =
            checked(
                sourceHeader.FirstCommentSequence +
                (uint)sourceHeader.CommentPageCount);

        var nextOutputSequence =
            checked(
                sourceHeader.FirstCommentSequence +
                (uint)newCommentPages.Count);

        var audioPageCount = 0;
        var sawEndOfStream = false;

        while (true)
        {
            var sourcePage =
                OggPageCodec.ReadRawPage(
                    input);

            if (sourcePage is null)
                break;

            if (sawEndOfStream)
            {
                throw new InvalidDataException(
                    "Die Ogg-Datei enthält Seiten " +
                    "nach dem End-of-Stream-Flag.");
            }

            ValidatePageChecksum(
                sourcePage);

            var info =
                ParsePage(sourcePage);

            if (info.StreamSerial !=
                sourceHeader.StreamSerial)
            {
                throw new InvalidDataException(
                    "Verkettete oder multiplexte Ogg-Streams " +
                    "werden für sicheres Tag-Schreiben " +
                    "noch nicht unterstützt.");
            }

            if (info.Sequence !=
                expectedSourceSequence)
            {
                throw new InvalidDataException(
                    "Die Ogg-Sequenznummern sind " +
                    "nicht fortlaufend.");
            }

            if ((info.HeaderType & 0x02) != 0)
            {
                throw new InvalidDataException(
                    "Unerwartetes Beginning-of-Stream-Flag " +
                    "innerhalb der Audioseiten.");
            }

            byte[] outputPage;

            if (info.Sequence ==
                nextOutputSequence)
            {
                // Wenn sich die Anzahl der Comment-Seiten
                // nicht geändert hat, bleibt die Audioseite
                // vollständig bytegenau erhalten.
                outputPage =
                    sourcePage;
            }
            else
            {
                // Nur Seitennummer ändern.
                // Danach muss die Seiten-CRC neu berechnet werden.
                outputPage =
                    WithPageSequence(
                        sourcePage,
                        nextOutputSequence);
            }

            output.Write(
                outputPage);

            audioPageCount++;

            if ((info.HeaderType & 0x04) != 0)
            {
                sawEndOfStream = true;
            }

            expectedSourceSequence =
                checked(
                    expectedSourceSequence + 1);

            nextOutputSequence =
                checked(
                    nextOutputSequence + 1);
        }

        if (audioPageCount == 0)
        {
            throw new InvalidDataException(
                "Die Opus-Datei enthält keine Audioseiten.");
        }

        output.Flush(
            flushToDisk: true);
    }

    private static void ValidateModifiedCopy(
        string sourcePath,
        string modifiedPath,
        Action<byte[], byte[]> validateTags)
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

        var before =
            ReadHeader(source);

        var after =
            ReadHeader(modified);

        if (!before.IdentificationPage
                .AsSpan()
                .SequenceEqual(
                    after.IdentificationPage))
        {
            throw new InvalidDataException(
                "Die OpusHead-Seite wurde verändert.");
        }

        if (before.StreamSerial !=
            after.StreamSerial)
        {
            throw new InvalidDataException(
                "Die Ogg-Stream-ID wurde verändert.");
        }

        validateTags(
            before.OpusTagsPacket,
            after.OpusTagsPacket);

        var expectedSourceSequence =
            checked(
                before.FirstCommentSequence +
                (uint)before.CommentPageCount);

        var expectedModifiedSequence =
            checked(
                after.FirstCommentSequence +
                (uint)after.CommentPageCount);

        var audioPageCount = 0;

        while (true)
        {
            var sourcePage =
                OggPageCodec.ReadRawPage(
                    source);

            var modifiedPage =
                OggPageCodec.ReadRawPage(
                    modified);

            if (sourcePage is null &&
                modifiedPage is null)
            {
                break;
            }

            if (sourcePage is null ||
                modifiedPage is null)
            {
                throw new InvalidDataException(
                    "Die Anzahl der Audioseiten wurde verändert.");
            }

            ValidatePageChecksum(
                sourcePage);

            ValidatePageChecksum(
                modifiedPage);

            var sourceInfo =
                ParsePage(sourcePage);

            var modifiedInfo =
                ParsePage(modifiedPage);

            if (sourceInfo.StreamSerial !=
                    before.StreamSerial ||
                modifiedInfo.StreamSerial !=
                    after.StreamSerial)
            {
                throw new InvalidDataException(
                    "Unerwartete Ogg-Stream-ID " +
                    "innerhalb der Audioseiten.");
            }

            if (sourceInfo.Sequence !=
                    expectedSourceSequence ||
                modifiedInfo.Sequence !=
                    expectedModifiedSequence)
            {
                throw new InvalidDataException(
                    "Unerwartete Ogg-Sequenznummer " +
                    "bei der Sicherheitsprüfung.");
            }

            // Bytes 18..21 = Page Sequence
            // Bytes 22..25 = CRC
            //
            // Nur diese acht Bytes dürfen sich
            // innerhalb einer Audioseite unterscheiden.
            if (sourcePage.Length !=
                modifiedPage.Length)
            {
                throw new InvalidDataException(
                    "Eine Audioseite hat ihre Länge verändert.");
            }

            if (!sourcePage
                    .AsSpan(0, 18)
                    .SequenceEqual(
                        modifiedPage.AsSpan(0, 18)) ||
                !sourcePage
                    .AsSpan(26)
                    .SequenceEqual(
                        modifiedPage.AsSpan(26)))
            {
                throw new InvalidDataException(
                    "Nutzdaten oder Struktur einer " +
                    "Audioseite wurden verändert.");
            }

            expectedSourceSequence =
                checked(
                    expectedSourceSequence + 1);

            expectedModifiedSequence =
                checked(
                    expectedModifiedSequence + 1);

            audioPageCount++;
        }

        if (audioPageCount == 0)
        {
            throw new InvalidDataException(
                "Bei der Sicherheitsprüfung wurden " +
                "keine Audioseiten gefunden.");
        }
    }

    private static OpusHeader ReadHeader(
        Stream stream)
    {
        var identificationPage =
            ReadRequiredPage(
                stream,
                "OpusHead-Seite");

        ValidatePageChecksum(
            identificationPage);

        var identificationInfo =
            ParsePage(
                identificationPage);

        if ((identificationInfo.HeaderType & 0x02) == 0)
        {
            throw new InvalidDataException(
                "Die erste Ogg-Seite besitzt " +
                "kein Beginning-of-Stream-Flag.");
        }

        if ((identificationInfo.HeaderType & 0x01) != 0)
        {
            throw new InvalidDataException(
                "Die erste Ogg-Seite ist unerwartet " +
                "als Paketfortsetzung markiert.");
        }

        if ((identificationInfo.HeaderType & 0x04) != 0)
        {
            throw new InvalidDataException(
                "Die erste Ogg-Seite besitzt bereits " +
                "das End-of-Stream-Flag.");
        }

        if (identificationInfo.GranulePosition != 0)
        {
            throw new InvalidDataException(
                "Die OpusHead-Seite besitzt " +
                "keine Granule Position 0.");
        }

        var identificationPacket =
            GetOnlyPacketFromPage(
                identificationInfo,
                "OpusHead");

        if (!identificationPacket
                .AsSpan()
                .StartsWith("OpusHead"u8))
        {
            throw new InvalidDataException(
                "Das erste Ogg-Paket ist kein OpusHead.");
        }

        var streamSerial =
            identificationInfo.StreamSerial;

        var expectedSequence =
            checked(
                identificationInfo.Sequence + 1);

        var firstCommentSequence =
            expectedSequence;

        using var commentPacket =
            new MemoryStream();

        var commentPageCount = 0;

        while (true)
        {
            var page =
                ReadRequiredPage(
                    stream,
                    "OpusTags-Seite");

            ValidatePageChecksum(
                page);

            var info =
                ParsePage(page);

            if (info.StreamSerial !=
                streamSerial)
            {
                throw new InvalidDataException(
                    "Mehrere oder multiplexte Ogg-Streams " +
                    "werden für sicheres Tag-Schreiben " +
                    "noch nicht unterstützt.");
            }

            if (info.Sequence !=
                expectedSequence)
            {
                throw new InvalidDataException(
                    "Die Ogg-Sequenznummern sind " +
                    "nicht fortlaufend.");
            }

            if ((info.HeaderType & 0x02) != 0 ||
                (info.HeaderType & 0x04) != 0)
            {
                throw new InvalidDataException(
                    "Ungültiges BOS/EOS-Flag " +
                    "innerhalb des OpusTags-Headers.");
            }

            var continued =
                (info.HeaderType & 0x01) != 0;

            if (commentPageCount == 0)
            {
                if (continued)
                {
                    throw new InvalidDataException(
                        "Die erste OpusTags-Seite ist " +
                        "unerwartet als Paketfortsetzung markiert.");
                }
            }
            else if (!continued)
            {
                throw new InvalidDataException(
                    "Eine fortgesetzte OpusTags-Seite besitzt " +
                    "kein Continued-Packet-Flag.");
            }

            ValidateSinglePacketPageSegments(
                info.LacingValues,
                "OpusTags");

            commentPacket.Write(
                info.Body);

            if (commentPacket.Length >
                MaximumCommentHeaderBytes)
            {
                throw new InvalidDataException(
                    "Der OpusTags-Header ist zu groß.");
            }

            commentPageCount++;

            var packetCompletes =
                info.LacingValues[^1] < 255;

            if (packetCompletes)
            {
                if (info.GranulePosition != 0)
                {
                    throw new InvalidDataException(
                        "Die abschließende OpusTags-Seite " +
                        "besitzt keine Granule Position 0.");
                }

                break;
            }

            if (info.GranulePosition != -1)
            {
                throw new InvalidDataException(
                    "Eine vollständig vom OpusTags-Paket " +
                    "überspannte Seite besitzt " +
                    "keine Granule Position -1.");
            }

            expectedSequence =
                checked(
                    expectedSequence + 1);
        }

        var opusTagsPacket =
            commentPacket.ToArray();

        if (!opusTagsPacket
                .AsSpan()
                .StartsWith("OpusTags"u8))
        {
            throw new InvalidDataException(
                "Das zweite Ogg-Paket ist kein OpusTags.");
        }

        return new OpusHeader(
            identificationPage,
            streamSerial,
            firstCommentSequence,
            commentPageCount,
            opusTagsPacket);
    }

    private static byte[] GetOnlyPacketFromPage(
        OggPageInfo page,
        string packetName)
    {
        ValidateSinglePacketPageSegments(
            page.LacingValues,
            packetName);

        if (page.LacingValues[^1] == 255)
        {
            throw new InvalidDataException(
                $"{packetName} endet nicht auf derselben Ogg-Seite.");
        }

        return page.Body;
    }

    private static void ValidateSinglePacketPageSegments(
        byte[] lacingValues,
        string packetName)
    {
        if (lacingValues.Length == 0)
        {
            throw new InvalidDataException(
                $"{packetName}-Seite enthält keine Segmente.");
        }

        for (var index = 0;
             index < lacingValues.Length - 1;
             index++)
        {
            if (lacingValues[index] != 255)
            {
                throw new InvalidDataException(
                    $"{packetName} endet vor dem Ende " +
                    "seiner Ogg-Seite.");
            }
        }
    }

    private static void ValidateTagRemovalPreservation(
        byte[] beforePacket,
        byte[] afterPacket)
    {
        var before =
            ParseOpusTags(
                beforePacket);

        var after =
            ParseOpusTags(
                afterPacket);

        if (!before.Vendor
                .AsSpan()
                .SequenceEqual(
                    after.Vendor))
        {
            throw new InvalidDataException(
                "Der OpusTags-Vendor wurde verändert.");
        }

        if (!before.TrailingData
                .AsSpan()
                .SequenceEqual(
                    after.TrailingData))
        {
            throw new InvalidDataException(
                "Binäre Zusatzdaten hinter OpusTags " +
                "wurden verändert.");
        }

        var beforeProtected =
            before.Comments
                .Where(
                    comment =>
                        !IsOwnedDynamicRangeField(
                            comment))
                .ToArray();

        var afterProtected =
            after.Comments
                .Where(
                    comment =>
                        !IsOwnedDynamicRangeField(
                            comment))
                .ToArray();

        if (beforeProtected.Length !=
            afterProtected.Length)
        {
            throw new InvalidDataException(
                "Die Anzahl fremder bzw. geschützter " +
                "Opus-Tags wurde beim Entfernen verändert.");
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
                    $"Geschützter Opus-Tag {index} " +
                    "wurde beim Entfernen verändert.");
            }
        }

        if (after.Comments.Any(
                IsOwnedDynamicRangeField))
        {
            throw new InvalidDataException(
                "Mindestens ein DR-Tag ist nach dem Entfernen " +
                "noch in den OpusTags vorhanden.");
        }
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

    private static void ValidateTagPreservation(
        byte[] beforePacket,
        byte[] afterPacket,
        int expectedTrackDynamicRange,
        int? expectedAlbumDynamicRange)
    {
        var before =
            ParseOpusTags(
                beforePacket);

        var after =
            ParseOpusTags(
                afterPacket);

        if (!before.Vendor
                .AsSpan()
                .SequenceEqual(
                    after.Vendor))
        {
            throw new InvalidDataException(
                "Der OpusTags-Vendor wurde verändert.");
        }

        if (!before.TrailingData
                .AsSpan()
                .SequenceEqual(
                    after.TrailingData))
        {
            throw new InvalidDataException(
                "Binäre Zusatzdaten hinter OpusTags " +
                "wurden verändert.");
        }

        var beforeProtected =
            before.Comments
                .Where(
                    comment =>
                        !IsMutableOwnedField(
                            comment,
                            expectedAlbumDynamicRange))
                .ToArray();

        var afterProtected =
            after.Comments
                .Where(
                    comment =>
                        !IsMutableOwnedField(
                            comment,
                            expectedAlbumDynamicRange))
                .ToArray();

        if (beforeProtected.Length !=
            afterProtected.Length)
        {
            throw new InvalidDataException(
                "Die Anzahl fremder bzw. geschützter " +
                "Opus-Tags wurde verändert.");
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
                    $"Geschützter Opus-Tag {index} " +
                    "wurde verändert.");
            }
        }

        ValidateSingleOwnedValue(
            after.Comments,
            TrackDynamicRangeField,
            expectedTrackDynamicRange);

        if (expectedAlbumDynamicRange.HasValue)
        {
            ValidateSingleOwnedValue(
                after.Comments,
                AlbumDynamicRangeField,
                expectedAlbumDynamicRange.Value);
        }
    }

    private static bool IsMutableOwnedField(
        byte[] comment,
        int? albumDynamicRange)
    {
        if (IsField(
                comment,
                TrackDynamicRangeField))
        {
            return true;
        }

        return
            albumDynamicRange.HasValue &&
            IsField(
                comment,
                AlbumDynamicRangeField);
    }

    private static void ValidateSingleOwnedValue(
        IReadOnlyList<byte[]> comments,
        string fieldName,
        int expectedValue)
    {
        var matches =
            comments
                .Where(
                    comment =>
                        IsField(
                            comment,
                            fieldName))
                .ToArray();

        if (matches.Length != 1)
        {
            throw new InvalidDataException(
                $"{fieldName} ist nach dem Schreiben " +
                "nicht eindeutig vorhanden.");
        }

        var actual =
            GetFieldValue(
                matches[0]);

        var expected =
            expectedValue.ToString(
                CultureInfo.InvariantCulture);

        if (!string.Equals(
                actual,
                expected,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"{fieldName} besitzt nach dem Schreiben " +
                "nicht den erwarteten Wert.");
        }
    }

    private static ParsedOpusTags ParseOpusTags(
        byte[] packet)
    {
        if (!packet
                .AsSpan()
                .StartsWith("OpusTags"u8))
        {
            throw new InvalidDataException(
                "Kein gültiges OpusTags-Paket.");
        }

        var offset = 8;

        var vendorLength =
            ReadUInt32(
                packet,
                ref offset);

        var vendor =
            ReadBytes(
                packet,
                ref offset,
                vendorLength);

        var commentCount =
            ReadUInt32(
                packet,
                ref offset);

        var comments =
            new List<byte[]>();

        for (uint index = 0;
             index < commentCount;
             index++)
        {
            var length =
                ReadUInt32(
                    packet,
                    ref offset);

            comments.Add(
                ReadBytes(
                    packet,
                    ref offset,
                    length));
        }

        var trailing =
            packet
                .AsSpan(offset)
                .ToArray();

        return new ParsedOpusTags(
            vendor,
            comments,
            trailing);
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

    private static uint ReadUInt32(
        byte[] data,
        ref int offset)
    {
        if (offset > data.Length - 4)
        {
            throw new InvalidDataException(
                "Beschädigtes OpusTags-Paket.");
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
                "Ungültige OpusTags-Länge.");
        }

        var intLength =
            (int)length;

        if (offset >
            data.Length - intLength)
        {
            throw new InvalidDataException(
                "Beschädigtes OpusTags-Paket.");
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

    private static byte[] ReadRequiredPage(
        Stream stream,
        string description)
    {
        return
            OggPageCodec.ReadRawPage(stream)
            ?? throw new InvalidDataException(
                $"Die erwartete {description} fehlt.");
    }

    private static void ValidatePageChecksum(
        byte[] page)
    {
        if (!OggPageCodec.HasValidChecksum(
                page))
        {
            throw new InvalidDataException(
                "Eine Ogg-Seite besitzt eine ungültige CRC.");
        }
    }

    private static OggPageInfo ParsePage(
        byte[] page)
    {
        var segmentCount =
            page[26];

        var lacing =
            page
                .AsSpan(
                    27,
                    segmentCount)
                .ToArray();

        var bodyOffset =
            27 +
            segmentCount;

        var body =
            page
                .AsSpan(
                    bodyOffset)
                .ToArray();

        return new OggPageInfo(
            page[5],
            BinaryPrimitives.ReadInt64LittleEndian(
                page.AsSpan(
                    6,
                    8)),
            BinaryPrimitives.ReadUInt32LittleEndian(
                page.AsSpan(
                    14,
                    4)),
            BinaryPrimitives.ReadUInt32LittleEndian(
                page.AsSpan(
                    18,
                    4)),
            lacing,
            body);
    }

    private static byte[] WithPageSequence(
        byte[] page,
        uint sequence)
    {
        var result =
            page.ToArray();

        BinaryPrimitives.WriteUInt32LittleEndian(
            result.AsSpan(
                18,
                4),
            sequence);

        return
            OggPageCodec.WithRecalculatedChecksum(
                result);
    }

    private sealed record OggPageInfo(
        byte HeaderType,
        long GranulePosition,
        uint StreamSerial,
        uint Sequence,
        byte[] LacingValues,
        byte[] Body);

    private sealed record OpusHeader(
        byte[] IdentificationPage,
        uint StreamSerial,
        uint FirstCommentSequence,
        int CommentPageCount,
        byte[] OpusTagsPacket);

    private sealed record ParsedOpusTags(
        byte[] Vendor,
        IReadOnlyList<byte[]> Comments,
        byte[] TrailingData);
}
