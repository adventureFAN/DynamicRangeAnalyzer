using System.Buffers.Binary;
using System.Text;

namespace DRAnalyzer.Core.Tagging;

public static class VorbisCommentEditor
{
    private static ReadOnlySpan<byte> Signature =>
        new byte[] { 0x03, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s' };

    private const string TrackDynamicRangeField =
        "DYNAMIC RANGE";

    private const string AlbumDynamicRangeField =
        "ALBUM DYNAMIC RANGE";

    public static byte[] UpdateDynamicRangeTags(
        byte[] packet,
        int trackDynamicRange,
        int? albumDynamicRange)
    {
        ArgumentNullException.ThrowIfNull(packet);

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

        var parsed =
            Parse(packet);

        var comments =
            new List<byte[]>();

        foreach (var comment in parsed.Comments)
        {
            if (IsField(
                    comment,
                    TrackDynamicRangeField))
            {
                continue;
            }

            if (albumDynamicRange.HasValue &&
                IsField(
                    comment,
                    AlbumDynamicRangeField))
            {
                continue;
            }

            // Alles Fremde bleibt bytegenau und in derselben Reihenfolge.
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

        var result =
            Build(
                parsed.Vendor,
                comments,
                parsed.TrailingData);

        ValidateWrittenTags(
            result,
            trackDynamicRange,
            albumDynamicRange);

        return result;
    }

    public static bool HasDynamicRangeTags(
        byte[] packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        var parsed =
            Parse(packet);

        return
            parsed.Comments.Any(
                comment =>
                    IsField(
                        comment,
                        TrackDynamicRangeField) ||
                    IsField(
                        comment,
                        AlbumDynamicRangeField));
    }

    public static byte[] RemoveDynamicRangeTags(
        byte[] packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        var parsed =
            Parse(packet);

        var comments =
            parsed.Comments
                .Where(
                    comment =>
                        !IsField(
                            comment,
                            TrackDynamicRangeField) &&
                        !IsField(
                            comment,
                            AlbumDynamicRangeField))
                .ToArray();

        if (comments.Length ==
            parsed.Comments.Count)
        {
            return packet.ToArray();
        }

        var result =
            Build(
                parsed.Vendor,
                comments,
                parsed.TrailingData);

        ValidateRemovedTags(result);

        return result;
    }

    private static ParsedVorbisComment Parse(
        byte[] packet)
    {
        if (!packet
                .AsSpan()
                .StartsWith(Signature))
        {
            throw new InvalidDataException(
                "Das Paket ist kein Vorbis-Comment-Header.");
        }

        var offset =
            Signature.Length;

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
            var commentLength =
                ReadUInt32(
                    packet,
                    ref offset);

            comments.Add(
                ReadBytes(
                    packet,
                    ref offset,
                    commentLength));
        }

        if (offset >= packet.Length)
        {
            throw new InvalidDataException(
                "Der Vorbis-Comment-Header besitzt kein Framing-Bit.");
        }

        // Das Framing-Bit ist Bit 0 des nächsten Octets.
        // Wir erhalten das gesamte Ende bytegenau, damit auch ungewöhnliche
        // Padding-/Zusatzbits nicht von DRAnalyzer normalisiert werden.
        var trailingData =
            packet
                .AsSpan(offset)
                .ToArray();

        if ((trailingData[0] & 0x01) == 0)
        {
            throw new InvalidDataException(
                "Das Vorbis-Comment-Framing-Bit ist nicht gesetzt.");
        }

        return new ParsedVorbisComment(
            vendor,
            comments,
            trailingData);
    }

    private static byte[] Build(
        byte[] vendor,
        IReadOnlyList<byte[]> comments,
        byte[] trailingData)
    {
        if (trailingData.Length == 0 ||
            (trailingData[0] & 0x01) == 0)
        {
            throw new InvalidDataException(
                "Ungültiges Vorbis-Comment-Framing-Bit.");
        }

        using var stream =
            new MemoryStream();

        stream.Write(Signature);

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

    private static void ValidateWrittenTags(
        byte[] packet,
        int expectedTrackDynamicRange,
        int? expectedAlbumDynamicRange)
    {
        var parsed =
            Parse(packet);

        var trackValues =
            parsed.Comments
                .Where(
                    comment =>
                        IsField(
                            comment,
                            TrackDynamicRangeField))
                .Select(GetFieldValue)
                .ToArray();

        AssertSingleValue(
            trackValues,
            expectedTrackDynamicRange.ToString(),
            "Track-DR");

        if (!expectedAlbumDynamicRange.HasValue)
            return;

        var albumValues =
            parsed.Comments
                .Where(
                    comment =>
                        IsField(
                            comment,
                            AlbumDynamicRangeField))
                .Select(GetFieldValue)
                .ToArray();

        AssertSingleValue(
            albumValues,
            expectedAlbumDynamicRange.Value.ToString(),
            "Album-DR");
    }

    private static void ValidateRemovedTags(
        byte[] packet)
    {
        var parsed =
            Parse(packet);

        if (parsed.Comments.Any(
                comment =>
                    IsField(
                        comment,
                        TrackDynamicRangeField) ||
                    IsField(
                        comment,
                        AlbumDynamicRangeField)))
        {
            throw new InvalidDataException(
                "Die DR-Tags konnten nach dem Entfernen " +
                "nicht sicher verifiziert werden.");
        }
    }

    private static void AssertSingleValue(
        string[] values,
        string expected,
        string name)
    {
        if (values.Length != 1 ||
            values[0] != expected)
        {
            throw new InvalidDataException(
                $"{name} konnte nach dem Schreiben " +
                "nicht eindeutig verifiziert werden.");
        }
    }

    private static uint ReadUInt32(
        byte[] data,
        ref int offset)
    {
        if (offset > data.Length - 4)
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

    private sealed record ParsedVorbisComment(
        byte[] Vendor,
        IReadOnlyList<byte[]> Comments,
        byte[] TrailingData);
}
