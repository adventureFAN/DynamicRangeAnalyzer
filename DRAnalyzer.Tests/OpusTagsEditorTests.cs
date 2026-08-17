using System.Buffers.Binary;
using System.Text;
using DRAnalyzer.Core.Tagging;

namespace DRAnalyzer.Tests;

public sealed class OpusTagsEditorTests
{
    [Fact]
    public void UpdateDynamicRangeTags_ChangesOnlyOwnedFields()
    {
        var vendor =
            Encoding.UTF8.GetBytes(
                "OpusTestVendor – 日本語");

        var comments =
            new[]
            {
                Utf8("ARTIST=Björk – 東京"),
                Utf8("TITLE=Straße – 한국어"),
                Utf8("ALBUM=Café – العربية"),
                Utf8("R128_TRACK_GAIN=-573"),
                Utf8("REPLAYGAIN_TRACK_GAIN=-5.25 dB"),
                Utf8("CUSTOM_TEST=DO NOT TOUCH – 🎵"),

                Utf8("dynamic range=7"),
                Utf8("DYNAMIC RANGE=9"),

                Utf8("album dynamic range=8"),
                Utf8("ALBUM DYNAMIC RANGE=10")
            };

        // Absichtlich Nicht-Text.
        // Das erste Byte hat außerdem Bit 0 gesetzt.
        var trailing =
            new byte[]
            {
                0xA5,
                0x00,
                0xFF,
                0x13,
                0x37,
                0x80,
                0x42
            };

        var original =
            BuildPacket(
                vendor,
                comments,
                trailing);

        var modified =
            OpusTagsEditor.UpdateDynamicRangeTags(
                original,
                trackDynamicRange: 12,
                albumDynamicRange: 13);

        // Input-Array selbst darf nicht verändert worden sein.
        var originalAgain =
            BuildPacket(
                vendor,
                comments,
                trailing);

        Assert.True(
            original.AsSpan()
                .SequenceEqual(originalAgain));

        var before =
            ParsePacket(original);

        var after =
            ParsePacket(modified);

        // Vendor bytegenau identisch.
        Assert.True(
            before.Vendor
                .AsSpan()
                .SequenceEqual(
                    after.Vendor));

        // Binäre Zusatzdaten bytegenau identisch.
        Assert.True(
            before.Trailing
                .AsSpan()
                .SequenceEqual(
                    after.Trailing));

        var beforeForeign =
            before.Comments
                .Where(
                    value =>
                        !IsOwnedDrField(value))
                .ToArray();

        var afterForeign =
            after.Comments
                .Where(
                    value =>
                        !IsOwnedDrField(value))
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
                $"Fremder Comment {index} wurde verändert.");
        }

        Assert.Equal(
            "12",
            GetSingleFieldValue(
                after.Comments,
                "DYNAMIC RANGE"));

        Assert.Equal(
            "13",
            GetSingleFieldValue(
                after.Comments,
                "ALBUM DYNAMIC RANGE"));

        // Doppelte alte DR-Tags müssen verschwunden sein.
        Assert.Single(
            after.Comments,
            value =>
                IsField(
                    value,
                    "DYNAMIC RANGE"));

        Assert.Single(
            after.Comments,
            value =>
                IsField(
                    value,
                    "ALBUM DYNAMIC RANGE"));
    }

    [Fact]
    public void NullAlbumDr_PreservesExistingAlbumTagByteExactly()
    {
        var originalAlbumTag =
            Utf8("album dynamic range=8");

        var original =
            BuildPacket(
                Utf8("Vendor"),
                new[]
                {
                    Utf8("ARTIST=Test"),
                    Utf8("dynamic range=7"),
                    originalAlbumTag,
                    Utf8("CUSTOM=UNCHANGED")
                },
                new byte[]
                {
                    0x01,
                    0x22,
                    0x33
                });

        var modified =
            OpusTagsEditor.UpdateDynamicRangeTags(
                original,
                trackDynamicRange: 12,
                albumDynamicRange: null);

        var after =
            ParsePacket(modified);

        Assert.Equal(
            "12",
            GetSingleFieldValue(
                after.Comments,
                "DYNAMIC RANGE"));

        var preservedAlbum =
            Assert.Single(
                after.Comments,
                value =>
                    IsField(
                        value,
                        "ALBUM DYNAMIC RANGE"));

        Assert.True(
            originalAlbumTag
                .AsSpan()
                .SequenceEqual(
                    preservedAlbum));
    }

    [Fact]
    public void RemoveDynamicRangeTags_RemovesOnlyOwnedFields()
    {
        var vendor =
            Encoding.UTF8.GetBytes(
                "OpusTestVendor – 日本語");

        var comments =
            new[]
            {
                Utf8("ARTIST=Björk – 東京"),
                Utf8("TITLE=Straße – 한국어"),
                Utf8("R128_TRACK_GAIN=-573"),
                Utf8("CUSTOM_TEST=DO NOT TOUCH – 🎵"),
                Utf8("dynamic range=7"),
                Utf8("DYNAMIC RANGE=9"),
                Utf8("album dynamic range=8"),
                Utf8("ALBUM DYNAMIC RANGE=10")
            };

        var trailing =
            new byte[]
            {
                0xA5,
                0x00,
                0xFF,
                0x13,
                0x37
            };

        var original =
            BuildPacket(
                vendor,
                comments,
                trailing);

        Assert.True(
            OpusTagsEditor.HasDynamicRangeTags(
                original));

        var modified =
            OpusTagsEditor.RemoveDynamicRangeTags(
                original);

        var before =
            ParsePacket(
                original);

        var after =
            ParsePacket(
                modified);

        Assert.True(
            before.Vendor
                .AsSpan()
                .SequenceEqual(
                    after.Vendor));

        Assert.True(
            before.Trailing
                .AsSpan()
                .SequenceEqual(
                    after.Trailing));

        var beforeForeign =
            before.Comments
                .Where(
                    value =>
                        !IsOwnedDrField(value))
                .ToArray();

        Assert.Equal(
            beforeForeign.Length,
            after.Comments.Count);

        for (var index = 0;
             index < beforeForeign.Length;
             index++)
        {
            Assert.True(
                beforeForeign[index]
                    .AsSpan()
                    .SequenceEqual(
                        after.Comments[index]),
                $"Fremder Comment {index} wurde verändert.");
        }

        Assert.DoesNotContain(
            after.Comments,
            IsOwnedDrField);

        Assert.False(
            OpusTagsEditor.HasDynamicRangeTags(
                modified));
    }

    [Fact]
    public void RemoveDynamicRangeTags_WhenNoneExist_IsByteExact()
    {
        var original =
            BuildPacket(
                Utf8("Vendor"),
                new[]
                {
                    Utf8("ARTIST=Test"),
                    Utf8("TITLE=No DR tags"),
                    Utf8("CUSTOM=UNCHANGED")
                },
                new byte[]
                {
                    0x01,
                    0x22,
                    0x33
                });

        Assert.False(
            OpusTagsEditor.HasDynamicRangeTags(
                original));

        var modified =
            OpusTagsEditor.RemoveDynamicRangeTags(
                original);

        Assert.True(
            original
                .AsSpan()
                .SequenceEqual(
                    modified));
    }

    private static byte[] Utf8(
        string value)
    {
        return Encoding.UTF8.GetBytes(value);
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

        var actual =
            Encoding.ASCII.GetString(
                comment,
                0,
                equalsIndex);

        return string.Equals(
            actual,
            fieldName,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string GetSingleFieldValue(
        IReadOnlyList<byte[]> comments,
        string fieldName)
    {
        var value =
            Assert.Single(
                comments,
                item =>
                    IsField(
                        item,
                        fieldName));

        var equalsIndex =
            Array.IndexOf(
                value,
                (byte)'=');

        return Encoding.UTF8.GetString(
            value,
            equalsIndex + 1,
            value.Length - equalsIndex - 1);
    }

    private static byte[] BuildPacket(
        byte[] vendor,
        IReadOnlyList<byte[]> comments,
        byte[] trailing)
    {
        using var stream =
            new MemoryStream();

        stream.Write("OpusTags"u8);

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

        stream.Write(trailing);

        return stream.ToArray();
    }

    private static Parsed ParsePacket(
        byte[] packet)
    {
        Assert.True(
            packet.AsSpan()
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

        var count =
            ReadUInt32(
                packet,
                ref offset);

        var comments =
            new List<byte[]>();

        for (uint index = 0;
             index < count;
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

        return new Parsed(
            vendor,
            comments,
            packet.AsSpan(offset).ToArray());
    }

    private static uint ReadUInt32(
        byte[] data,
        ref int offset)
    {
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
        var result =
            data.AsSpan(
                    offset,
                    checked((int)length))
                .ToArray();

        offset +=
            result.Length;

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

    private sealed record Parsed(
        byte[] Vendor,
        IReadOnlyList<byte[]> Comments,
        byte[] Trailing);
}
