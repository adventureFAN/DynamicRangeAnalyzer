using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using DRAnalyzer.Core.Tagging;

namespace DRAnalyzer.Tests;

[Trait("Category", "ExternalReference")]
public sealed class OpusDynamicRangeTagWriterEdgeCaseTests
{
    [Fact]
    public void MissingOwnedTags_AreAdded_AndEverythingElseIsPreserved()
    {
        var sourcePath =
            GetRealOpusSource();

        var tempDirectory =
            CreateTempDirectory();

        try
        {
            var fixturePath =
                Path.Combine(
                    tempDirectory,
                    "missing-dr-tags.opus");

            CreateModifiedFixture(
                sourcePath,
                fixturePath,
                comments =>
                    comments
                        .Where(
                            comment =>
                                !IsOwnedDrField(comment))
                        .Select(
                            comment =>
                                comment.ToArray())
                        .ToList());

            var before =
                ReadOpusFile(
                    fixturePath);

            var beforeTags =
                ParseOpusTags(
                    before.Packets[1].Data);

            Assert.DoesNotContain(
                beforeTags.Comments,
                comment =>
                    IsField(
                        comment,
                        "DYNAMIC RANGE"));

            Assert.DoesNotContain(
                beforeTags.Comments,
                comment =>
                    IsField(
                        comment,
                        "ALBUM DYNAMIC RANGE"));

            OpusDynamicRangeTagWriter.Write(
                fixturePath,
                trackDynamicRange: 20,
                albumDynamicRange: 21);

            var after =
                ReadOpusFile(
                    fixturePath);

            var afterTags =
                ParseOpusTags(
                    after.Packets[1].Data);

            AssertMetadataPreserved(
                beforeTags,
                afterTags);

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

            AssertAudioPacketsEqual(
                before,
                after);
        }
        finally
        {
            Directory.Delete(
                tempDirectory,
                recursive: true);
        }
    }

    [Fact]
    public void DuplicateOwnedTags_AreReducedToSingleCanonicalValues()
    {
        var sourcePath =
            GetRealOpusSource();

        var tempDirectory =
            CreateTempDirectory();

        try
        {
            var fixturePath =
                Path.Combine(
                    tempDirectory,
                    "duplicate-dr-tags.opus");

            CreateModifiedFixture(
                sourcePath,
                fixturePath,
                comments =>
                {
                    var result =
                        comments
                            .Where(
                                comment =>
                                    !IsOwnedDrField(comment))
                            .Select(
                                comment =>
                                    comment.ToArray())
                            .ToList();

                    result.Add(
                        Utf8(
                            "dynamic range=3"));

                    result.Add(
                        Utf8(
                            "DYNAMIC RANGE=4"));

                    result.Add(
                        Utf8(
                            "album dynamic range=5"));

                    result.Add(
                        Utf8(
                            "ALBUM DYNAMIC RANGE=6"));

                    return result;
                });

            var before =
                ReadOpusFile(
                    fixturePath);

            var beforeTags =
                ParseOpusTags(
                    before.Packets[1].Data);

            Assert.Equal(
                2,
                beforeTags.Comments.Count(
                    comment =>
                        IsField(
                            comment,
                            "DYNAMIC RANGE")));

            Assert.Equal(
                2,
                beforeTags.Comments.Count(
                    comment =>
                        IsField(
                            comment,
                            "ALBUM DYNAMIC RANGE")));

            OpusDynamicRangeTagWriter.Write(
                fixturePath,
                trackDynamicRange: 20,
                albumDynamicRange: 21);

            var after =
                ReadOpusFile(
                    fixturePath);

            var afterTags =
                ParseOpusTags(
                    after.Packets[1].Data);

            AssertMetadataPreserved(
                beforeTags,
                afterTags);

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

            AssertAudioPacketsEqual(
                before,
                after);
        }
        finally
        {
            Directory.Delete(
                tempDirectory,
                recursive: true);
        }
    }

