using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using DRAnalyzer.Core.Tagging;

namespace DRAnalyzer.Tests;

[Trait("Category", "ExternalReference")]
public sealed class ManualRealOpusPreservationTests
{
    [Fact]
    public void WrittenCopy_PreservesEverythingExceptOwnedDrTags()
    {
        var originalPath =
            Environment.GetEnvironmentVariable(
                "DRANALYZER_MANUAL_OPUS_ORIGINAL");

        var copyPath =
            Environment.GetEnvironmentVariable(
                "DRANALYZER_MANUAL_OPUS_COPY");

        var expectedOriginalHash =
            Environment.GetEnvironmentVariable(
                "DRANALYZER_MANUAL_OPUS_ORIGINAL_SHA256");

        Assert.False(
            string.IsNullOrWhiteSpace(originalPath),
            "DRANALYZER_MANUAL_OPUS_ORIGINAL ist nicht gesetzt.");

        Assert.False(
            string.IsNullOrWhiteSpace(copyPath),
            "DRANALYZER_MANUAL_OPUS_COPY ist nicht gesetzt.");

        Assert.False(
            string.IsNullOrWhiteSpace(expectedOriginalHash),
            "DRANALYZER_MANUAL_OPUS_ORIGINAL_SHA256 ist nicht gesetzt.");

        Assert.True(
            File.Exists(originalPath),
            $"Original fehlt: {originalPath}");

        Assert.True(
            File.Exists(copyPath),
            $"Testkopie fehlt: {copyPath}");

        Assert.False(
            string.Equals(
                Path.GetFullPath(originalPath),
                Path.GetFullPath(copyPath),
                StringComparison.OrdinalIgnoreCase),
            "Original und Testkopie dürfen nicht dieselbe Datei sein.");

        // Beweist, dass das Original seit Beginn
        // dieses Testlaufs nicht verändert wurde.
        var currentOriginalHash =
            CalculateSha256(
                originalPath);

        Assert.Equal(
            expectedOriginalHash,
            currentOriginalHash,
            ignoreCase: true);

        var original =
            ReadOpusFile(
                originalPath);

        var modified =
            ReadOpusFile(
                copyPath);

        // ----------------------------------------------------
        // Ogg-/Opus-Grundstruktur
        // ----------------------------------------------------

        Assert.Equal(
            original.StreamSerial,
            modified.StreamSerial);

        Assert.Equal(
            original.Pages.Count -
            original.CommentPageCount,
            modified.Pages.Count -
            modified.CommentPageCount);

        // Die komplette OpusHead-Seite muss bytegenau
        // identisch geblieben sein.
        AssertBytesEqual(
            original.Pages[0].Raw,
            modified.Pages[0].Raw,
            "Die OpusHead-Ogg-Seite wurde verändert.");

        AssertBytesEqual(
            original.Packets[0].Data,
            modified.Packets[0].Data,
            "Das OpusHead-Paket wurde verändert.");

        // ----------------------------------------------------
        // OpusTags
        // ----------------------------------------------------

        var beforeTags =
            ParseOpusTags(
                original.Packets[1].Data);

        var afterTags =
            ParseOpusTags(
                modified.Packets[1].Data);

        AssertBytesEqual(
            beforeTags.Vendor,
            afterTags.Vendor,
            "Der OpusTags-Vendor wurde verändert.");

        AssertBytesEqual(
            beforeTags.TrailingData,
            afterTags.TrailingData,
            "Trailing Data hinter den OpusTags wurde verändert.");

        var beforeForeign =
            beforeTags.Comments
                .Where(
                    comment =>
                        !IsOwnedDrField(comment))
                .ToArray();

        var afterForeign =
            afterTags.Comments
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
            AssertBytesEqual(
                beforeForeign[index],
                afterForeign[index],
                $"Fremder Opus-Comment {index} wurde verändert.");
        }

        Assert.Equal(
            "20",
            GetSingleFieldValue(
                afterTags.Comments,
                "DYNAMIC RANGE"));

