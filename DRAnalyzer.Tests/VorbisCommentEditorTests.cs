using System.Buffers.Binary;
using System.Text;
using DRAnalyzer.Core.Tagging;

namespace DRAnalyzer.Tests;

public sealed class VorbisCommentEditorTests
{
    [Fact]
    public void Update_PreservesVendorForeignCommentsAndTrailingData()
    {
        var original =
            BuildCommentPacket(
                "Unit Test Vendor"u8.ToArray(),
                new[]
                {
                    Utf8("TITLE=Björk – Jóga"),
                    Utf8("REPLAYGAIN_TRACK_GAIN=-7.25 dB"),
                    Utf8("dynamic range=3"),
                    Utf8("DYNAMIC RANGE=4"),
                    Utf8("album dynamic range=5"),
                    Utf8("CUSTOM=unchanged")
                },
                new byte[] { 0x01, 0xA4, 0x00 });

        var before =
            ParseCommentPacket(original);

        var updated =
            VorbisCommentEditor.UpdateDynamicRangeTags(
                original,
                trackDynamicRange: 12,
                albumDynamicRange: 13);

        var after =
            ParseCommentPacket(updated);

        Assert.True(
            before.Vendor.AsSpan().SequenceEqual(after.Vendor));

        Assert.True(
            before.TrailingData.AsSpan().SequenceEqual(after.TrailingData));

        var expectedForeign =
            before.Comments
                .Where(comment => !IsOwned(comment))
                .ToArray();

        var actualForeign =
            after.Comments
                .Where(comment => !IsOwned(comment))
                .ToArray();

        Assert.Equal(
            expectedForeign.Length,
            actualForeign.Length);

        for (var index = 0;
             index < expectedForeign.Length;
             index++)
        {
            Assert.True(
                expectedForeign[index]
                    .AsSpan()
                    .SequenceEqual(actualForeign[index]));
        }

        Assert.Equal(
            "12",
            GetSingleValue(
                after.Comments,
                "DYNAMIC RANGE"));

        Assert.Equal(
            "13",
            GetSingleValue(
                after.Comments,
                "ALBUM DYNAMIC RANGE"));
    }

    [Fact]
    public void TrackOnlyWrite_PreservesExistingAlbumDynamicRangeByteExactly()
    {
        var albumComment =
            Utf8("album dynamic range=99");

        var original =
            BuildCommentPacket(
                "Vendor"u8.ToArray(),
                new[]
                {
                    Utf8("TITLE=Track"),
                    albumComment,
                    Utf8("DYNAMIC RANGE=5")
                },
                new byte[] { 0x01 });

        var updated =
            VorbisCommentEditor.UpdateDynamicRangeTags(
                original,
                trackDynamicRange: 10,
                albumDynamicRange: null);

        var parsed =
            ParseCommentPacket(updated);

        Assert.Equal(
            "10",
            GetSingleValue(
                parsed.Comments,
                "DYNAMIC RANGE"));

        var preservedAlbum =
            Assert.Single(
                parsed.Comments,
                comment =>
                    IsField(
                        comment,
                        "ALBUM DYNAMIC RANGE"));

        Assert.True(
            albumComment
                .AsSpan()
                .SequenceEqual(preservedAlbum));
    }

    [Fact]
    public void Remove_RemovesAllOwnedFields_AndNoOpIsByteExact()
    {
        var original =
            BuildCommentPacket(
                "Vendor"u8.ToArray(),
                new[]
                {
                    Utf8("TITLE=Track"),
                    Utf8("dynamic range=5"),
                    Utf8("DYNAMIC RANGE=6"),
                    Utf8("album dynamic range=7"),
                    Utf8("GENRE=Rock")
                },
                new byte[] { 0x01 });

        var removed =
            VorbisCommentEditor.RemoveDynamicRangeTags(
                original);

        var parsed =
            ParseCommentPacket(removed);

        Assert.DoesNotContain(
            parsed.Comments,
            IsOwned);

        Assert.Equal(
            2,
            parsed.Comments.Count);

        var secondRemove =
            VorbisCommentEditor.RemoveDynamicRangeTags(
                removed);

        Assert.True(
            removed
                .AsSpan()
                .SequenceEqual(secondRemove));
    }

    private static byte[] BuildCommentPacket(
        byte[] vendor,
        IReadOnlyList<byte[]> comments,
        byte[] trailingData)
    {
        using var stream =
            new MemoryStream();

        stream.WriteByte(0x03);
        stream.Write("vorbis"u8);

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

        stream.Write(trailingData);

        return stream.ToArray();
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

    private static byte[] Utf8(
        string value)
    {
        return Encoding.UTF8.GetBytes(value);
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

    private static uint ReadUInt32(
        byte[] data,
        ref int offset)
    {
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
        var result =
            data
                .AsSpan(
                    offset,
                    checked((int)length))
                .ToArray();

        offset +=
            result.Length;

        return result;
    }

    private sealed record ParsedComment(
        byte[] Vendor,
        IReadOnlyList<byte[]> Comments,
        byte[] TrailingData);
}