    [Fact]
    public void CommentPageCountChange_RenumbersOnlyAudioPageSequenceAndChecksum()
    {
        var sourcePath =
            GetRealOpusSource();

        var tempDirectory =
            CreateTempDirectory();

        try
        {
            var fixturePath =
                Path.Combine(
                    tempDirectory,
                    "sequence-delta.opus");

            // Wir erzeugen absichtlich einen sehr großen
            // zusätzlichen EIGENEN DR-Tag.
            //
            // Dadurch wächst OpusTags um > 100 KB und benötigt
            // garantiert mehr Ogg-Seiten.
            //
            // Der Writer darf diesen alten eigenen Tag entfernen
            // und anschließend normale DR20/DR21-Tags schreiben.
            CreateModifiedFixture(
                sourcePath,
                fixturePath,
                comments =>
                {
                    var result =
                        comments
                            .Select(
                                comment =>
                                    comment.ToArray())
                            .ToList();

                    result.Add(
                        Utf8(
                            "DYNAMIC RANGE=" +
                            new string(
                                '9',
                                100_000)));

                    return result;
                });

            var before =
                ReadOpusFile(
                    fixturePath);

            var beforeTags =
                ParseOpusTags(
                    before.Packets[1].Data);

            var beforeCommentPageCount =
                before.Packets[1].EndPageIndex -
                before.Packets[1].StartPageIndex +
                1;

            Assert.Contains(
                beforeTags.Comments,
                comment =>
                    IsField(
                        comment,
                        "DYNAMIC RANGE"));

            OpusDynamicRangeTagWriter.Write(
                fixturePath,
                trackDynamicRange: 20,
                albumDynamicRange: 21);

            var after =
                ReadOpusFile(
                    fixturePath);

            var afterTags =
                ParseOpusTags(
                    after.Packets[1].Data);

            var afterCommentPageCount =
                after.Packets[1].EndPageIndex -
                after.Packets[1].StartPageIndex +
                1;

            // Dieser Test ist sinnlos, wenn sich die Zahl
            // der Comment-Seiten nicht wirklich geändert hat.
            Assert.NotEqual(
                beforeCommentPageCount,
                afterCommentPageCount);

            Assert.True(
                beforeCommentPageCount >
                afterCommentPageCount,
                "Die absichtlich aufgeblähten OpusTags " +
                "wurden nach dem Schreiben nicht kleiner.");

            var expectedSequenceDelta =
                afterCommentPageCount -
                beforeCommentPageCount;

            Assert.NotEqual(
                0,
                expectedSequenceDelta);

            // ------------------------------------------------
            // Identification Header / Stream
            // ------------------------------------------------

            Assert.Equal(
                before.StreamSerial,
                after.StreamSerial);

            Assert.True(
                before.Pages[0]
                    .Raw
                    .AsSpan()
                    .SequenceEqual(
                        after.Pages[0].Raw),
                "Die OpusHead-Seite wurde verändert.");

            // ------------------------------------------------
            // Metadaten
            // ------------------------------------------------

            AssertMetadataPreserved(
                beforeTags,
                afterTags);

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

            // ------------------------------------------------
            // Entscheidend:
            // alle Audio-PAKETE bytegenau identisch
            // ------------------------------------------------

            AssertAudioPacketsEqual(
                before,
                after);

            // ------------------------------------------------
            // Audio-SEITEN:
            //
            // Nur Sequence (18..21) und dadurch CRC (22..25)
            // dürfen sich ändern.
            // ------------------------------------------------

            var beforeAudioStart =
                before.Packets[1].EndPageIndex + 1;

            var afterAudioStart =
                after.Packets[1].EndPageIndex + 1;

            var beforeAudioPages =
                before.Pages
                    .Skip(
                        beforeAudioStart)
                    .ToArray();

            var afterAudioPages =
                after.Pages
                    .Skip(
                        afterAudioStart)
                    .ToArray();

            Assert.NotEmpty(
                beforeAudioPages);

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
                    beforePage.Raw.Length,
                    afterPage.Raw.Length);

                // Capture Pattern, Version, Flags,
                // Granule Position und Stream Serial.
                Assert.True(
                    beforePage.Raw
                        .AsSpan(
                            0,
                            18)
                        .SequenceEqual(
                            afterPage.Raw.AsSpan(
                                0,
                                18)),
                    $"Audioseite {index}: " +
                    "Header vor Sequence wurde verändert.");

                // Segmentanzahl, Lacing-Tabelle
                // und komplette Nutzdaten.
                Assert.True(
                    beforePage.Raw
                        .AsSpan(26)
                        .SequenceEqual(
                            afterPage.Raw.AsSpan(26)),
                    $"Audioseite {index}: " +
                    "Lacing oder Nutzdaten wurden verändert.");

                var actualSequenceDelta =
                    (long)afterPage.Sequence -
                    beforePage.Sequence;

                Assert.Equal(
                    expectedSequenceDelta,
                    actualSequenceDelta);

                Assert.True(
                    OggPageCodec.HasValidChecksum(
                        afterPage.Raw),
                    $"Audioseite {index}: " +
                    "CRC nach Renummerierung ungültig.");
            }

