using System.Buffers.Binary;
using System.Text;

namespace DRAnalyzer.Core.Tagging;

public static class VorbisDynamicRangeTagWriter
{
    private const int MaximumHeaderPacketBytes =
        125_829_120;

    private const string TrackDynamicRangeField =
        "DYNAMIC RANGE";

    private const string AlbumDynamicRangeField =
        "ALBUM DYNAMIC RANGE";

    private static ReadOnlySpan<byte> IdentificationSignature =>
        new byte[] { 0x01, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s' };

    private static ReadOnlySpan<byte> CommentSignature =>
        new byte[] { 0x03, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s' };

    private static ReadOnlySpan<byte> SetupSignature =>
        new byte[] { 0x05, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s' };

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
                "Die Ogg-Vorbis-Datei wurde nicht gefunden.",
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
                    VorbisCommentEditor.UpdateDynamicRangeTags(
                        packet,
                        trackDynamicRange,
                        albumDynamicRange));

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
                "Die Ogg-Vorbis-Datei wurde nicht gefunden.",
                filePath);
        }

        var fullPath =
            Path.GetFullPath(filePath);

        if (!HasOwnedDynamicRangeTags(fullPath))
            return;

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
                VorbisCommentEditor.RemoveDynamicRangeTags);

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
            ReadHeader(input);

        return
            VorbisCommentEditor.HasDynamicRangeTags(
                header.CommentPacket);
    }

    private static void WriteModifiedCopy(
        string sourcePath,
        string destinationPath,
        Func<byte[], byte[]> editComment)
    {
        using var input =
            new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

        var sourceHeader =
            ReadHeader(input);

        var modifiedComment =
            editComment(
                sourceHeader.CommentPacket);

        var newHeaderPages =
            OggVorbisHeaderPageBuilder.Build(
                modifiedComment,
                sourceHeader.SetupPacket,
                sourceHeader.StreamSerial,
                sourceHeader.FirstHeaderSequence);

        using var output =
            new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);

        // Die Identification-Seite bleibt vollständig bytegenau.
        output.Write(
            sourceHeader.IdentificationPage);

        foreach (var page in newHeaderPages)
        {
            output.Write(page);
        }

        var expectedSourceSequence =
            checked(
                sourceHeader.FirstHeaderSequence +
                (uint)sourceHeader.HeaderPageCount);

        var nextOutputSequence =
            checked(
                sourceHeader.FirstHeaderSequence +
                (uint)newHeaderPages.Count);

        var audioPageCount = 0;
        var sawEndOfStream = false;

        while (true)
        {
            var sourcePage =
                OggPageCodec.ReadRawPage(input);

            if (sourcePage is null)
                break;

            if (sawEndOfStream)
            {
                throw new InvalidDataException(
                    "Die Ogg-Datei enthält Seiten nach dem " +
                    "End-of-Stream-Flag.");
            }

            ValidatePageChecksum(sourcePage);

            var info =
                ParsePage(sourcePage);

            if (info.StreamSerial !=
                sourceHeader.StreamSerial)
            {
                throw new InvalidDataException(
                    "Verkettete oder multiplexte Ogg-Streams " +
                    "werden für sicheres Vorbis-Tag-Schreiben " +
                    "noch nicht unterstützt.");
            }

            if (info.Sequence !=
                expectedSourceSequence)
            {
                throw new InvalidDataException(
                    "Die Ogg-Sequenznummern sind nicht fortlaufend.");
            }

            if ((info.HeaderType & 0x02) != 0)
            {
                throw new InvalidDataException(
                    "Unerwartetes Beginning-of-Stream-Flag " +
                    "innerhalb der Vorbis-Audioseiten.");
            }

            if (audioPageCount == 0 &&
                (info.HeaderType & 0x01) != 0)
            {
                throw new InvalidDataException(
                    "Das erste Vorbis-Audiopaket beginnt nicht " +
                    "auf einer frischen Ogg-Seite.");
            }

            byte[] outputPage;

            if (info.Sequence ==
                nextOutputSequence)
            {
                outputPage =
                    sourcePage;
            }
            else
            {
                outputPage =
                    WithPageSequence(
                        sourcePage,
                        nextOutputSequence);
            }

            output.Write(outputPage);

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
                "Die Ogg-Vorbis-Datei enthält keine Audioseiten.");
        }

        output.Flush(
            flushToDisk: true);
    }

    private static void ValidateModifiedCopy(
        string sourcePath,
        string modifiedPath,
        Action<byte[], byte[]> validateComment)
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
                "Die Vorbis-Identification-Seite wurde verändert.");
        }

        if (before.StreamSerial !=
            after.StreamSerial)
        {
            throw new InvalidDataException(
                "Die Ogg-Stream-ID wurde verändert.");
        }

        if (!before.SetupPacket
                .AsSpan()
                .SequenceEqual(
                    after.SetupPacket))
        {
            throw new InvalidDataException(
                "Der Vorbis-Setup-Header wurde verändert.");
        }

        validateComment(
            before.CommentPacket,
            after.CommentPacket);

        var expectedSourceSequence =
            checked(
                before.FirstHeaderSequence +
                (uint)before.HeaderPageCount);

        var expectedModifiedSequence =
            checked(
                after.FirstHeaderSequence +
                (uint)after.HeaderPageCount);

        var audioPageCount = 0;

        while (true)
        {
            var sourcePage =
                OggPageCodec.ReadRawPage(source);

            var modifiedPage =
                OggPageCodec.ReadRawPage(modified);

            if (sourcePage is null &&
                modifiedPage is null)
            {
                break;
            }

            if (sourcePage is null ||
                modifiedPage is null)
            {
                throw new InvalidDataException(
                    "Die Anzahl der Vorbis-Audioseiten wurde verändert.");
            }

            ValidatePageChecksum(sourcePage);
            ValidatePageChecksum(modifiedPage);

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
                    "Unerwartete Ogg-Stream-ID innerhalb " +
                    "der Vorbis-Audioseiten.");
            }

            if (sourceInfo.Sequence !=
                    expectedSourceSequence ||
                modifiedInfo.Sequence !=
                    expectedModifiedSequence)
            {
                throw new InvalidDataException(
                    "Unerwartete Ogg-Sequenznummer bei " +
                    "der Vorbis-Sicherheitsprüfung.");
            }

            if (sourcePage.Length !=
                modifiedPage.Length)
            {
                throw new InvalidDataException(
                    "Eine Vorbis-Audioseite hat ihre Länge verändert.");
            }

            // Nur Sequence (18..21) und CRC (22..25) dürfen sich ändern.
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
                    "Vorbis-Audioseite wurden verändert.");
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
                "Bei der Vorbis-Sicherheitsprüfung wurden " +
                "keine Audioseiten gefunden.");
        }
    }

    private static VorbisHeader ReadHeader(
        Stream stream)
    {
        var identificationPage =
            ReadRequiredPage(
                stream,
                "Vorbis-Identification-Seite");

        ValidatePageChecksum(
            identificationPage);

        var identificationInfo =
            ParsePage(
                identificationPage);

        if ((identificationInfo.HeaderType & 0x02) == 0)
        {
            throw new InvalidDataException(
                "Die erste Ogg-Seite besitzt kein " +
                "Beginning-of-Stream-Flag.");
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
                "Die Vorbis-Identification-Seite besitzt " +
                "keine Granule Position 0.");
        }

        var identificationPacket =
            GetOnlyPacketFromPage(
                identificationInfo,
                "Vorbis-Identification");

        if (!identificationPacket
                .AsSpan()
                .StartsWith(
                    IdentificationSignature))
        {
            throw new InvalidDataException(
                "Das erste Ogg-Paket ist kein " +
                "Vorbis-Identification-Header.");
        }

        var streamSerial =
            identificationInfo.StreamSerial;

        var firstHeaderSequence =
            checked(
                identificationInfo.Sequence + 1);

        var expectedSequence =
            firstHeaderSequence;

        var packets =
            new List<byte[]>();

        using var currentPacket =
            new MemoryStream();

        var packetContinuesFromPreviousPage =
            false;

        var headerPageCount = 0;

        while (packets.Count < 2)
        {
            var page =
                ReadRequiredPage(
                    stream,
                    "Vorbis-Comment-/Setup-Seite");

            ValidatePageChecksum(page);

            var info =
                ParsePage(page);

            if (info.StreamSerial !=
                streamSerial)
            {
                throw new InvalidDataException(
                    "Mehrere oder multiplexte Ogg-Streams " +
                    "werden für sicheres Vorbis-Tag-Schreiben " +
                    "noch nicht unterstützt.");
            }

            if (info.Sequence !=
                expectedSequence)
            {
                throw new InvalidDataException(
                    "Die Ogg-Sequenznummern sind nicht fortlaufend.");
            }

            if ((info.HeaderType & 0x02) != 0 ||
                (info.HeaderType & 0x04) != 0)
            {
                throw new InvalidDataException(
                    "Ungültiges BOS/EOS-Flag innerhalb " +
                    "der Vorbis-Headerseiten.");
            }

            var continued =
                (info.HeaderType & 0x01) != 0;

            if (continued !=
                packetContinuesFromPreviousPage)
            {
                throw new InvalidDataException(
                    "Das Continued-Packet-Flag der " +
                    "Vorbis-Headerseiten ist inkonsistent.");
            }

            if (info.LacingValues.Length == 0)
            {
                throw new InvalidDataException(
                    "Eine Vorbis-Headerseite enthält keine Segmente.");
            }

            var completesPacketOnPage =
                info.LacingValues.Any(
                    value => value < 255);

            var expectedGranulePosition =
                completesPacketOnPage
                    ? 0L
                    : -1L;

            if (info.GranulePosition !=
                expectedGranulePosition)
            {
                throw new InvalidDataException(
                    completesPacketOnPage
                        ? "Eine Vorbis-Headerseite, auf der ein Headerpaket endet, besitzt keine Granule Position 0."
                        : "Eine fortgesetzte Vorbis-Headerseite ohne Paketende besitzt nicht die Granule Position -1.");
            }

            var bodyOffset = 0;

            for (var index = 0;
                 index < info.LacingValues.Length;
                 index++)
            {
                var segmentLength =
                    info.LacingValues[index];

                currentPacket.Write(
                    info.Body,
                    bodyOffset,
                    segmentLength);

                bodyOffset +=
                    segmentLength;

                if (currentPacket.Length >
                    MaximumHeaderPacketBytes)
                {
                    throw new InvalidDataException(
                        "Ein Vorbis-Headerpaket ist zu groß.");
                }

                if (segmentLength < 255)
                {
                    packets.Add(
                        currentPacket.ToArray());

                    currentPacket.SetLength(0);

                    packetContinuesFromPreviousPage =
                        false;

                    if (packets.Count == 2)
                    {
                        // Der Setup-Header muss die Seite abschließen;
                        // das erste Audiopaket beginnt auf einer frischen Seite.
                        if (index !=
                            info.LacingValues.Length - 1)
                        {
                            throw new InvalidDataException(
                                "Der Vorbis-Setup-Header beendet " +
                                "seine Ogg-Seite nicht.");
                        }

                        break;
                    }
                }
                else
                {
                    packetContinuesFromPreviousPage =
                        true;
                }
            }

            if (bodyOffset !=
                info.Body.Length)
            {
                throw new InvalidDataException(
                    "Die Vorbis-Headerseite konnte nicht " +
                    "vollständig in Pakete zerlegt werden.");
            }

            headerPageCount++;

            expectedSequence =
                checked(
                    expectedSequence + 1);
        }

        if (packets.Count != 2)
        {
            throw new InvalidDataException(
                "Vorbis-Comment- oder Setup-Header fehlt.");
        }

        var commentPacket =
            packets[0];

        var setupPacket =
            packets[1];

        if (!commentPacket
                .AsSpan()
                .StartsWith(CommentSignature))
        {
            throw new InvalidDataException(
                "Das zweite Vorbis-Paket ist kein Comment-Header.");
        }

        if (!setupPacket
                .AsSpan()
                .StartsWith(SetupSignature))
        {
            throw new InvalidDataException(
                "Das dritte Vorbis-Paket ist kein Setup-Header.");
        }

        return new VorbisHeader(
            identificationPage,
            streamSerial,
            firstHeaderSequence,
            headerPageCount,
            commentPacket,
            setupPacket);
    }

    private static byte[] GetOnlyPacketFromPage(
        OggPageInfo page,
        string packetName)
    {
        if (page.LacingValues.Length == 0)
        {
            throw new InvalidDataException(
                $"{packetName}-Seite enthält keine Segmente.");
        }

        for (var index = 0;
             index < page.LacingValues.Length - 1;
             index++)
        {
            if (page.LacingValues[index] != 255)
            {
                throw new InvalidDataException(
                    $"{packetName} endet vor dem Ende seiner Ogg-Seite.");
            }
        }

        if (page.LacingValues[^1] == 255)
        {
            throw new InvalidDataException(
                $"{packetName} endet nicht auf derselben Ogg-Seite.");
        }

        return page.Body;
    }

    private static void ValidateTagRemovalPreservation(
        byte[] beforePacket,
        byte[] afterPacket)
    {
        var before =
            ParseVorbisComment(
                beforePacket);

        var after =
            ParseVorbisComment(
                afterPacket);

        AssertVendorAndTrailingPreserved(
            before,
            after);

        var beforeProtected =
            before.Comments
                .Where(
                    comment =>
                        !IsOwnedDynamicRangeField(comment))
                .ToArray();

        var afterProtected =
            after.Comments
                .Where(
                    comment =>
                        !IsOwnedDynamicRangeField(comment))
                .ToArray();

        AssertProtectedCommentsEqual(
            beforeProtected,
            afterProtected,
            "beim Entfernen");

        if (after.Comments.Any(
                IsOwnedDynamicRangeField))
        {
            throw new InvalidDataException(
                "Mindestens ein DR-Tag ist nach dem Entfernen " +
                "noch im Vorbis-Comment-Header vorhanden.");
        }
    }

    private static void ValidateTagPreservation(
        byte[] beforePacket,
        byte[] afterPacket,
        int expectedTrackDynamicRange,
        int? expectedAlbumDynamicRange)
    {
        var before =
            ParseVorbisComment(
                beforePacket);

        var after =
            ParseVorbisComment(
                afterPacket);

        AssertVendorAndTrailingPreserved(
            before,
            after);

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

        AssertProtectedCommentsEqual(
            beforeProtected,
            afterProtected,
            "beim Schreiben");

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

    private static void AssertVendorAndTrailingPreserved(
        ParsedVorbisComment before,
        ParsedVorbisComment after)
    {
        if (!before.Vendor
                .AsSpan()
                .SequenceEqual(
                    after.Vendor))
        {
            throw new InvalidDataException(
                "Der Vorbis-Comment-Vendor wurde verändert.");
        }

        if (!before.TrailingData
                .AsSpan()
                .SequenceEqual(
                    after.TrailingData))
        {
            throw new InvalidDataException(
                "Das Vorbis-Comment-Framing-/Trailing-Ende " +
                "wurde verändert.");
        }
    }

    private static void AssertProtectedCommentsEqual(
        byte[][] before,
        byte[][] after,
        string operation)
    {
        if (before.Length !=
            after.Length)
        {
            throw new InvalidDataException(
                $"Die Anzahl geschützter Vorbis-Kommentare wurde {operation} verändert.");
        }

        for (var index = 0;
             index < before.Length;
             index++)
        {
            if (!before[index]
                    .AsSpan()
                    .SequenceEqual(
                        after[index]))
            {
                throw new InvalidDataException(
                    $"Geschützter Vorbis-Kommentar {index} wurde {operation} verändert.");
            }
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
        var values =
            comments
                .Where(
                    comment =>
                        IsField(
                            comment,
                            fieldName))
                .Select(GetFieldValue)
                .ToArray();

        var expected =
            expectedValue.ToString();

        if (values.Length != 1 ||
            values[0] != expected)
        {
            throw new InvalidDataException(
                $"{fieldName} konnte nach dem Schreiben " +
                "nicht eindeutig verifiziert werden.");
        }
    }

    private static ParsedVorbisComment ParseVorbisComment(
        byte[] packet)
    {
        if (!packet
                .AsSpan()
                .StartsWith(CommentSignature))
        {
            throw new InvalidDataException(
                "Kein Vorbis-Comment-Header.");
        }

        var offset =
            CommentSignature.Length;

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

        if (offset >= packet.Length)
        {
            throw new InvalidDataException(
                "Vorbis-Comment-Framing-Bit fehlt.");
        }

        var trailingData =
            packet
                .AsSpan(offset)
                .ToArray();

        if ((trailingData[0] & 0x01) == 0)
        {
            throw new InvalidDataException(
                "Vorbis-Comment-Framing-Bit ist nicht gesetzt.");
        }

        return new ParsedVorbisComment(
            vendor,
            comments,
            trailingData);
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

    private static uint ReadUInt32(
        byte[] data,
        ref int offset)
    {
        if (offset >
            data.Length - 4)
        {
            throw new InvalidDataException(
                "Ungültiger Vorbis-Comment-Header.");
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
                "Ungültige Vorbis-Comment-Länge.");
        }

        var intLength =
            (int)length;

        if (offset >
            data.Length - intLength)
        {
            throw new InvalidDataException(
                "Beschädigter Vorbis-Comment-Header.");
        }

        var result =
            data
                .AsSpan(
                    offset,
                    intLength)
                .ToArray();

        offset +=
            intLength;

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
        if (!OggPageCodec.HasValidChecksum(page))
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
            27 + segmentCount;

        var body =
            page
                .AsSpan(bodyOffset)
                .ToArray();

        return new OggPageInfo(
            page[5],
            BinaryPrimitives.ReadInt64LittleEndian(
                page.AsSpan(6, 8)),
            BinaryPrimitives.ReadUInt32LittleEndian(
                page.AsSpan(14, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(
                page.AsSpan(18, 4)),
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
            result.AsSpan(18, 4),
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

    private sealed record VorbisHeader(
        byte[] IdentificationPage,
        uint StreamSerial,
        uint FirstHeaderSequence,
        int HeaderPageCount,
        byte[] CommentPacket,
        byte[] SetupPacket);

    private sealed record ParsedVorbisComment(
        byte[] Vendor,
        IReadOnlyList<byte[]> Comments,
        byte[] TrailingData);
}
