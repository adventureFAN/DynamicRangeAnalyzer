using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using DRAnalyzer.Core.Analysis;
using DRAnalyzer.Core.Metadata;
using DRAnalyzer.Core.Tagging;

namespace DRAnalyzer.Tests;

[Trait("Category", "ExternalReference")]
public sealed class ManualRealVorbisPreservationTests
{
    [Fact]
    public void DiscoveryReferenceCopy_WriteAndRemove_PreservesRealVorbisFile()
    {
        var albumDirectory =
            Environment.GetEnvironmentVariable(
                "DRANALYZER_REFERENCE_DISCOVERY_OGG_DIR");

        Assert.False(
            string.IsNullOrWhiteSpace(albumDirectory),
            "DRANALYZER_REFERENCE_DISCOVERY_OGG_DIR ist nicht gesetzt.");

        Assert.True(
            Directory.Exists(albumDirectory),
            $"Ogg-Vorbis-Referenzordner fehlt: {albumDirectory}");

        var originalPath =
            FindFirstVorbisFile(albumDirectory!);

        Assert.False(
            string.IsNullOrWhiteSpace(originalPath),
            "Im Discovery-Referenzordner wurde keine unterstützte Ogg-Vorbis-Datei gefunden.");

        var originalHashBefore =
            CalculateSha256(originalPath!);

        var before =
            ReadOggFile(originalPath!);

        AssertVorbisHeaderPackets(before);

        var beforeComment =
            ParseCommentPacket(
                before.Packets[1].Data);

        var beforeMetadata =
            AudioMetadataReader.Read(originalPath!);

        var beforeAnalysis =
            DynamicRangeAnalyzer.Analyze(originalPath!);

        var tempDirectory =
            Path.Combine(
                Path.GetTempPath(),
                "DRAnalyzer-VorbisPreservation-" +
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

            VorbisDynamicRangeTagWriter.Write(
                copyPath,
                trackDynamicRange: 20,
                albumDynamicRange: 21);

            var afterWrite =
                ReadOggFile(copyPath);

            AssertVorbisHeaderPackets(afterWrite);

            var afterWriteComment =
                ParseCommentPacket(
                    afterWrite.Packets[1].Data);

            var afterWriteMetadata =
                AudioMetadataReader.Read(copyPath);

            var afterWriteAnalysis =
                DynamicRangeAnalyzer.Analyze(copyPath);

            AssertPreservedStructureAndAudio(
                before,
                afterWrite);

            AssertCommentPreservation(
                beforeComment,
                afterWriteComment);

            Assert.Equal(
                "20",
                GetSingleValue(
                    afterWriteComment.Comments,
                    "DYNAMIC RANGE"));

            Assert.Equal(
                "21",
                GetSingleValue(
                    afterWriteComment.Comments,
                    "ALBUM DYNAMIC RANGE"));

            AssertForeignMetadataEqual(
                beforeMetadata.Tags,
                afterWriteMetadata.Tags);

            Assert.Equal(
                "20",
                afterWriteMetadata.DynamicRange);

            Assert.Equal(
                "21",
                afterWriteMetadata.AlbumDynamicRange);

            AssertAnalysisEquivalent(
                beforeAnalysis,
                afterWriteAnalysis);

            VorbisDynamicRangeTagWriter.Remove(copyPath);

            var afterRemove =
                ReadOggFile(copyPath);

            AssertVorbisHeaderPackets(afterRemove);

            var afterRemoveComment =
                ParseCommentPacket(
                    afterRemove.Packets[1].Data);

            var afterRemoveMetadata =
                AudioMetadataReader.Read(copyPath);

            var afterRemoveAnalysis =
                DynamicRangeAnalyzer.Analyze(copyPath);

            AssertPreservedStructureAndAudio(
                before,
                afterRemove);

            AssertCommentPreservation(
                beforeComment,
                afterRemoveComment);

            Assert.DoesNotContain(
                afterRemoveComment.Comments,
                IsOwned);

            AssertForeignMetadataEqual(
                beforeMetadata.Tags,
                afterRemoveMetadata.Tags);

            Assert.True(
                string.IsNullOrWhiteSpace(
                    afterRemoveMetadata.DynamicRange));

            Assert.True(
                string.IsNullOrWhiteSpace(
                    afterRemoveMetadata.AlbumDynamicRange));

            AssertAnalysisEquivalent(
                beforeAnalysis,
                afterRemoveAnalysis);

            var originalHashAfter =
                CalculateSha256(originalPath!);

            Assert.Equal(
                originalHashBefore,
                originalHashAfter);

            Console.WriteLine(
                $"Ogg Vorbis realfile: {Path.GetFileName(originalPath)}");

            Console.WriteLine(
                $"Foreign comments preserved: {GetForeignComments(beforeComment).Length}");

            Console.WriteLine(
                $"Audio packets preserved: {before.Packets.Count - 3}");

            Console.WriteLine(
                $"Audio Ogg pages checked: {before.Pages.Count - before.Packets[3].StartPageIndex}");

            Console.WriteLine(
                "Identification packet/page preserved");

            Console.WriteLine(
                "Setup packet preserved byte-exactly");

            Console.WriteLine(
                "Write DR: 20 / Album DR: 21");

            Console.WriteLine(
                "Remove: owned DR comments removed");

            Console.WriteLine(
                "Re-analysis after Write and Remove successful");

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

    private static string? FindFirstVorbisFile(
        string albumDirectory)
    {
        foreach (var path in Directory
                     .EnumerateFiles(
                         albumDirectory,
                         "*.ogg",
                         SearchOption.AllDirectories)
                     .OrderBy(
                         path => path,
                         StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var snapshot =
                    ReadOggFile(path);

                if (IsVorbisSnapshot(snapshot))
                    return path;
            }
            catch
            {
                // Andere/defekte .ogg-Dateien werden bei der Referenzsuche übersprungen.
            }
        }

        return null;
    }

    private static bool IsVorbisSnapshot(
        OggFileSnapshot snapshot)
    {
        return
            snapshot.Packets.Count >= 4 &&
            snapshot.Packets[0].Data.AsSpan().StartsWith(
                new byte[] { 0x01, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s' }) &&
            snapshot.Packets[1].Data.AsSpan().StartsWith(
                new byte[] { 0x03, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s' }) &&
            snapshot.Packets[2].Data.AsSpan().StartsWith(
                new byte[] { 0x05, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s' });
    }

    private static void AssertVorbisHeaderPackets(
        OggFileSnapshot snapshot)
    {
        Assert.True(
            IsVorbisSnapshot(snapshot),
            "Die Testdatei besitzt nicht die erwarteten drei Vorbis-Headerpakete plus Audio.");

        Assert.Equal(
            0,
            snapshot.Packets[0].StartPageIndex);

        Assert.Equal(
            0,
            snapshot.Packets[0].EndPageIndex);

        Assert.True(
            snapshot.Packets[3].StartPageIndex >
            snapshot.Packets[2].EndPageIndex,
            "Das erste Vorbis-Audiopaket beginnt nicht auf einer frischen Ogg-Seite.");
    }

    private static void AssertPreservedStructureAndAudio(
        OggFileSnapshot before,
        OggFileSnapshot after)
    {
        Assert.Equal(
            before.StreamSerial,
            after.StreamSerial);

        Assert.True(
            before.Pages[0]
                .AsSpan()
                .SequenceEqual(after.Pages[0]),
            "Die Vorbis-Identification-Seite wurde verändert.");

        Assert.True(
            before.Packets[0].Data
                .AsSpan()
                .SequenceEqual(after.Packets[0].Data),
            "Der Vorbis-Identification-Header wurde verändert.");

        Assert.True(
            before.Packets[2].Data
                .AsSpan()
                .SequenceEqual(after.Packets[2].Data),
            "Der Vorbis-Setup-Header wurde verändert.");

        var beforeAudio =
            before.Packets
                .Skip(3)
                .Select(packet => packet.Data)
                .ToArray();

        var afterAudio =
            after.Packets
                .Skip(3)
                .Select(packet => packet.Data)
                .ToArray();

        Assert.Equal(
            beforeAudio.Length,
            afterAudio.Length);

        for (var index = 0;
             index < beforeAudio.Length;
             index++)
        {
            Assert.True(
                beforeAudio[index]
                    .AsSpan()
                    .SequenceEqual(afterAudio[index]),
                $"Vorbis-Audiopaket {index} wurde verändert.");
        }

        var beforeFirstAudioPage =
            before.Packets[3].StartPageIndex;

        var afterFirstAudioPage =
            after.Packets[3].StartPageIndex;

        var beforeAudioPages =
            before.Pages
                .Skip(beforeFirstAudioPage)
                .ToArray();

        var afterAudioPages =
            after.Pages
                .Skip(afterFirstAudioPage)
                .ToArray();

        Assert.Equal(
            beforeAudioPages.Length,
            afterAudioPages.Length);

        for (var index = 0;
             index < beforeAudioPages.Length;
             index++)
        {
            var beforePage =
                beforeAudioPages[index];

            var afterPage =
                afterAudioPages[index];

            Assert.Equal(
                beforePage.Length,
                afterPage.Length);

            // Nur Page Sequence (18..21) und CRC (22..25) dürfen
            // sich wegen einer geänderten Header-Seitenzahl unterscheiden.
            Assert.True(
                beforePage
                    .AsSpan(0, 18)
                    .SequenceEqual(
                        afterPage.AsSpan(0, 18)),
                $"Struktur vor Sequence/CRC der Audioseite {index} wurde verändert.");

            Assert.True(
                beforePage
                    .AsSpan(26)
                    .SequenceEqual(
                        afterPage.AsSpan(26)),
                $"Nutzdaten/Segmenttabelle der Audioseite {index} wurden verändert.");

            Assert.True(
                OggPageCodec.HasValidChecksum(afterPage),
                $"Audioseite {index} besitzt nach dem Schreiben keine gültige CRC.");
        }
    }

    private static void AssertCommentPreservation(
        ParsedComment before,
        ParsedComment after)
    {
        Assert.True(
            before.Vendor
                .AsSpan()
                .SequenceEqual(after.Vendor),
            "Der Vorbis-Comment-Vendor wurde verändert.");

        Assert.True(
            before.TrailingData
                .AsSpan()
                .SequenceEqual(after.TrailingData),
            "Das Framing-/Trailing-Ende des Vorbis-Comment-Headers wurde verändert.");

        var beforeForeign =
            GetForeignComments(before);

        var afterForeign =
            GetForeignComments(after);

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
                    .SequenceEqual(afterForeign[index]),
                $"Fremder Vorbis-Kommentar {index} wurde verändert oder umsortiert.");
        }
    }

    private static byte[][] GetForeignComments(
        ParsedComment comment)
    {
        return comment.Comments
            .Where(value => !IsOwned(value))
            .Select(value => value.ToArray())
            .ToArray();
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
        return
            string.Equals(
                key,
                "DYNAMIC RANGE",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                key,
                "ALBUM DYNAMIC RANGE",
                StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertAnalysisEquivalent(
        DynamicRangeResult before,
        DynamicRangeResult after)
    {
        Assert.Equal(
            before.DynamicRange,
            after.DynamicRange);

        Assert.Equal(
            before.RoundedDynamicRange,
            after.RoundedDynamicRange);

        Assert.Equal(
            before.PeakDb,
            after.PeakDb);

        Assert.Equal(
            before.RmsDb,
            after.RmsDb);

        Assert.Equal(
            before.Channels,
            after.Channels);

        Assert.Equal(
            before.SampleRate,
            after.SampleRate);

        Assert.Equal(
            before.BlockCount,
            after.BlockCount);

        Assert.Equal(
            before.ChannelDynamicRange,
            after.ChannelDynamicRange);

        Assert.Equal(
            before.ChannelPeakDb,
            after.ChannelPeakDb);

        Assert.Equal(
            before.ChannelRmsDb,
            after.ChannelRmsDb);
    }

    private static OggFileSnapshot ReadOggFile(
        string filePath)
    {
        using var input =
            new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

        var pages =
            new List<byte[]>();

        var packets =
            new List<OggPacketSnapshot>();

        using var currentPacket =
            new MemoryStream();

        var packetStartPage = -1;
        var pageIndex = 0;
        uint? streamSerial = null;

        while (true)
        {
            var page =
                OggPageCodec.ReadRawPage(input);

            if (page is null)
                break;

            Assert.True(
                OggPageCodec.HasValidChecksum(page),
                $"Ogg-Seite {pageIndex} besitzt eine ungültige CRC.");

            var currentSerial =
                BinaryPrimitives.ReadUInt32LittleEndian(
                    page.AsSpan(14, 4));

            if (!streamSerial.HasValue)
            {
                streamSerial = currentSerial;
            }
            else
            {
                Assert.Equal(
                    streamSerial.Value,
                    currentSerial);
            }

            pages.Add(page);

            var segmentCount =
                page[26];

            var bodyOffset =
                27 + segmentCount;

            var bodyCursor = 0;

            for (var segmentIndex = 0;
                 segmentIndex < segmentCount;
                 segmentIndex++)
            {
                if (currentPacket.Length == 0)
                {
                    packetStartPage =
                        pageIndex;
                }

                var length =
                    page[27 + segmentIndex];

                currentPacket.Write(
                    page,
                    bodyOffset + bodyCursor,
                    length);

                bodyCursor += length;

                if (length < 255)
                {
                    packets.Add(
                        new OggPacketSnapshot(
                            currentPacket.ToArray(),
                            packetStartPage,
                            pageIndex));

                    currentPacket.SetLength(0);
                    packetStartPage = -1;
                }
            }

            pageIndex++;
        }

        Assert.Equal(
            0,
            currentPacket.Length);

        Assert.NotEmpty(pages);
        Assert.True(streamSerial.HasValue);

        return new OggFileSnapshot(
            streamSerial!.Value,
            pages,
            packets);
    }

    private static ParsedComment ParseCommentPacket(
        byte[] packet)
    {
        Assert.True(
            packet.AsSpan().StartsWith(
                new byte[] { 0x03, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s' }));

        var offset = 7;

        var vendorLength =
            ReadUInt32(packet, ref offset);

        var vendor =
            ReadBytes(packet, ref offset, vendorLength);

        var count =
            ReadUInt32(packet, ref offset);

        var comments =
            new List<byte[]>();

        for (uint index = 0;
             index < count;
             index++)
        {
            var length =
                ReadUInt32(packet, ref offset);

            comments.Add(
                ReadBytes(packet, ref offset, length));
        }

        Assert.True(
            offset < packet.Length,
            "Vorbis-Comment-Header besitzt kein Framing-/Trailing-Ende.");

        Assert.True(
            (packet[offset] & 0x01) != 0,
            "Vorbis-Comment-Framing-Bit ist nicht gesetzt.");

        return new ParsedComment(
            vendor,
            comments,
            packet.AsSpan(offset).ToArray());
    }

    private static bool IsOwned(
        byte[] comment)
    {
        return
            IsField(comment, "DYNAMIC RANGE") ||
            IsField(comment, "ALBUM DYNAMIC RANGE");
    }

    private static bool IsField(
        byte[] comment,
        string fieldName)
    {
        var equalsIndex =
            Array.IndexOf(comment, (byte)'=');

        if (equalsIndex <= 0)
            return false;

        return string.Equals(
            Encoding.ASCII.GetString(
                comment,
                0,
                equalsIndex),
            fieldName,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string GetSingleValue(
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
            Array.IndexOf(comment, (byte)'=');

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
            offset >= 0 &&
            data.Length - offset >= 4,
            "Vorbis-Comment-Längenfeld liegt außerhalb des Pakets.");

        var value =
            BinaryPrimitives.ReadUInt32LittleEndian(
                data.AsSpan(offset, 4));

        offset += 4;
        return value;
    }

    private static byte[] ReadBytes(
        byte[] data,
        ref int offset,
        uint length)
    {
        var intLength =
            checked((int)length);

        Assert.True(
            offset >= 0 &&
            intLength >= 0 &&
            data.Length - offset >= intLength,
            "Vorbis-Comment-Daten liegen außerhalb des Pakets.");

        var result =
            data
                .AsSpan(
                    offset,
                    intLength)
                .ToArray();

        offset += intLength;
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

    private sealed record OggPacketSnapshot(
        byte[] Data,
        int StartPageIndex,
        int EndPageIndex);

    private sealed record OggFileSnapshot(
        uint StreamSerial,
        IReadOnlyList<byte[]> Pages,
        IReadOnlyList<OggPacketSnapshot> Packets);

    private sealed record ParsedComment(
        byte[] Vendor,
        IReadOnlyList<byte[]> Comments,
        byte[] TrailingData);
}
