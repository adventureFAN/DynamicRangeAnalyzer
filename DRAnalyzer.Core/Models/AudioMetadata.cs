namespace DRAnalyzer.Core.Models;

public sealed record AudioMetadata(
    string Artist,
    string Album,
    string Title,
    string AlbumArtist,
    string Track,
    string TrackTotal,
    string Date,
    string Genre,
    string Composer,
    string DynamicRange,
    string AlbumDynamicRange,
    IReadOnlyDictionary<string, string> Tags
)
{
    public bool HasOwnedDynamicRangeTag =>
        HasTag("DYNAMIC RANGE");

    public bool HasOwnedAlbumDynamicRangeTag =>
        HasTag("ALBUM DYNAMIC RANGE");

    private bool HasTag(string name)
    {
        return Tags.Keys.Any(
            key =>
                string.Equals(
                    key,
                    name,
                    StringComparison.OrdinalIgnoreCase));
    }
}
