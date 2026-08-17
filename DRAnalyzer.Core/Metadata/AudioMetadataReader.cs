using System.Diagnostics;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using DRAnalyzer.Core.Models;
using DRAnalyzer.Core.Processes;

namespace DRAnalyzer.Core.Metadata;

public static class AudioMetadataReader
{
    private static readonly TimeSpan FfprobeTimeout =
        TimeSpan.FromSeconds(30);

    private static readonly Regex CombinedTrackRegex =
        new(
            @"^\s*(\d+)\s*(?:/|of)\s*(\d+)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static AudioMetadata Read(string filePath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ExternalToolLocator.ResolveFfprobe(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("error");

        startInfo.ArgumentList.Add("-select_streams");
        startInfo.ArgumentList.Add("a:0");

        startInfo.ArgumentList.Add("-show_entries");
        startInfo.ArgumentList.Add("stream_tags:format_tags");

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
                "FFprobe was terminated because reading metadata " +
                "exceeded 30 seconds.");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"FFprobe reported an error: {error}");
        }

        using var document =
            JsonDocument.Parse(output);

        var tags =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        var root =
            document.RootElement;

        // Zuerst Container-Tags übernehmen.
        if (root.TryGetProperty("format", out var format) &&
            format.TryGetProperty("tags", out var formatTags))
        {
            AddTags(formatTags, tags);
        }

        // Danach Tags des ersten Audio-Streams.
        // Gleichnamige Stream-Tags dürfen Container-Tags überschreiben.
        if (root.TryGetProperty("streams", out var streams) &&
            streams.ValueKind == JsonValueKind.Array &&
            streams.GetArrayLength() > 0)
        {
            var stream =
                streams[0];

            if (stream.TryGetProperty("tags", out var streamTags))
            {
                AddTags(streamTags, tags);
            }
        }

        var title =
            GetTag(tags, "TITLE");

        if (string.IsNullOrWhiteSpace(title))
        {
            title =
                Path.GetFileNameWithoutExtension(filePath);
        }

        var track =
            GetTag(
                tags,
                "TRACK",
                "TRACKNUMBER");

        var trackTotal =
            GetTag(
                tags,
                "TRACKTOTAL",
                "TOTALTRACKS");

        NormalizeTrack(
            ref track,
            ref trackTotal);

        return new AudioMetadata(
            Artist: GetTag(tags, "ARTIST"),
            Album: GetTag(tags, "ALBUM"),
            Title: title,

            AlbumArtist: GetTag(
                tags,
                "ALBUMARTIST",
                "ALBUM_ARTIST",
                "ALBUM ARTIST"),

            Track: track,
            TrackTotal: trackTotal,

            Date: GetTag(
                tags,
                "DATE",
                "YEAR"),

            Genre: GetTag(tags, "GENRE"),
            Composer: GetTag(tags, "COMPOSER"),

            DynamicRange: GetTag(
                tags,
                "DYNAMIC RANGE",
                "DYNAMIC_RANGE",
                "DR"),

            AlbumDynamicRange: GetTag(
                tags,
                "ALBUM DYNAMIC RANGE",
                "ALBUM_DYNAMIC_RANGE",
                "ALBUM DR",
                "ALBUMDR"),

            Tags: tags
        );
    }

    private static void NormalizeTrack(
        ref string track,
        ref string trackTotal)
    {
        if (string.IsNullOrWhiteSpace(track))
            return;

        track = track.Trim();

        var match =
            CombinedTrackRegex.Match(track);

        if (match.Success)
        {
            track =
                NormalizeNumber(
                    match.Groups[1].Value);

            if (string.IsNullOrWhiteSpace(trackTotal))
            {
                trackTotal =
                    NormalizeNumber(
                        match.Groups[2].Value);
            }

            return;
        }

        track =
            NormalizeNumber(track);

        if (!string.IsNullOrWhiteSpace(trackTotal))
        {
            trackTotal =
                NormalizeNumber(trackTotal);
        }
    }

    private static string NormalizeNumber(
        string value)
    {
        value = value.Trim();

        return int.TryParse(value, out var number)
            ? number.ToString()
            : value;
    }

    private static void AddTags(
        JsonElement source,
        Dictionary<string, string> target)
    {
        if (source.ValueKind != JsonValueKind.Object)
            return;

        foreach (var property in source.EnumerateObject())
        {
            target[property.Name] =
                property.Value.ToString();
        }
    }

    private static string GetTag(
        IReadOnlyDictionary<string, string> tags,
        params string[] names)
    {
        foreach (var name in names)
        {
            if (tags.TryGetValue(name, out var value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return "";
    }
}

