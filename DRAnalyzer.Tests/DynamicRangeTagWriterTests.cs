using System.Buffers.Binary;
using System.Text;
using DRAnalyzer.Core.Tagging;

namespace DRAnalyzer.Tests;

public sealed class DynamicRangeTagWriterTests
{
    [Theory]
    [InlineData(@"C:\Music\track.flac")]
    [InlineData(@"C:\Music\track.FLAC")]
    [InlineData(@"C:\Music\track.FlAc")]
    [InlineData(@"C:\Music\track.opus")]
    [InlineData(@"C:\Music\track.OPUS")]
    [InlineData(@"C:\Music\track.OpUs")]
    [InlineData(@"C:\Music\track.mp3")]
    [InlineData(@"C:\Music\track.MP3")]
    [InlineData(@"C:\Music\track.Mp3")]
    [InlineData(@"C:\Music\track.ogg")]
    [InlineData(@"C:\Music\track.OGG")]
    [InlineData(@"C:\Music\track.OgG")]
    [InlineData(@"C:\Music\track.m4a")]
    [InlineData(@"C:\Music\track.M4A")]
    [InlineData(@"C:\Music\track.M4a")]
    [InlineData(@"C:\Music\track.wav")]
    [InlineData(@"C:\Music\track.WAV")]
    [InlineData(@"C:\Music\track.Wav")]
    [InlineData(@"C:\Music\track.aiff")]
    [InlineData(@"C:\Music\track.AIFF")]
    [InlineData(@"C:\Music\track.aif")]
    [InlineData(@"C:\Music\track.AIF")]
    [InlineData(@"C:\Music\track.ape")]
    [InlineData(@"C:\Music\track.APE")]
    [InlineData(@"C:\Music\track.Ape")]
    [InlineData(@"C:\Music\track.wv")]
    [InlineData(@"C:\Music\track.WV")]
    [InlineData(@"C:\Music\track.Wv")]
    public void CanWrite_AcceptsSupportedFormats(
        string filePath)
    {
        Assert.True(
            DynamicRangeTagWriter.CanWrite(
                filePath));
    }

    [Theory]
    [InlineData(@"C:\Music\track.aac")]
    [InlineData("")]
    public void CanWrite_RejectsUnsupportedFormats(
        string filePath)
    {
        Assert.False(
            DynamicRangeTagWriter.CanWrite(
                filePath));
    }

    [Theory]
    [InlineData(@"C:\Music\track.flac")]
    [InlineData(@"C:\Music\track.FLAC")]
    [InlineData(@"C:\Music\track.opus")]
    [InlineData(@"C:\Music\track.OPUS")]
    [InlineData(@"C:\Music\track.mp3")]
    [InlineData(@"C:\Music\track.MP3")]
    [InlineData(@"C:\Music\track.ogg")]
    [InlineData(@"C:\Music\track.OGG")]
    [InlineData(@"C:\Music\track.m4a")]
    [InlineData(@"C:\Music\track.M4A")]
    [InlineData(@"C:\Music\track.wav")]
    [InlineData(@"C:\Music\track.WAV")]
    [InlineData(@"C:\Music\track.aiff")]
    [InlineData(@"C:\Music\track.AIFF")]
    [InlineData(@"C:\Music\track.aif")]
    [InlineData(@"C:\Music\track.AIF")]
    [InlineData(@"C:\Music\track.ape")]
    [InlineData(@"C:\Music\track.APE")]
    [InlineData(@"C:\Music\track.wv")]
    [InlineData(@"C:\Music\track.WV")]
    public void CanRemove_AcceptsSupportedFormats(
        string filePath)
    {
        Assert.True(
            DynamicRangeTagWriter.CanRemove(
                filePath));
    }

    [Theory]
    [InlineData(@"C:\Music\track.aac")]
    [InlineData("")]
    public void CanRemove_RejectsUnsupportedFormats(
        string filePath)
    {
        Assert.False(
            DynamicRangeTagWriter.CanRemove(
                filePath));
    }

