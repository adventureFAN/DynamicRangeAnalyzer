namespace DRAnalyzer.Core.Analysis;

public enum AlbumGroupingSource
{
    Metadata,
    ParentDirectory
}

public readonly record struct AlbumGroupingKey(
    AlbumGroupingSource Source,
    string Primary,
    string Secondary)
{
    public static AlbumGroupingKey Create(
        string albumArtist,
        string artist,
        string album,
        string filePath)
    {
        var normalizedAlbum =
            NormalizeText(album);

        if (!string.IsNullOrWhiteSpace(normalizedAlbum))
        {
            var groupingArtist =
                string.IsNullOrWhiteSpace(albumArtist)
                    ? artist
                    : albumArtist;

            return new AlbumGroupingKey(
                AlbumGroupingSource.Metadata,
                NormalizeText(groupingArtist),
                normalizedAlbum);
        }

        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException(
                "A file path is required when album metadata is missing.",
                nameof(filePath));
        }

        var fullPath =
            Path.GetFullPath(filePath);

        var parentDirectory =
            Path.GetDirectoryName(fullPath);

        if (string.IsNullOrWhiteSpace(parentDirectory))
        {
            parentDirectory =
                Path.GetPathRoot(fullPath) ?? fullPath;
        }

        return new AlbumGroupingKey(
            AlbumGroupingSource.ParentDirectory,
            NormalizeDirectory(parentDirectory),
            "");
    }

    private static string NormalizeText(
        string value)
    {
        return (value ?? "")
            .Trim()
            .ToUpperInvariant();
    }

    private static string NormalizeDirectory(
        string directoryPath)
    {
        return Path
            .GetFullPath(directoryPath)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
    }
}