        Assert.Equal(
            "21",
            GetSingleFieldValue(
                afterTags.Comments,
                "ALBUM DYNAMIC RANGE"));

        Assert.Single(
            afterTags.Comments,
            comment =>
                IsField(
                    comment,
                    "DYNAMIC RANGE"));

        Assert.Single(
            afterTags.Comments,
            comment =>
                IsField(
                    comment,
                    "ALBUM DYNAMIC RANGE"));

        // ----------------------------------------------------
        // Audio-Pakete
        // ----------------------------------------------------

        var originalAudioPackets =
            original.Packets
                .Skip(2)
                .ToArray();

        var modifiedAudioPackets =
            modified.Packets
                .Skip(2)
                .ToArray();

        Assert.NotEmpty(
            originalAudioPackets);

        Assert.Equal(
            originalAudioPackets.Length,
            modifiedAudioPackets.Length);

        for (var index = 0;
             index < originalAudioPackets.Length;
             index++)
        {
            AssertBytesEqual(
                originalAudioPackets[index].Data,
                modifiedAudioPackets[index].Data,
                $"Audio-Paket {index} wurde verändert.");
        }

        // ----------------------------------------------------
        // Audio-Ogg-Seiten
        // ----------------------------------------------------

        var originalAudioPages =
            original.Pages
                .Skip(
                    original.Packets[1].EndPageIndex + 1)
                .ToArray();

        var modifiedAudioPages =
            modified.Pages
                .Skip(
                    modified.Packets[1].EndPageIndex + 1)
                .ToArray();

        Assert.Equal(
            originalAudioPages.Length,
            modifiedAudioPages.Length);

        var expectedSequenceDelta =
            modified.CommentPageCount -
            original.CommentPageCount;

        for (var index = 0;
             index < originalAudioPages.Length;
             index++)
        {
            var before =
                originalAudioPages[index];

            var after =
                modifiedAudioPages[index];

            Assert.Equal(
                before.Raw.Length,
                after.Raw.Length);

            // Capture Pattern, Version, Flags,
            // Granule Position und Serial müssen gleich sein.
            AssertBytesEqual(
                before.Raw.AsSpan(0, 18).ToArray(),
                after.Raw.AsSpan(0, 18).ToArray(),
                $"Audioseite {index}: Headerdaten wurden verändert.");

            // Segmenttabelle + kompletter Seiteninhalt
            // müssen bytegenau identisch sein.
            AssertBytesEqual(
                before.Raw.AsSpan(26).ToArray(),
                after.Raw.AsSpan(26).ToArray(),
                $"Audioseite {index}: Lacing oder Nutzdaten wurden verändert.");

            var actualSequenceDelta =
                (long)after.Sequence -
                before.Sequence;

            Assert.Equal(
                expectedSequenceDelta,
                actualSequenceDelta);
        }

        Console.WriteLine(
            $"Original Comment-Seiten: {original.CommentPageCount}");

        Console.WriteLine(
            $"Neue Comment-Seiten: {modified.CommentPageCount}");

        Console.WriteLine(
            $"Audio-Seiten: {originalAudioPages.Length}");

        Console.WriteLine(
            $"Audio-Pakete: {originalAudioPackets.Length}");

        Console.WriteLine(
            $"Sequence-Delta: {expectedSequenceDelta}");

        Console.WriteLine(
            "Track DR: 20");

