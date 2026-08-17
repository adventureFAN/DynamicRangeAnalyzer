using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text;
using DRAnalyzer.Core.Processes;

namespace DRAnalyzer.Core.Analysis;

public static class DynamicRangeAnalyzer
{
    private const double WindowCoefficient =
        3.0040816326530613;

    private const int HistogramBins = 10001;

    private static readonly TimeSpan FfmpegAnalysisTimeout =
        TimeSpan.FromHours(1);

    private static readonly TimeSpan FfprobeTimeout =
        TimeSpan.FromSeconds(30);

    public static DynamicRangeResult Analyze(string filePath)
    {
        var info = ReadAudioInfo(filePath);

        var windowFrames =
            (int)Math.Floor(
                info.SampleRate * WindowCoefficient);

        if (windowFrames <= 0)
            throw new InvalidOperationException(
                "Invalid analysis window size.");

        var sumSquares =
            new double[info.Channels];

        var currentPeaks =
            new double[info.Channels];

        var sumWindowRms2 =
            new double[info.Channels];

        var primaryPeak =
            new double[info.Channels];

        var secondaryPeak =
            new double[info.Channels];

        var primaryKey =
            Enumerable
                .Repeat(-100000.0, info.Channels)
                .ToArray();

        var secondaryKey =
            Enumerable
                .Repeat(-100000.0, info.Channels)
                .ToArray();

        var histograms =
            new int[info.Channels][];

        for (var channel = 0;
             channel < info.Channels;
             channel++)
        {
            histograms[channel] =
                new int[HistogramBins];
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = ExternalToolLocator.ResolveFfmpeg(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("error");

        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(filePath);

        startInfo.ArgumentList.Add("-map");
        startInfo.ArgumentList.Add("0:a:0");

        startInfo.ArgumentList.Add("-vn");
        startInfo.ArgumentList.Add("-sn");
        startInfo.ArgumentList.Add("-dn");

        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("f64le");

        startInfo.ArgumentList.Add("-acodec");
        startInfo.ArgumentList.Add("pcm_f64le");

        startInfo.ArgumentList.Add("pipe:1");

        using var process =
            Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "FFmpeg could not be started.");

        using var timeoutGuard =
            new ProcessTimeoutGuard(
                process,
                FfmpegAnalysisTimeout);

        var errorTask =
            process.StandardError.ReadToEndAsync();

        using var output =
            process.StandardOutput.BaseStream;

        var buffer =
            new byte[(1024 * 1024) + sizeof(double)];

        var leftoverBytes = 0;
        long sampleIndex = 0;
        var framesInWindow = 0;
        var windowCount = 0;

        while (true)
        {
            var bytesRead =
                output.Read(
                    buffer,
                    leftoverBytes,
                    buffer.Length - leftoverBytes);

            if (bytesRead == 0)
                break;

            var totalBytes =
                leftoverBytes + bytesRead;

            var usableBytes =
                totalBytes -
                (totalBytes % sizeof(double));

            for (var offset = 0;
                 offset < usableBytes;
                 offset += sizeof(double))
            {
                var sample =
                    BinaryPrimitives.ReadDoubleLittleEndian(
                        buffer.AsSpan(
                            offset,
                            sizeof(double)));

                if (!double.IsFinite(sample))
                    throw new InvalidOperationException(
                        "FFmpeg returned a non-finite PCM sample value.");

                var channel =
                    (int)(sampleIndex % info.Channels);

                var magnitude =
                    Math.Abs(sample);

                sumSquares[channel] +=
                    magnitude * magnitude;

                if (magnitude > currentPeaks[channel])
                    currentPeaks[channel] = magnitude;

                sampleIndex++;

                if (channel ==
                    info.Channels - 1)
                {
                    framesInWindow++;

                    if (framesInWindow ==
                        windowFrames)
                    {
                        SubmitWindow(
                            framesInWindow,
                            info.Channels,
                            sumSquares,
                            currentPeaks,
                            sumWindowRms2,
                            primaryPeak,
                            secondaryPeak,
                            primaryKey,
                            secondaryKey,
                            histograms);

                        framesInWindow = 0;
                        windowCount++;
                    }
                }
            }

            leftoverBytes =
                totalBytes - usableBytes;

            if (leftoverBytes > 0)
            {
                Buffer.BlockCopy(
                    buffer,
                    usableBytes,
                    buffer,
                    0,
                    leftoverBytes);
            }
        }

        process.WaitForExit();

        var error =
            errorTask.GetAwaiter().GetResult();

        if (timeoutGuard.TimedOut)
        {
            throw new TimeoutException(
                "FFmpeg was terminated because analysis of a single " +
                "file exceeded 60 minutes.");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"FFmpeg reported an error: {error}");
        }

        if (leftoverBytes != 0)
        {
            throw new InvalidOperationException(
                "FFmpeg returned incomplete PCM data.");
        }

        if (sampleIndex % info.Channels != 0)
        {
            throw new InvalidOperationException(
                "The PCM data contains an incomplete audio frame.");
        }

        if (framesInWindow > 0)
        {
            SubmitWindow(
                framesInWindow,
                info.Channels,
                sumSquares,
                currentPeaks,
                sumWindowRms2,
                primaryPeak,
                secondaryPeak,
                primaryKey,
                secondaryKey,
                histograms);

            windowCount++;
        }

        if (windowCount == 0)
        {
            throw new InvalidOperationException(
                "The file contains no analyzable audio data.");
        }

        var channelDr =
            new double[info.Channels];

        var channelRms =
            new double[info.Channels];

        for (var channel = 0;
             channel < info.Channels;
             channel++)
        {
            channelRms[channel] =
                Math.Sqrt(
                    sumWindowRms2[channel] /
                    windowCount);

            var target =
                Math.Max(
                    1,
                    windowCount / 5);

            var selectedCount = 0;
            var selectedPower = 0.0;

            for (var bin = HistogramBins - 1;
                 bin >= 0;
                 bin--)
            {
                var count =
                    histograms[channel][bin];

                if (count == 0)
                    continue;

                var binDb =
                    bin * 0.01 - 100.0;

                selectedCount += count;

                selectedPower +=
                    Math.Pow(
                        10.0,
                        binDb / 10.0) *
                    count;

                if (selectedCount >= target)
                    break;
            }

            var selectedPeak =
                secondaryPeak[channel] > 0
                    ? secondaryPeak[channel]
                    : primaryPeak[channel];

            var dr = 0.0;

            if (selectedPeak > 0 &&
                selectedCount > 0)
            {
                var loudRms =
                    Math.Sqrt(
                        selectedPower /
                        selectedCount);

                if (loudRms != 0)
                {
                    dr =
                        -20.0 *
                        Math.Log10(
                            loudRms /
                            selectedPeak);

                    if (dr < 0)
                    {
                        dr =
                            Math.Max(
                                -20.0 *
                                Math.Log10(
                                    loudRms /
                                    primaryPeak[channel]),
                                0.0);
                    }
                }
            }

            channelDr[channel] = dr;
        }

        var internalTrackDr =
            channelDr.Average();

        var publicChannelDr =
            channelDr
                .Select(value => (double)(float)value)
                .ToArray();

        var publicChannelRms =
            channelRms
                .Select(value => (float)value)
                .ToArray();

        var publicChannelPeak =
            primaryPeak
                .Select(value => (float)value)
                .ToArray();

        var reportPeakLinear =
            publicChannelPeak.Max();

        var rmsSquareSum = 0.0;

        foreach (var rms in publicChannelRms)
        {
            var square =
                (float)(rms * rms);

            rmsSquareSum += square;
        }

        var reportRmsLinear =
            Math.Sqrt(
                rmsSquareSum /
                info.Channels);

        var channelPeakDb =
            publicChannelPeak
                .Select(value =>
                    ToDecibels(value))
                .ToArray();

        var channelRmsDb =
            publicChannelRms
                .Select(value =>
                    ToDecibels(value))
                .ToArray();

        return new DynamicRangeResult(
            DynamicRange:
                (float)internalTrackDr,

            PeakDb:
                ToDecibels(
                    reportPeakLinear),

            RmsDb:
                ToDecibels(
                    reportRmsLinear),

            Channels:
                info.Channels,

            SampleRate:
                info.SampleRate,

            BlockCount:
                windowCount,

            ChannelDynamicRange:
                publicChannelDr,

            ChannelPeakDb:
                channelPeakDb,

            ChannelRmsDb:
                channelRmsDb);
    }

