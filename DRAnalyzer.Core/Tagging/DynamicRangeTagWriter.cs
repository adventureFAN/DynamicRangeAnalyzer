namespace DRAnalyzer.Core.Tagging;

public static class DynamicRangeTagWriter
{
    public static bool CanWrite(
        string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        var extension =
            Path.GetExtension(filePath);

        return
            string.Equals(
                extension,
                ".flac",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                extension,
                ".opus",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                extension,
                ".mp3",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                extension,
                ".ogg",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                extension,
                ".m4a",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                extension,
                ".wav",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                extension,
                ".aiff",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                extension,
                ".aif",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                extension,
                ".ape",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                extension,
                ".wv",
                StringComparison.OrdinalIgnoreCase);
    }

    public static bool CanRemove(
        string filePath)
    {
        return CanWrite(
            filePath);
    }

    public static void Write(
        string filePath,
        int trackDynamicRange,
        int? albumDynamicRange)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            filePath);

        var extension =
            Path.GetExtension(filePath);

        if (string.Equals(
                extension,
                ".flac",
                StringComparison.OrdinalIgnoreCase))
        {
            FlacDynamicRangeTagWriter.Write(
                filePath,
                trackDynamicRange,
                albumDynamicRange);

            return;
        }

        if (string.Equals(
                extension,
                ".opus",
                StringComparison.OrdinalIgnoreCase))
        {
            OpusDynamicRangeTagWriter.Write(
                filePath,
                trackDynamicRange,
                albumDynamicRange);

            return;
        }

        if (string.Equals(
                extension,
                ".mp3",
                StringComparison.OrdinalIgnoreCase))
        {
            Mp3DynamicRangeTagWriter.Write(
                filePath,
                trackDynamicRange,
                albumDynamicRange);

            return;
        }

        if (string.Equals(
                extension,
                ".ogg",
                StringComparison.OrdinalIgnoreCase))
        {
            VorbisDynamicRangeTagWriter.Write(
                filePath,
                trackDynamicRange,
                albumDynamicRange);

            return;
        }

        if (string.Equals(
                extension,
                ".m4a",
                StringComparison.OrdinalIgnoreCase))
        {
            M4aDynamicRangeTagWriter.Write(
                filePath,
                trackDynamicRange,
                albumDynamicRange);

            return;
        }

        if (string.Equals(
                extension,
                ".wav",
                StringComparison.OrdinalIgnoreCase))
        {
            WavDynamicRangeTagWriter.Write(
                filePath,
                trackDynamicRange,
                albumDynamicRange);

            return;
        }

        if (string.Equals(
                extension,
                ".aiff",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                extension,
                ".aif",
                StringComparison.OrdinalIgnoreCase))
        {
            AiffDynamicRangeTagWriter.Write(
                filePath,
                trackDynamicRange,
                albumDynamicRange);

            return;
        }

        if (string.Equals(
                extension,
                ".ape",
                StringComparison.OrdinalIgnoreCase))
        {
            ApeDynamicRangeTagWriter.Write(
                filePath,
                trackDynamicRange,
                albumDynamicRange);

            return;
        }

        if (string.Equals(
                extension,
                ".wv",
                StringComparison.OrdinalIgnoreCase))
        {
            WavPackDynamicRangeTagWriter.Write(
                filePath,
                trackDynamicRange,
                albumDynamicRange);

            return;
        }

        throw new NotSupportedException(
            $"Das Audioformat '{extension}' " +
            "wird für das Schreiben von DR-Tags " +
            "noch nicht unterstützt.");
    }

    public static void Remove(
        string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            filePath);

        var extension =
            Path.GetExtension(filePath);

        if (string.Equals(
                extension,
                ".flac",
                StringComparison.OrdinalIgnoreCase))
        {
            FlacDynamicRangeTagWriter.Remove(
                filePath);

            return;
        }

        if (string.Equals(
                extension,
                ".opus",
                StringComparison.OrdinalIgnoreCase))
        {
            OpusDynamicRangeTagWriter.Remove(
                filePath);

            return;
        }

        if (string.Equals(
                extension,
                ".mp3",
                StringComparison.OrdinalIgnoreCase))
        {
            Mp3DynamicRangeTagWriter.Remove(
                filePath);

            return;
        }

        if (string.Equals(
                extension,
                ".ogg",
                StringComparison.OrdinalIgnoreCase))
        {
            VorbisDynamicRangeTagWriter.Remove(
                filePath);

            return;
        }

        if (string.Equals(
                extension,
                ".m4a",
                StringComparison.OrdinalIgnoreCase))
        {
            M4aDynamicRangeTagWriter.Remove(
                filePath);

            return;
        }

        if (string.Equals(
                extension,
                ".wav",
                StringComparison.OrdinalIgnoreCase))
        {
            WavDynamicRangeTagWriter.Remove(
                filePath);

            return;
        }

        if (string.Equals(
                extension,
                ".aiff",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                extension,
                ".aif",
                StringComparison.OrdinalIgnoreCase))
        {
            AiffDynamicRangeTagWriter.Remove(
                filePath);

            return;
        }

        if (string.Equals(
                extension,
                ".ape",
                StringComparison.OrdinalIgnoreCase))
        {
            ApeDynamicRangeTagWriter.Remove(
                filePath);

            return;
        }

        if (string.Equals(
                extension,
                ".wv",
                StringComparison.OrdinalIgnoreCase))
        {
            WavPackDynamicRangeTagWriter.Remove(
                filePath);

            return;
        }

        throw new NotSupportedException(
            $"Das Audioformat '{extension}' " +
            "wird für das Entfernen von DR-Tags " +
            "noch nicht unterstützt.");
    }
}