        Console.WriteLine(
            "Album DR: 21");
    }

    [Fact]
    public void RemoveCopy_PreservesRealOpusExceptOwnedDrTags()
    {
        var originalPath =
            Environment.GetEnvironmentVariable(
                "DRANALYZER_MANUAL_OPUS_ORIGINAL");

        Assert.False(
            string.IsNullOrWhiteSpace(originalPath),
            "DRANALYZER_MANUAL_OPUS_ORIGINAL ist nicht gesetzt.");

        Assert.True(
            File.Exists(originalPath),
            $"OPUS-Original fehlt: {originalPath}");

        var originalHash =
            CalculateSha256(
                originalPath);

        var tempDirectory =
            Path.Combine(
                Path.GetTempPath(),
                "DRAnalyzer-OpusRemove-" +
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
                ReadOpusFile(
                    originalPath);

            OpusDynamicRangeTagWriter.Write(
                copyPath,
                trackDynamicRange: 20,
                albumDynamicRange: 21);

            OpusDynamicRangeTagWriter.Remove(
                copyPath);

            var after =
                ReadOpusFile(
                    copyPath);

            Assert.Equal(
                before.StreamSerial,
                after.StreamSerial);

            Assert.Equal(
                before.Pages.Count -
                before.CommentPageCount,
                after.Pages.Count -
                after.CommentPageCount);

            AssertBytesEqual(
                before.Pages[0].Raw,
                after.Pages[0].Raw,
                "Die OpusHead-Ogg-Seite wurde verändert.");

            AssertBytesEqual(
                before.Packets[0].Data,
                after.Packets[0].Data,
                "Das OpusHead-Paket wurde verändert.");

            var beforeTags =
                ParseOpusTags(
                    before.Packets[1].Data);

            var afterTags =
                ParseOpusTags(
                    after.Packets[1].Data);

            AssertBytesEqual(
                beforeTags.Vendor,
                afterTags.Vendor,
                "Der OpusTags-Vendor wurde verändert.");

            AssertBytesEqual(
                beforeTags.TrailingData,
                afterTags.TrailingData,
                "Trailing Data hinter den OpusTags wurde verändert.");

            var beforeForeign =
                beforeTags.Comments
                    .Where(
                        comment =>
                            !IsOwnedDrField(comment))
                    .ToArray();

            var afterForeign =
                afterTags.Comments
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
                AssertBytesEqual(
                    beforeForeign[index],
                    afterForeign[index],
                    $"Fremder Opus-Comment {index} wurde verändert.");
            }

            Assert.DoesNotContain(
                afterTags.Comments,
                IsOwnedDrField);

            var beforeAudioPackets =
                before.Packets
                    .Skip(2)
                    .ToArray();

            var afterAudioPackets =
                after.Packets
                    .Skip(2)
                    .ToArray();

            Assert.NotEmpty(
                beforeAudioPackets);

            Assert.Equal(
                beforeAudioPackets.Length,
                afterAudioPackets.Length);

            for (var index = 0;
                 index < beforeAudioPackets.Length;
                 index++)
            {
                AssertBytesEqual(
                    beforeAudioPackets[index].Data,
                    afterAudioPackets[index].Data,
                    $"Audio-Paket {index} wurde verändert.");
            }

            var beforeAudioPages =
                before.Pages
                    .Skip(
                        before.Packets[1].EndPageIndex + 1)
                    .ToArray();

            var afterAudioPages =
                after.Pages
                    .Skip(
                        after.Packets[1].EndPageIndex + 1)
                    .ToArray();

            Assert.Equal(
                beforeAudioPages.Length,
                afterAudioPages.Length);

            var expectedSequenceDelta =
                after.CommentPageCount -
                before.CommentPageCount;

            for (var index = 0;
                 index < beforeAudioPages.Length;
                 index++)
            {
                var beforePage =
                    beforeAudioPages[index];

                var afterPage =
                    afterAudioPages[index];

                Assert.Equal(
                    beforePage.Raw.Length,
                    afterPage.Raw.Length);

                AssertBytesEqual(
                    beforePage.Raw
                        .AsSpan(0, 18)
                        .ToArray(),
                    afterPage.Raw
                        .AsSpan(0, 18)
                        .ToArray(),
                    $"Audioseite {index}: Headerdaten wurden verändert.");

                AssertBytesEqual(
                    beforePage.Raw
                        .AsSpan(26)
                        .ToArray(),
                    afterPage.Raw
                        .AsSpan(26)
                        .ToArray(),
                    $"Audioseite {index}: Lacing oder Nutzdaten wurden verändert.");

                Assert.Equal(
                    expectedSequenceDelta,
                    (long)afterPage.Sequence -
                    beforePage.Sequence);
            }

            Assert.Equal(
                originalHash,
                CalculateSha256(
                    originalPath),
                ignoreCase: true);

            Console.WriteLine(
                $"Original Comment-Seiten: {before.CommentPageCount}");

            Console.WriteLine(
                $"Nach Remove Comment-Seiten: {after.CommentPageCount}");

            Console.WriteLine(
                $"Audio-Seiten: {beforeAudioPages.Length}");

            Console.WriteLine(
                $"Audio-Pakete: {beforeAudioPackets.Length}");

            Console.WriteLine(
                $"Sequence-Delta: {expectedSequenceDelta}");
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

    private static ParsedOpusFile ReadOpusFile(
        string filePath)
    {
        using var stream =
            new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

        var pages =
            new List<ParsedPage>();

        uint? streamSerial = null;
        uint? previousSequence = null;

        while (true)
        {
            var raw =
                OggPageCodec.ReadRawPage(
                    stream);

            if (raw is null)
                break;

            Assert.True(
                OggPageCodec.HasValidChecksum(raw),
                $"Ungültige Ogg-CRC auf Seite {pages.Count}.");

            var page =
                ParsePage(raw);

            if (!streamSerial.HasValue)
            {
                streamSerial =
                    page.StreamSerial;
            }
            else
            {
                Assert.Equal(
                    streamSerial.Value,
                    page.StreamSerial);
            }

            if (previousSequence.HasValue)
            {
                Assert.Equal(
                    previousSequence.Value + 1,
                    page.Sequence);
            }

            previousSequence =
                page.Sequence;

            pages.Add(page);
        }

        Assert.True(
            pages.Count >= 3,
            "Unerwartet wenige Ogg-Seiten.");

        Assert.NotNull(
            streamSerial);

        Assert.True(
            (pages[0].HeaderType & 0x02) != 0,
            "Erste Seite besitzt kein BOS-Flag.");

        var packets =
            ReconstructPackets(
                pages);

        Assert.True(
            packets.Count >= 3,
            "Unerwartet wenige Ogg-Pakete.");

        Assert.True(
            packets[0].Data
                .AsSpan()
                .StartsWith("OpusHead"u8),
            "Paket 1 ist kein OpusHead.");

        Assert.True(
            packets[1].Data
                .AsSpan()
                .StartsWith("OpusTags"u8),
            "Paket 2 ist kein OpusTags.");

        Assert.Equal(
            0,
            packets[0].StartPageIndex);

        Assert.Equal(
            0,
            packets[0].EndPageIndex);

        Assert.True(
            packets[0].EndsAtPageBoundary,
            "OpusHead endet nicht am Seitenende.");

        Assert.Equal(
            1,
            packets[1].StartPageIndex);

        Assert.True(
            packets[1].EndsAtPageBoundary,
            "OpusTags endet nicht am Seitenende.");

        var commentPageCount =
            packets[1].EndPageIndex -
            packets[1].StartPageIndex +
            1;

        return new ParsedOpusFile(
            streamSerial.Value,
            pages,
            packets,
            commentPageCount);
    }

    private static List<ParsedPacket> ReconstructPackets(
        IReadOnlyList<ParsedPage> pages)
    {
        var packets =
            new List<ParsedPacket>();

        var current =
            new List<byte>();

        var packetOpen = false;
        var packetStartPage = 0;

        for (var pageIndex = 0;
             pageIndex < pages.Count;
             pageIndex++)
        {
            var page =
                pages[pageIndex];

            var continued =
                (page.HeaderType & 0x01) != 0;

            Assert.Equal(
                packetOpen,
                continued);

            var bodyOffset = 0;

            for (var segmentIndex = 0;
                 segmentIndex < page.LacingValues.Length;
                 segmentIndex++)
            {
                if (!packetOpen)
                {
                    current.Clear();

                    packetStartPage =
                        pageIndex;

                    packetOpen =
                        true;
                }

                var segmentLength =
                    page.LacingValues[segmentIndex];

                current.AddRange(
                    page.Body
                        .AsSpan(
                            bodyOffset,
                            segmentLength)
                        .ToArray());

                bodyOffset +=
                    segmentLength;

                if (segmentLength < 255)
                {
                    packets.Add(
                        new ParsedPacket(
                            current.ToArray(),
                            packetStartPage,
                            pageIndex,
                            segmentIndex ==
                            page.LacingValues.Length - 1));

                    packetOpen =
                        false;
                }
            }

            Assert.Equal(
                page.Body.Length,
                bodyOffset);
        }

        Assert.False(
            packetOpen,
            "Datei endet mitten in einem Ogg-Paket.");

        return packets;
    }

    private static ParsedPage ParsePage(
        byte[] raw)
    {
        Assert.True(
            raw.AsSpan(0, 4)
                .SequenceEqual("OggS"u8));

        var segmentCount =
            raw[26];

        var lacing =
            raw.AsSpan(
                    27,
                    segmentCount)
                .ToArray();

        var bodyOffset =
            27 +
            segmentCount;

        var body =
            raw.AsSpan(
                    bodyOffset)
                .ToArray();

        Assert.Equal(
            lacing.Sum(
                value => (int)value),
            body.Length);

        return new ParsedPage(
            raw,
            raw[5],
            BinaryPrimitives.ReadInt64LittleEndian(
                raw.AsSpan(
                    6,
                    8)),
            BinaryPrimitives.ReadUInt32LittleEndian(
                raw.AsSpan(
                    14,
                    4)),
            BinaryPrimitives.ReadUInt32LittleEndian(
                raw.AsSpan(
                    18,
                    4)),
            lacing,
            body);
    }

    private static ParsedOpusTags ParseOpusTags(
        byte[] packet)
    {
        Assert.True(
            packet
                .AsSpan()
                .StartsWith("OpusTags"u8));

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

        var trailingData =
            packet.AsSpan(offset)
                .ToArray();

        return new ParsedOpusTags(
            vendor,
            comments,
            trailingData);
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
                item =>
                    IsField(
                        item,
                        fieldName));

        var equalsIndex =
            Array.IndexOf(
                comment,
                (byte)'=');

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

        var result =
            BinaryPrimitives.ReadUInt32LittleEndian(
                data.AsSpan(
                    offset,
                    4));

        offset += 4;

        return result;
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
            data.AsSpan(
                    offset,
                    intLength)
                .ToArray();

        offset +=
            intLength;

        return result;
    }

    private static string CalculateSha256(
        string filePath)
    {
        using var stream =
            File.OpenRead(filePath);

        return Convert.ToHexString(
            SHA256.HashData(stream));
    }

    private static void AssertBytesEqual(
        byte[] expected,
        byte[] actual,
        string message)
    {
        Assert.True(
            expected
                .AsSpan()
                .SequenceEqual(actual),
            message);
    }

    private sealed record ParsedPage(
        byte[] Raw,
        byte HeaderType,
        long GranulePosition,
        uint StreamSerial,
        uint Sequence,
        byte[] LacingValues,
        byte[] Body);

    private sealed record ParsedPacket(
        byte[] Data,
        int StartPageIndex,
        int EndPageIndex,
        bool EndsAtPageBoundary);

    private sealed record ParsedOpusFile(
        uint StreamSerial,
        IReadOnlyList<ParsedPage> Pages,
        IReadOnlyList<ParsedPacket> Packets,
        int CommentPageCount);

    private sealed record ParsedOpusTags(
        byte[] Vendor,
        IReadOnlyList<byte[]> Comments,
        byte[] TrailingData);
}