            // ------------------------------------------------
            // Nicht nur strukturell gültig:
            // FFmpeg muss die komplette Datei dekodieren können.
            // ------------------------------------------------

            AssertFfmpegCanDecode(
                fixturePath);

            Console.WriteLine(
                $"Comment-Seiten vorher: " +
                $"{beforeCommentPageCount}");

            Console.WriteLine(
                $"Comment-Seiten nachher: " +
                $"{afterCommentPageCount}");

            Console.WriteLine(
                $"Sequence-Delta: " +
                $"{expectedSequenceDelta}");

            Console.WriteLine(
                $"Audio-Seiten geprüft: " +
                $"{beforeAudioPages.Length}");

            Console.WriteLine(
                $"Audio-Pakete geprüft: " +
                $"{before.Packets.Count - 2}");
        }
        finally
        {
            Directory.Delete(
                tempDirectory,
                recursive: true);
        }
    }
    [Fact]
    public void RemoveOwnedTags_PreservesMetadataAndAudio()
    {
        var sourcePath =
            GetRealOpusSource();

        var tempDirectory =
            CreateTempDirectory();

        try
        {
            var fixturePath =
                Path.Combine(
                    tempDirectory,
                    "remove-dr-tags.opus");

            CreateModifiedFixture(
                sourcePath,
                fixturePath,
                comments =>
                {
                    var result =
                        comments
                            .Where(
                                comment =>
                                    !IsOwnedDrField(comment))
                            .Select(
                                comment =>
                                    comment.ToArray())
                            .ToList();

                    result.Add(
                        Utf8(
                            "dynamic range=7"));

                    result.Add(
                        Utf8(
                            "DYNAMIC RANGE=9"));

                    result.Add(
                        Utf8(
                            "album dynamic range=8"));

                    result.Add(
                        Utf8(
                            "ALBUM DYNAMIC RANGE=10"));

                    return result;
                });

            var before =
                ReadOpusFile(
                    fixturePath);

            var beforeTags =
                ParseOpusTags(
                    before.Packets[1].Data);

            Assert.Equal(
                4,
                beforeTags.Comments.Count(
                    IsOwnedDrField));

            OpusDynamicRangeTagWriter.Remove(
                fixturePath);

            var after =
                ReadOpusFile(
                    fixturePath);

            var afterTags =
                ParseOpusTags(
                    after.Packets[1].Data);

            AssertMetadataPreserved(
                beforeTags,
                afterTags);

            Assert.DoesNotContain(
                afterTags.Comments,
                IsOwnedDrField);

            Assert.Equal(
                before.StreamSerial,
                after.StreamSerial);

            Assert.True(
                before.Pages[0]
                    .Raw
                    .AsSpan()
                    .SequenceEqual(
                        after.Pages[0].Raw),
                "Die OpusHead-Seite wurde verändert.");

            AssertAudioPacketsEqual(
                before,
                after);

            var beforeCommentPageCount =
                before.Packets[1].EndPageIndex -
                before.Packets[1].StartPageIndex +
                1;

            var afterCommentPageCount =
                after.Packets[1].EndPageIndex -
                after.Packets[1].StartPageIndex +
                1;

            var expectedSequenceDelta =
                afterCommentPageCount -
                beforeCommentPageCount;

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

                Assert.True(
                    beforePage.Raw
                        .AsSpan(
                            0,
                            18)
                        .SequenceEqual(
                            afterPage.Raw.AsSpan(
                                0,
                                18)),
                    $"Audioseite {index}: " +
                    "Headerdaten wurden verändert.");

                Assert.True(
                    beforePage.Raw
                        .AsSpan(26)
                        .SequenceEqual(
                            afterPage.Raw.AsSpan(26)),
                    $"Audioseite {index}: " +
                    "Lacing oder Nutzdaten wurden verändert.");

                Assert.Equal(
                    expectedSequenceDelta,
                    (long)afterPage.Sequence -
                    beforePage.Sequence);

                Assert.True(
                    OggPageCodec.HasValidChecksum(
                        afterPage.Raw));
            }

            AssertFfmpegCanDecode(
                fixturePath);
        }
        finally
        {
            Directory.Delete(
                tempDirectory,
                recursive: true);
        }
    }

    [Fact]
    public void Remove_WhenNoOwnedTagsExist_LeavesOpusByteExact()
    {
        var sourcePath =
            GetRealOpusSource();

        var tempDirectory =
            CreateTempDirectory();

        try
        {
            var fixturePath =
                Path.Combine(
                    tempDirectory,
                    "remove-noop.opus");

            CreateModifiedFixture(
                sourcePath,
                fixturePath,
                comments =>
                    comments
                        .Where(
                            comment =>
                                !IsOwnedDrField(comment))
                        .Select(
                            comment =>
                                comment.ToArray())
                        .ToList());

            var before =
                File.ReadAllBytes(
                    fixturePath);

            OpusDynamicRangeTagWriter.Remove(
                fixturePath);

            var after =
                File.ReadAllBytes(
                    fixturePath);

            Assert.True(
                before
                    .AsSpan()
                    .SequenceEqual(
                        after),
                "Eine OPUS-Datei ohne DR-Tags wurde verändert.");

            var fileName =
                Path.GetFileName(
                    fixturePath);

            Assert.Empty(
                Directory.GetFiles(
                    tempDirectory,
                    $".{fileName}.*.dranalyzer.tmp"));

            Assert.Empty(
                Directory.GetFiles(
                    tempDirectory,
                    $".{fileName}.*.dranalyzer.backup"));
        }
        finally
        {
            Directory.Delete(
                tempDirectory,
                recursive: true);
        }
    }

    [Fact]
    public void Remove_TruncatedOgg_AbortsAndLeavesSourceByteExact()
    {
        var sourcePath =
            GetRealOpusSource();

        var tempDirectory =
            CreateTempDirectory();

        try
        {
            var fixturePath =
                Path.Combine(
                    tempDirectory,
                    "truncated-remove.opus");

            CreateModifiedFixture(
                sourcePath,
                fixturePath,
                comments =>
                {
                    var result =
                        comments
                            .Where(
                                comment =>
                                    !IsOwnedDrField(comment))
                            .Select(
                                comment =>
                                    comment.ToArray())
                            .ToList();

                    result.Add(
                        Utf8(
                            "DYNAMIC RANGE=20"));

                    result.Add(
                        Utf8(
                            "ALBUM DYNAMIC RANGE=21"));

                    return result;
                });

            using (var stream =
                   new FileStream(
                       fixturePath,
                       FileMode.Open,
                       FileAccess.Write,
                       FileShare.None))
            {
                Assert.True(
                    stream.Length > 100);

                stream.SetLength(
                    stream.Length - 37);
            }

            var before =
                File.ReadAllBytes(
                    fixturePath);

            Assert.Throws<InvalidDataException>(
                () =>
                    OpusDynamicRangeTagWriter.Remove(
                        fixturePath));

            var after =
                File.ReadAllBytes(
                    fixturePath);

            Assert.True(
                before
                    .AsSpan()
                    .SequenceEqual(after),
                "Die beschädigte Quelldatei wurde trotz " +
                "abgebrochenem Entfernen verändert.");

            var fileName =
                Path.GetFileName(
                    fixturePath);

            Assert.Empty(
                Directory.GetFiles(
                    tempDirectory,
                    $".{fileName}.*.dranalyzer.tmp"));

            Assert.Empty(
                Directory.GetFiles(
                    tempDirectory,
                    $".{fileName}.*.dranalyzer.backup"));
        }
        finally
        {
            Directory.Delete(
                tempDirectory,
                recursive: true);
        }
    }

    [Fact]
    public void TruncatedOgg_AbortsAndLeavesSourceByteExact()
    {
        var sourcePath =
            GetRealOpusSource();

        var tempDirectory =
            CreateTempDirectory();

        try
        {
            var fixturePath =
                Path.Combine(
                    tempDirectory,
                    "truncated.opus");

            File.Copy(
                sourcePath,
                fixturePath);

            using (var stream =
                   new FileStream(
                       fixturePath,
                       FileMode.Open,
                       FileAccess.Write,
                       FileShare.None))
            {
                Assert.True(
                    stream.Length > 100);

                stream.SetLength(
                    stream.Length - 37);
            }

            var before =
                File.ReadAllBytes(
                    fixturePath);

            Assert.Throws<InvalidDataException>(
                () =>
                    OpusDynamicRangeTagWriter.Write(
                        fixturePath,
                        trackDynamicRange: 20,
                        albumDynamicRange: 21));

            var after =
                File.ReadAllBytes(
                    fixturePath);

            Assert.True(
                before
                    .AsSpan()
                    .SequenceEqual(after),
                "Die beschädigte Quelldatei wurde trotz " +
                "abgebrochenem Schreibvorgang verändert.");

            var fileName =
                Path.GetFileName(
                    fixturePath);

            Assert.Empty(
                Directory.GetFiles(
                    tempDirectory,
                    $".{fileName}.*.dranalyzer.tmp"));

            Assert.Empty(
                Directory.GetFiles(
                    tempDirectory,
                    $".{fileName}.*.dranalyzer.backup"));
        }
        finally
        {
            Directory.Delete(
                tempDirectory,
                recursive: true);
        }
    }

    private static void AssertFfmpegCanDecode(
        string filePath)
    {
        var startInfo =
            new ProcessStartInfo
            {
                FileName = "ffmpeg",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

        startInfo.ArgumentList.Add(
            "-v");

        startInfo.ArgumentList.Add(
            "error");

        startInfo.ArgumentList.Add(
            "-i");

        startInfo.ArgumentList.Add(
            filePath);

        startInfo.ArgumentList.Add(
            "-map");

        startInfo.ArgumentList.Add(
            "0:a:0");

        startInfo.ArgumentList.Add(
            "-f");

        startInfo.ArgumentList.Add(
            "null");

        startInfo.ArgumentList.Add(
            "-");

        using var process =
            Process.Start(
                startInfo)
            ?? throw new InvalidOperationException(
                "FFmpeg konnte nicht gestartet werden.");

        var standardOutput =
            process.StandardOutput.ReadToEnd();

        var standardError =
            process.StandardError.ReadToEnd();

        process.WaitForExit();

        Assert.True(
            process.ExitCode == 0,
            "FFmpeg konnte die erzeugte OPUS-Datei " +
            "nicht vollständig dekodieren." +
            Environment.NewLine +
            standardError +
            Environment.NewLine +
            standardOutput);
    }
    private static string GetRealOpusSource()
    {
        var sourcePath =
            Environment.GetEnvironmentVariable(
                "DRANALYZER_MANUAL_OPUS_ORIGINAL");

        Assert.False(
            string.IsNullOrWhiteSpace(sourcePath),
            "DRANALYZER_MANUAL_OPUS_ORIGINAL ist nicht gesetzt.");

        Assert.True(
            File.Exists(sourcePath),
            $"OPUS-Referenzdatei fehlt: {sourcePath}");

        Assert.Equal(
            ".opus",
            Path.GetExtension(sourcePath),
            ignoreCase: true);

        return sourcePath;
    }

    private static string CreateTempDirectory()
    {
        var path =
            Path.Combine(
                Path.GetTempPath(),
                "DRAnalyzer-OpusEdge-" +
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(
            path);

        return path;
    }

    private static void CreateModifiedFixture(
        string sourcePath,
        string destinationPath,
        Func<List<byte[]>, List<byte[]>> mutateComments)
    {
        var source =
            ReadOpusFile(
                sourcePath);

        var originalTags =
            ParseOpusTags(
                source.Packets[1].Data);

        var mutableComments =
            originalTags.Comments
                .Select(
                    comment =>
                        comment.ToArray())
                .ToList();

        var modifiedComments =
            mutateComments(
                mutableComments);

        var modifiedPacket =
            BuildOpusTags(
                originalTags.Vendor,
                modifiedComments,
                originalTags.TrailingData);

        var firstCommentPageIndex =
            source.Packets[1].StartPageIndex;

        var firstCommentSequence =
            source.Pages[
                firstCommentPageIndex]
                .Sequence;

        var newCommentPages =
            OggOpusCommentPageBuilder.Build(
                modifiedPacket,
                source.StreamSerial,
                firstCommentSequence);

        var oldAudioStartPage =
            source.Packets[1].EndPageIndex + 1;

        // Wichtig:
        // Der Output-Stream muss vollständig geschlossen sein,
        // BEVOR ReadOpusFile() die erzeugte Fixture erneut öffnet.
        using (var output =
               new FileStream(
                   destinationPath,
                   FileMode.CreateNew,
                   FileAccess.Write,
                   FileShare.None))
        {
            // OpusHead-Seite bytegenau übernehmen.
            output.Write(
                source.Pages[0].Raw);

            foreach (var page
                     in newCommentPages)
            {
                output.Write(
                    page);
            }

            var nextSequence =
                checked(
                    firstCommentSequence +
                    (uint)newCommentPages.Count);

            for (var pageIndex =
                     oldAudioStartPage;
                 pageIndex <
                     source.Pages.Count;
                 pageIndex++)
            {
                var sourcePage =
                    source.Pages[
                        pageIndex];

                byte[] outputPage;

                if (sourcePage.Sequence ==
                    nextSequence)
                {
                    outputPage =
                        sourcePage.Raw;
                }
                else
                {
                    outputPage =
                        WithPageSequence(
                            sourcePage.Raw,
                            nextSequence);
                }

                output.Write(
                    outputPage);

                nextSequence =
                    checked(
                        nextSequence + 1);
            }

            output.Flush(
                flushToDisk: true);
        }

        // Erst NACH Dispose erneut öffnen.
        // Fixture muss selbst eine gültige
        // Ogg-Opus-Datei sein.
        _ =
            ReadOpusFile(
                destinationPath);
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
                OggPageCodec.HasValidChecksum(
                    raw),
                $"Ungültige CRC auf Ogg-Seite {pages.Count}.");

            var page =
                ParsePage(
                    raw);

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

            pages.Add(
                page);
        }

        Assert.True(
            pages.Count >= 3);

        Assert.NotNull(
            streamSerial);

        var packets =
            ReconstructPackets(
                pages);

        Assert.True(
            packets.Count >= 3);

        Assert.True(
            packets[0].Data
                .AsSpan()
                .StartsWith("OpusHead"u8));

        Assert.True(
            packets[1].Data
                .AsSpan()
                .StartsWith("OpusTags"u8));

        Assert.Equal(
            0,
            packets[0].StartPageIndex);

        Assert.Equal(
            0,
            packets[0].EndPageIndex);

        Assert.Equal(
            1,
            packets[1].StartPageIndex);

        Assert.True(
            packets[1].EndsAtPageBoundary);

        return new ParsedOpusFile(
            streamSerial.Value,
            pages,
            packets);
    }

    private static List<ParsedPacket>
        ReconstructPackets(
            IReadOnlyList<ParsedPage> pages)
    {
        var packets =
            new List<ParsedPacket>();

        var current =
            new List<byte>();

        var packetOpen =
            false;

        var packetStartPage =
            0;

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

            var bodyOffset =
                0;

            for (var segmentIndex = 0;
                 segmentIndex <
                 page.LacingValues.Length;
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
                    page.LacingValues[
                        segmentIndex];

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
            packetOpen);

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
                value =>
                    (int)value),
            body.Length);

        return new ParsedPage(
            raw,
            raw[5],
            BinaryPrimitives
                .ReadInt64LittleEndian(
                    raw.AsSpan(
                        6,
                        8)),
            BinaryPrimitives
                .ReadUInt32LittleEndian(
                    raw.AsSpan(
                        14,
                        4)),
            BinaryPrimitives
                .ReadUInt32LittleEndian(
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
            packet.AsSpan()
                .StartsWith("OpusTags"u8));

        var offset =
            8;

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
            packet.AsSpan(
                    offset)
                .ToArray();

        return new ParsedOpusTags(
            vendor,
            comments,
            trailingData);
    }

    private static byte[] BuildOpusTags(
        byte[] vendor,
        IReadOnlyList<byte[]> comments,
        byte[] trailingData)
    {
        using var stream =
            new MemoryStream();

        stream.Write(
            "OpusTags"u8);

        WriteUInt32(
            stream,
            checked(
                (uint)vendor.Length));

        stream.Write(
            vendor);

        WriteUInt32(
            stream,
            checked(
                (uint)comments.Count));

        foreach (var comment
                 in comments)
        {
            WriteUInt32(
                stream,
                checked(
                    (uint)comment.Length));

            stream.Write(
                comment);
        }

        stream.Write(
            trailingData);

        return stream.ToArray();
    }

    private static void AssertMetadataPreserved(
        ParsedOpusTags before,
        ParsedOpusTags after)
    {
        Assert.True(
            before.Vendor
                .AsSpan()
                .SequenceEqual(
                    after.Vendor),
            "Vendor wurde verändert.");

        Assert.True(
            before.TrailingData
                .AsSpan()
                .SequenceEqual(
                    after.TrailingData),
            "Trailing Data wurde verändert.");

        var beforeForeign =
            before.Comments
                .Where(
                    comment =>
                        !IsOwnedDrField(
                            comment))
                .ToArray();

        var afterForeign =
            after.Comments
                .Where(
                    comment =>
                        !IsOwnedDrField(
                            comment))
                .ToArray();

        Assert.Equal(
            beforeForeign.Length,
            afterForeign.Length);

        for (var index = 0;
             index <
             beforeForeign.Length;
             index++)
        {
            Assert.True(
                beforeForeign[index]
                    .AsSpan()
                    .SequenceEqual(
                        afterForeign[index]),
                $"Fremder Comment {index} wurde verändert.");
        }
    }

    private static void AssertAudioPacketsEqual(
        ParsedOpusFile before,
        ParsedOpusFile after)
    {
        var beforeAudio =
            before.Packets
                .Skip(2)
                .ToArray();

        var afterAudio =
            after.Packets
                .Skip(2)
                .ToArray();

        Assert.NotEmpty(
            beforeAudio);

        Assert.Equal(
            beforeAudio.Length,
            afterAudio.Length);

        for (var index = 0;
             index <
             beforeAudio.Length;
             index++)
        {
            Assert.True(
                beforeAudio[index]
                    .Data
                    .AsSpan()
                    .SequenceEqual(
                        afterAudio[index].Data),
                $"Audio-Paket {index} wurde verändert.");
        }
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
            comment.Length -
            equalsIndex - 1);
    }

    private static byte[] Utf8(
        string value)
    {
        return Encoding.UTF8.GetBytes(
            value);
    }

    private static uint ReadUInt32(
        byte[] data,
        ref int offset)
    {
        if (offset >
            data.Length - 4)
        {
            throw new InvalidDataException(
                "Beschädigtes OpusTags-Paket.");
        }

        var value =
            BinaryPrimitives
                .ReadUInt32LittleEndian(
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
        if (length >
            int.MaxValue)
        {
            throw new InvalidDataException(
                "Ungültige OpusTags-Länge.");
        }

        var intLength =
            (int)length;

        if (offset >
            data.Length -
            intLength)
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

        stream.Write(
            buffer);
    }

    private static byte[] WithPageSequence(
        byte[] page,
        uint sequence)
    {
        var result =
            page.ToArray();

        BinaryPrimitives
            .WriteUInt32LittleEndian(
                result.AsSpan(
                    18,
                    4),
                sequence);

        return
            OggPageCodec
                .WithRecalculatedChecksum(
                    result);
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
        IReadOnlyList<ParsedPacket> Packets);

    private sealed record ParsedOpusTags(
        byte[] Vendor,
        IReadOnlyList<byte[]> Comments,
        byte[] TrailingData);
}




