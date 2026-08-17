using DRAnalyzer.Core.Models;

namespace DRAnalyzer.Tests;

public class AudioMetadataOwnershipTests
{
    [Fact]
    public void ExactOwnedFields_AreRecognizedCaseInsensitively()
    {
        var metadata = CreateMetadata(
            new Dictionary<string, string>
            {
                ["dynamic range"] = "DR12",
                ["ALBUM DYNAMIC RANGE"] = "DR13"
            });

        Assert.True(metadata.HasOwnedDynamicRangeTag);
        Assert.True(metadata.HasOwnedAlbumDynamicRangeTag);
    }

    [Fact]
    public void CompatibilityAliases_AreNotTreatedAsOwnedFields()
    {
        var metadata = CreateMetadata(
            new Dictionary<string, string>
            {
                ["DR"] = "DR12",
                ["DYNAMIC_RANGE"] = "DR12",
                ["ALBUM DR"] = "DR13",
                ["ALBUM_DYNAMIC_RANGE"] = "DR13"
            });

        Assert.False(metadata.HasOwnedDynamicRangeTag);
        Assert.False(metadata.HasOwnedAlbumDynamicRangeTag);
    }

    [Fact]
    public void EmptyOwnedFields_AreStillRecognizedForRemoval()
    {
        var metadata = CreateMetadata(
            new Dictionary<string, string>
            {
                ["DYNAMIC RANGE"] = "   ",
                ["ALBUM DYNAMIC RANGE"] = ""
            });

        Assert.True(metadata.HasOwnedDynamicRangeTag);
        Assert.True(metadata.HasOwnedAlbumDynamicRangeTag);
    }

    private static AudioMetadata CreateMetadata(
        IReadOnlyDictionary<string, string> tags)
    {
        return new AudioMetadata(
            Artist: "",
            Album: "",
            Title: "Test",
            AlbumArtist: "",
            Track: "",
            TrackTotal: "",
            Date: "",
            Genre: "",
            Composer: "",
            DynamicRange: "",
            AlbumDynamicRange: "",
            Tags: tags);
    }
}
