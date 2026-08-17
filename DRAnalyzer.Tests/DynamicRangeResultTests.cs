using DRAnalyzer.Core.Analysis;

namespace DRAnalyzer.Tests;

public sealed class DynamicRangeResultTests
{
    [Fact]
    public void RoundedDynamicRange_PreservesReferenceSinglePrecisionBoundary()
    {
        const double internalDr = 12.4999999;

        // In Double Precision liegt der Wert noch unter 12.5.
        Assert.Equal(
            12,
            (int)Math.Round(
                internalDr,
                0,
                MidpointRounding.AwayFromZero));

        // Die Referenz-Ausgabe reduziert den öffentlichen DR-Wert
        // vor der Rundung auf Single Precision. Dieser Wert wird 12.5f.
        Assert.Equal(
            12.5f,
            (float)internalDr);

        var result =
            new DynamicRangeResult(
                DynamicRange: internalDr,
                PeakDb: 0,
                RmsDb: 0,
                Channels: 2,
                SampleRate: 48000,
                BlockCount: 1,
                ChannelDynamicRange: [internalDr, internalDr],
                ChannelPeakDb: [0, 0],
                ChannelRmsDb: [0, 0]);

        Assert.Equal(
            13,
            result.RoundedDynamicRange);
    }
}
