namespace DRAnalyzer.Core.Analysis;

public sealed record DynamicRangeResult(
    double DynamicRange,
    double PeakDb,
    double RmsDb,
    int Channels,
    int SampleRate,
    int BlockCount,
    IReadOnlyList<double> ChannelDynamicRange,
    IReadOnlyList<double> ChannelPeakDb,
    IReadOnlyList<double> ChannelRmsDb)
{
    public int RoundedDynamicRange
    {
        get
        {
            // Absichtlich zuerst auf Single Precision reduzieren.
            // Dieser öffentliche DR-Wert ist gegen foo_dr_meter 1.0.8
            // referenzvalidiert; direktes Math.Round(double) kann an
            // Float-Halbgrenzen ein anderes Ergebnis liefern.
            var publicDr = (float)DynamicRange;
            return (int)(publicDr + 0.5f);
        }
    }
}
