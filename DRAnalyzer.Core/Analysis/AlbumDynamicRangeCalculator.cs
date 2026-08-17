namespace DRAnalyzer.Core.Analysis;

public static class AlbumDynamicRangeCalculator
{
    public static int Calculate(IEnumerable<int> trackDynamicRanges)
    {
        ArgumentNullException.ThrowIfNull(trackDynamicRanges);

        var values = trackDynamicRanges.ToArray();

        if (values.Length == 0)
        {
            throw new ArgumentException(
                "Mindestens ein Track-DR-Wert wird benötigt.",
                nameof(trackDynamicRanges));
        }

        var average = values.Average();

        return (int)Math.Round(
            average,
            0,
            MidpointRounding.AwayFromZero);
    }
}
