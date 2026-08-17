using DRAnalyzer.Core.Analysis;

namespace DRAnalyzer.Tests;

public sealed class AlbumGroupingKeyTests
{
    [Fact]
    public void AlbumMetadata_PrefersAlbumArtist()
    {
        var first =
            AlbumGroupingKey.Create(
                "Various Artists",
                "Artist A",
                "Compilation",
                Path.Combine("Music", "A.flac"));

        var second =
            AlbumGroupingKey.Create(
                "Various Artists",
                "Artist B",
                "Compilation",
                Path.Combine("Other", "B.flac"));

        Assert.Equal(first, second);
        Assert.Equal(
            AlbumGroupingSource.Metadata,
            first.Source);
    }

    [Fact]
    public void MissingAlbumArtist_FallsBackToArtist()
    {
        var first =
            AlbumGroupingKey.Create(
                "",
                "Tool",
                "10,000 Days",
                Path.Combine("Music", "01.opus"));

        var second =
            AlbumGroupingKey.Create(
                "",
                "Tool",
                "10,000 Days",
                Path.Combine("Elsewhere", "02.opus"));

        Assert.Equal(first, second);
    }

    [Fact]
    public void Metadata_IsNormalizedForGrouping()
    {
        var first =
            AlbumGroupingKey.Create(
                "  Tool  ",
                "",
                "  10,000 Days ",
                Path.Combine("Music", "01.opus"));

        var second =
            AlbumGroupingKey.Create(
                "tool",
                "",
                "10,000 DAYS",
                Path.Combine("Other", "02.opus"));

        Assert.Equal(first, second);
    }

    [Fact]
    public void MissingAlbum_UsesParentDirectory()
    {
        var albumDirectory =
            Path.Combine(
                Path.GetTempPath(),
                "DRAnalyzer-AlbumGrouping",
                "Tool",
                "10,000 Days");

        var first =
            AlbumGroupingKey.Create(
                "",
                "Tool",
                "",
                Path.Combine(albumDirectory, "01.opus"));

        var second =
            AlbumGroupingKey.Create(
                "",
                "Different Artist",
                "",
                Path.Combine(albumDirectory, "02.opus"));

        Assert.Equal(first, second);
        Assert.Equal(
            AlbumGroupingSource.ParentDirectory,
            first.Source);
    }

    [Fact]
    public void MissingAllMetadata_SameParentDirectory_UsesSameGroup()
    {
        var albumDirectory =
            Path.Combine(
                Path.GetTempPath(),
                "DRAnalyzer-AlbumGrouping",
                "MetadataLessAlbum");

        var first =
            AlbumGroupingKey.Create(
                "",
                "",
                "",
                Path.Combine(albumDirectory, "Track A.flac"));

        var second =
            AlbumGroupingKey.Create(
                "",
                "",
                "",
                Path.Combine(albumDirectory, "Track B.flac"));

        Assert.Equal(first, second);
    }

    [Fact]
    public void MissingAlbum_DifferentParentDirectories_AreDifferentGroups()
    {
        var baseDirectory =
            Path.Combine(
                Path.GetTempPath(),
                "DRAnalyzer-AlbumGrouping");

        var first =
            AlbumGroupingKey.Create(
                "",
                "",
                "",
                Path.Combine(baseDirectory, "Album A", "01.flac"));

        var second =
            AlbumGroupingKey.Create(
                "",
                "",
                "",
                Path.Combine(baseDirectory, "Album B", "01.flac"));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void MetadataAndDirectoryFallback_CannotCollide()
    {
        var filePath =
            Path.Combine(
                Path.GetTempPath(),
                "DRAnalyzer-AlbumGrouping",
                "Album",
                "01.flac");

        var metadataKey =
            AlbumGroupingKey.Create(
                "",
                "Artist",
                "Album",
                filePath);

        var directoryKey =
            AlbumGroupingKey.Create(
                "",
                "Artist",
                "",
                filePath);

        Assert.NotEqual(metadataKey, directoryKey);
    }
}