    [Fact]
    public void Write_UnsupportedFormat_DoesNotModifyFile()
    {
        var filePath =
            Path.Combine(
                Path.GetTempPath(),
                $"dranalyzer-writer-{Guid.NewGuid():N}.aac");

        var original =
            new byte[]
            {
                0x01, 0x02, 0x03, 0x04,
                0x05, 0x06, 0x07, 0x08
            };

        try
        {
            File.WriteAllBytes(
                filePath,
                original);

            Assert.Throws<NotSupportedException>(
                () =>
                    DynamicRangeTagWriter.Write(
                        filePath,
                        trackDynamicRange: 12,
                        albumDynamicRange: 13));

            Assert.Equal(
                original,
                File.ReadAllBytes(filePath));
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Fact]
    public void Remove_UnsupportedFormat_DoesNotModifyFile()
    {
        var filePath =
            Path.Combine(
                Path.GetTempPath(),
                $"dranalyzer-remover-{Guid.NewGuid():N}.aac");

        var original =
            new byte[]
            {
                0x11, 0x22, 0x33, 0x44,
                0x55, 0x66, 0x77, 0x88
            };

        try
        {
            File.WriteAllBytes(
                filePath,
                original);

            Assert.Throws<NotSupportedException>(
                () =>
                    DynamicRangeTagWriter.Remove(
                        filePath));

            Assert.Equal(
                original,
                File.ReadAllBytes(filePath));
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Fact]
    public void WriteAndRemove_Mp3_DispatchesToMp3Writer()
    {
        var filePath =
            Path.Combine(
                Path.GetTempPath(),
                $"dranalyzer-writer-mp3-{Guid.NewGuid():N}.mp3");

        var originalPayload =
            new byte[]
            {
                0xFF, 0xFB, 0x90, 0x64,
                0x11, 0x22, 0x33, 0x44
            };

        try
        {
            File.WriteAllBytes(
                filePath,
                originalPayload);

            DynamicRangeTagWriter.Write(
                filePath,
                trackDynamicRange: 12,
                albumDynamicRange: 13);

            var written =
                File.ReadAllBytes(filePath);

            Assert.True(written.Length > originalPayload.Length);
            Assert.Equal((byte)'I', written[0]);
            Assert.Equal((byte)'D', written[1]);
            Assert.Equal((byte)'3', written[2]);

            DynamicRangeTagWriter.Remove(filePath);

            Assert.Equal(
                originalPayload,
                File.ReadAllBytes(filePath));
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Fact]
    public void WriteAndRemove_OggVorbis_DispatchesToVorbisWriter()
    {
        var filePath =
            Path.Combine(
                Path.GetTempPath(),
                $"dranalyzer-writer-vorbis-{Guid.NewGuid():N}.ogg");

        try
        {
            CreateSyntheticVorbisFile(filePath);

            var original =
                File.ReadAllBytes(filePath);

            DynamicRangeTagWriter.Write(
                filePath,
                trackDynamicRange: 12,
                albumDynamicRange: 13);

            var written =
                File.ReadAllBytes(filePath);

            Assert.True(
                ContainsSequence(
                    written,
                    "DYNAMIC RANGE=12"u8));

            Assert.True(
                ContainsSequence(
                    written,
                    "ALBUM DYNAMIC RANGE=13"u8));

            DynamicRangeTagWriter.Remove(filePath);

            Assert.Equal(
                original,
                File.ReadAllBytes(filePath));
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Fact]
    public void WriteAndRemove_M4a_DispatchesToM4aWriter()
    {
        var filePath =
            Path.Combine(
                Path.GetTempPath(),
                $"dranalyzer-writer-m4a-{Guid.NewGuid():N}.m4a");

        var malformed =
            new byte[]
            {
                0x00, 0x00, 0x00, 0x20,
                (byte)'f', (byte)'t', (byte)'y', (byte)'p'
            };

        try
        {
            File.WriteAllBytes(
                filePath,
                malformed);

            Assert.Throws<InvalidDataException>(
                () =>
                    DynamicRangeTagWriter.Write(
                        filePath,
                        trackDynamicRange: 12,
                        albumDynamicRange: 13));

            Assert.Equal(
                malformed,
                File.ReadAllBytes(filePath));

            Assert.Throws<InvalidDataException>(
                () =>
                    DynamicRangeTagWriter.Remove(
                        filePath));

            Assert.Equal(
                malformed,
                File.ReadAllBytes(filePath));
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Fact]
    public void WriteAndRemove_Wav_DispatchesToWavWriter()
    {
        var filePath =
            Path.Combine(
                Path.GetTempPath(),
                $"dranalyzer-writer-wav-{Guid.NewGuid():N}.wav");

        var original =
            BuildMinimalPcmWave();

        try
        {
            File.WriteAllBytes(
                filePath,
                original);

            DynamicRangeTagWriter.Write(
                filePath,
                trackDynamicRange: 12,
                albumDynamicRange: 13);

            var written =
                File.ReadAllBytes(filePath);

            Assert.True(written.Length > original.Length);
            Assert.True(ContainsSequence(written, "DYNAMIC RANGE"u8));
            Assert.True(ContainsSequence(written, "ALBUM DYNAMIC RANGE"u8));

            DynamicRangeTagWriter.Remove(filePath);

            Assert.Equal(
                original,
                File.ReadAllBytes(filePath));
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Fact]
    public void WriteAndRemove_Ape_DispatchesToApeWriter()
    {
        var filePath =
            Path.Combine(
                Path.GetTempPath(),
                $"dranalyzer-writer-ape-{Guid.NewGuid():N}.ape");

        var original =
            BuildMinimalMonkeyAudioPayload();

        try
        {
            File.WriteAllBytes(
                filePath,
                original);

            DynamicRangeTagWriter.Write(
                filePath,
                trackDynamicRange: 12,
                albumDynamicRange: 13);

            var written =
                File.ReadAllBytes(filePath);

            Assert.True(written.Length > original.Length);
            Assert.True(ContainsSequence(written, "APETAGEX"u8));
            Assert.True(ContainsSequence(written, "DYNAMIC RANGE"u8));
            Assert.True(ContainsSequence(written, "ALBUM DYNAMIC RANGE"u8));

            DynamicRangeTagWriter.Remove(filePath);

            Assert.Equal(
                original,
                File.ReadAllBytes(filePath));
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Fact]
    public void WriteAndRemove_WavPack_DispatchesToWavPackWriter()
    {
        var filePath =
            Path.Combine(
                Path.GetTempPath(),
                $"dranalyzer-writer-wavpack-{Guid.NewGuid():N}.wv");

        var original =
            BuildMinimalWavPackPayload();

        try
        {
            File.WriteAllBytes(
                filePath,
                original);

            DynamicRangeTagWriter.Write(
                filePath,
                trackDynamicRange: 12,
                albumDynamicRange: 13);

            var written =
                File.ReadAllBytes(filePath);

            Assert.True(written.Length > original.Length);
            Assert.True(ContainsSequence(written, "APETAGEX"u8));
            Assert.True(ContainsSequence(written, "DYNAMIC RANGE"u8));
            Assert.True(ContainsSequence(written, "ALBUM DYNAMIC RANGE"u8));

            DynamicRangeTagWriter.Remove(filePath);

            Assert.Equal(
                original,
                File.ReadAllBytes(filePath));
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Theory]
    [InlineData(".aiff")]
    [InlineData(".aif")]
    public void WriteAndRemove_Aiff_DispatchesToAiffWriter(
        string extension)
    {
        var filePath =
            Path.Combine(
                Path.GetTempPath(),
                $"dranalyzer-writer-aiff-{Guid.NewGuid():N}{extension}");

        var original =
            BuildMinimalAiff();

        try
        {
            File.WriteAllBytes(
                filePath,
                original);

            DynamicRangeTagWriter.Write(
                filePath,
                trackDynamicRange: 12,
                albumDynamicRange: 13);

            var written =
                File.ReadAllBytes(filePath);

            Assert.True(written.Length > original.Length);
            Assert.True(ContainsSequence(written, "DYNAMIC RANGE"u8));
            Assert.True(ContainsSequence(written, "ALBUM DYNAMIC RANGE"u8));

            DynamicRangeTagWriter.Remove(filePath);

            Assert.Equal(
                original,
                File.ReadAllBytes(filePath));
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    private static byte[] BuildMinimalWavPackPayload()
    {
        return CombineByteArrays(
            BuildMinimalWavPackBlock(160, 0x0410, 17),
            BuildMinimalWavPackBlock(224, 0x0410, 41));
    }

    private static byte[] BuildMinimalWavPackBlock(
        int length,
        ushort version,
        int seed)
    {
        var block =
            new byte[length];

        "wvpk"u8.CopyTo(
            block.AsSpan(0, 4));

        BinaryPrimitives.WriteUInt32LittleEndian(
            block.AsSpan(4, 4),
            checked((uint)(length - 8)));

        BinaryPrimitives.WriteUInt16LittleEndian(
            block.AsSpan(8, 2),
            version);

        for (var index = 10;
             index < block.Length;
             index++)
        {
            block[index] =
                checked((byte)((index * seed + 23) & 0xff));
        }

        return block;
    }

    private static byte[] CombineByteArrays(
        params byte[][] arrays)
    {
        var length =
            arrays.Sum(array => array.Length);

        var result =
            new byte[length];

        var offset = 0;

        foreach (var array in arrays)
        {
            array.CopyTo(result, offset);
            offset += array.Length;
        }

        return result;
    }

    private static byte[] BuildMinimalMonkeyAudioPayload()
    {
        var payload =
            new byte[4096];

        "MAC "u8.CopyTo(
            payload.AsSpan(0, 4));

        for (var index = 4;
             index < payload.Length;
             index++)
        {
            payload[index] =
                checked((byte)((index * 37 + 11) & 0xff));
        }

        return payload;
    }

    private static byte[] BuildMinimalAiff()
    {
        var commPayload = new byte[18];
        BinaryPrimitives.WriteUInt16BigEndian(
            commPayload.AsSpan(0, 2),
            1);
        BinaryPrimitives.WriteUInt32BigEndian(
            commPayload.AsSpan(2, 4),
            8);
        BinaryPrimitives.WriteUInt16BigEndian(
            commPayload.AsSpan(6, 2),
            16);

        byte[] sampleRate =
        {
            0x40, 0x0E, 0xAC, 0x44,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00
        };
        sampleRate.CopyTo(commPayload, 8);

        var comm = BuildAiffChunk("COMM", commPayload);

        var ssndPayload = new byte[8 + 16];
        for (var index = 8; index < ssndPayload.Length; index++)
        {
            ssndPayload[index] =
                checked((byte)(index - 7));
        }

        var ssnd = BuildAiffChunk("SSND", ssndPayload);
        var length = 12 + comm.Length + ssnd.Length;
        var aiff = new byte[length];

        "FORM"u8.CopyTo(aiff.AsSpan(0, 4));
        BinaryPrimitives.WriteUInt32BigEndian(
            aiff.AsSpan(4, 4),
            checked((uint)(length - 8)));
        "AIFF"u8.CopyTo(aiff.AsSpan(8, 4));

        comm.CopyTo(aiff, 12);
        ssnd.CopyTo(aiff, 12 + comm.Length);

        return aiff;
    }

    private static byte[] BuildAiffChunk(
        string id,
        byte[] payload)
    {
        var paddedLength =
            payload.Length + (payload.Length & 1);

        var chunk =
            new byte[8 + paddedLength];

        Encoding.ASCII
            .GetBytes(id)
            .CopyTo(chunk, 0);

        BinaryPrimitives.WriteUInt32BigEndian(
            chunk.AsSpan(4, 4),
            checked((uint)payload.Length));

        payload.CopyTo(chunk, 8);
        return chunk;
    }

    private static byte[] BuildMinimalPcmWave()
    {
        const int fmtPayloadSize = 16;
        const int dataPayloadSize = 8;
        const int riffSize =
            4 +
            8 + fmtPayloadSize +
            8 + dataPayloadSize;

        var wave =
            new byte[8 + riffSize];

        "RIFF"u8.CopyTo(wave.AsSpan(0, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(
            wave.AsSpan(4, 4),
            riffSize);
        "WAVE"u8.CopyTo(wave.AsSpan(8, 4));

        var offset = 12;

        "fmt "u8.CopyTo(wave.AsSpan(offset, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(
            wave.AsSpan(offset + 4, 4),
            fmtPayloadSize);
        BinaryPrimitives.WriteUInt16LittleEndian(
            wave.AsSpan(offset + 8, 2),
            1);
        BinaryPrimitives.WriteUInt16LittleEndian(
            wave.AsSpan(offset + 10, 2),
            1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            wave.AsSpan(offset + 12, 4),
            44100);
        BinaryPrimitives.WriteUInt32LittleEndian(
            wave.AsSpan(offset + 16, 4),
            88200);
        BinaryPrimitives.WriteUInt16LittleEndian(
            wave.AsSpan(offset + 20, 2),
            2);
        BinaryPrimitives.WriteUInt16LittleEndian(
            wave.AsSpan(offset + 22, 2),
            16);

        offset += 8 + fmtPayloadSize;

        "data"u8.CopyTo(wave.AsSpan(offset, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(
            wave.AsSpan(offset + 4, 4),
            dataPayloadSize);

        for (var index = 0; index < dataPayloadSize; index++)
        {
            wave[offset + 8 + index] =
                checked((byte)(index + 1));
        }

        return wave;
    }

    private static void CreateSyntheticVorbisFile(
        string filePath)
    {
        const uint serial =
            0x56465244;

        var identification =
            CreateHeaderPacket(
                0x01,
                payloadLength: 23);

        var comment =
            BuildCommentPacket();

        var setup =
            CreateHeaderPacket(
                0x05,
                payloadLength: 64);

        var identificationPage =
            BuildSinglePacketPage(
                identification,
                serial,
                sequence: 0,
                headerType: 0x02,
                granulePosition: 0);

        var headerPages =
            OggVorbisHeaderPageBuilder.Build(
                comment,
                setup,
                serial,
                firstPageSequence: 1);

        using var output =
            new FileStream(
                filePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);

        output.Write(identificationPage);

        foreach (var page in headerPages)
        {
            output.Write(page);
        }

        var audioPacket =
            new byte[]
            {
                0x00, 0x11, 0x22, 0x33,
                0x44, 0x55, 0x66, 0x77
            };

        var audioPage =
            BuildSinglePacketPage(
                audioPacket,
                serial,
                checked(1u + (uint)headerPages.Count),
                headerType: 0x04,
                granulePosition: 1024);

        output.Write(audioPage);
    }

    private static byte[] CreateHeaderPacket(
        byte type,
        int payloadLength)
    {
        var packet =
            new byte[7 + payloadLength];

        packet[0] = type;
        "vorbis"u8.CopyTo(
            packet.AsSpan(1, 6));

        for (var index = 7;
             index < packet.Length;
             index++)
        {
            packet[index] =
                checked((byte)(index % 251));
        }

        return packet;
    }

    private static byte[] BuildCommentPacket()
    {
        using var stream =
            new MemoryStream();

        stream.WriteByte(0x03);
        stream.Write("vorbis"u8);

        var vendor =
            "DRAnalyzer facade test"u8.ToArray();

        WriteUInt32(
            stream,
            checked((uint)vendor.Length));

        stream.Write(vendor);

        var artist =
            "ARTIST=Facade Test"u8.ToArray();

        WriteUInt32(
            stream,
            1);

        WriteUInt32(
            stream,
            checked((uint)artist.Length));

        stream.Write(artist);

        stream.WriteByte(0x01);

        return stream.ToArray();
    }

    private static byte[] BuildSinglePacketPage(
        byte[] packet,
        uint serial,
        uint sequence,
        byte headerType,
        long granulePosition)
    {
        if (packet.Length >= 255)
        {
            throw new InvalidOperationException(
                "Synthetic facade-test packet is unexpectedly large.");
        }

        var page =
            new byte[28 + packet.Length];

        "OggS"u8.CopyTo(
            page.AsSpan(0, 4));

        page[4] = 0;
        page[5] = headerType;

        BinaryPrimitives.WriteInt64LittleEndian(
            page.AsSpan(6, 8),
            granulePosition);

        BinaryPrimitives.WriteUInt32LittleEndian(
            page.AsSpan(14, 4),
            serial);

        BinaryPrimitives.WriteUInt32LittleEndian(
            page.AsSpan(18, 4),
            sequence);

        page.AsSpan(22, 4).Clear();
        page[26] = 1;
        page[27] =
            checked((byte)packet.Length);

        packet.CopyTo(
            page,
            28);

        return
            OggPageCodec.WithRecalculatedChecksum(
                page);
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

    private static bool ContainsSequence(
        byte[] source,
        ReadOnlySpan<byte> sequence)
    {
        return
            source
                .AsSpan()
                .IndexOf(sequence) >= 0;
    }

}