    private static void SubmitWindow(
        int frames,
        int channels,
        double[] sumSquares,
        double[] currentPeaks,
        double[] sumWindowRms2,
        double[] primaryPeak,
        double[] secondaryPeak,
        double[] primaryKey,
        double[] secondaryKey,
        int[][] histograms)
    {
        for (var channel = 0;
             channel < channels;
             channel++)
        {
            var rms2 =
                2.0 *
                sumSquares[channel] /
                frames;

            var rms =
                Math.Sqrt(rms2);

            sumWindowRms2[channel] +=
                rms2;

            var peak =
                currentPeaks[channel];

            if (peak > 0)
            {
                var peakKeyDb =
                    0.01 *
                    LRound(
                        2000.0 *
                        Math.Log10(peak));

                if (peakKeyDb >
                    primaryKey[channel])
                {
                    secondaryPeak[channel] =
                        primaryPeak[channel];

                    secondaryKey[channel] =
                        primaryKey[channel];

                    primaryPeak[channel] =
                        peak;

                    primaryKey[channel] =
                        peakKeyDb;
                }
                else if (peakKeyDb >
                         secondaryKey[channel])
                {
                    secondaryPeak[channel] =
                        peak;

                    secondaryKey[channel] =
                        peakKeyDb;
                }
            }

            if (rms != 0)
            {
                var rmsKeyDb =
                    0.01 *
                    LRound(
                        2000.0 *
                        Math.Log10(rms));

                rmsKeyDb =
                    Math.Clamp(
                        rmsKeyDb,
                        -100.0,
                        0.0);

                var bin =
                    LRound(
                        100.0 *
                        rmsKeyDb +
                        10000.0);

                bin =
                    Math.Clamp(
                        bin,
                        0,
                        HistogramBins - 1);

                histograms[channel][bin]++;
            }

            sumSquares[channel] = 0;
            currentPeaks[channel] = 0;
        }
    }

