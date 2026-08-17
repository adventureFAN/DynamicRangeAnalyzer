using DRAnalyzer.Core.Analysis;

namespace DRAnalyzer.Tests;

public sealed class AlbumDynamicRangeCalculatorTests
{
    [Fact]
    public void Tool10000Days_MatchesFooDrMeter108()
    {
        int[] trackDr =
        {
            7,
            7,
            12,
            7,
            8,
            8,
            7,
            10,
            11,
            7,
            8
        };

        var result =
            AlbumDynamicRangeCalculator.Calculate(trackDr);

        Assert.Equal(8, result);
    }

    [Fact]
    public void EmptyAlbum_IsRejected()
    {
        Assert.Throws<ArgumentException>(
            () => AlbumDynamicRangeCalculator.Calculate(
                Array.Empty<int>()));
    }
}