    private static int LRound(double value)
    {
        return checked(
            (int)Math.Round(
                value,
                0,
                MidpointRounding.AwayFromZero));
    }

    private static double ToDecibels(
        double value)
    {
        return value <= 0
            ? double.NegativeInfinity
            : 20.0 * Math.Log10(value);
    }

    private static AudioInfo ReadAudioInfo(
        string filePath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ExternalToolLocator.ResolveFfprobe(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.StandardOutputEncoding = Encoding.UTF8;

        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("error");

        startInfo.ArgumentList.Add("-select_streams");
        startInfo.ArgumentList.Add("a:0");

        startInfo.ArgumentList.Add("-show_entries");
        startInfo.ArgumentList.Add(
            "stream=sample_rate,channels");

        startInfo.ArgumentList.Add("-of");
        startInfo.ArgumentList.Add("json");

        startInfo.ArgumentList.Add(filePath);

        using var process =
            Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "FFprobe could not be started.");

        using var timeoutGuard =
            new ProcessTimeoutGuard(
                process,
                FfprobeTimeout);

        var outputTask =
            process.StandardOutput.ReadToEndAsync();

        var errorTask =
            process.StandardError.ReadToEndAsync();

        process.WaitForExit();

        var output =
            outputTask.GetAwaiter().GetResult();

        var error =
            errorTask.GetAwaiter().GetResult();

        if (timeoutGuard.TimedOut)
        {
            throw new TimeoutException(
                "FFprobe was terminated because reading the audio parameters " +
                "exceeded 30 seconds.");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"FFprobe reported an error: {error}");
        }

        using var document =
            JsonDocument.Parse(output);

        if (!document.RootElement
                .TryGetProperty(
                    "streams",
                    out var streams) ||
            streams.GetArrayLength() == 0)
        {
            throw new InvalidOperationException(
                "No audio stream was found.");
        }

        var stream =
            streams[0];

        var sampleRate =
            int.Parse(
                stream
                    .GetProperty("sample_rate")
                    .ToString(),
                CultureInfo.InvariantCulture);

        var channels =
            stream
                .GetProperty("channels")
                .GetInt32();

        if (sampleRate <= 0 ||
            channels <= 0)
        {
            throw new InvalidOperationException(
                "Invalid audio parameters.");
        }

        return new AudioInfo(
            sampleRate,
            channels);
    }

    private sealed record AudioInfo(
        int SampleRate,
        int Channels);
}

