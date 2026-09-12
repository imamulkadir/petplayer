using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using PetPlayer.Helpers;
using PetPlayer.Models;

namespace PetPlayer.Services;

/// <summary>
/// Parses SRT/VTT subtitle files into timestamped entries, and locates
/// candidate subtitle files sitting next to a media file. Parsing is
/// deliberately tolerant: a malformed block is skipped rather than aborting
/// the whole file, so a single bad cue never makes a subtitle unusable.
/// </summary>
public sealed partial class SubtitleService
{
    [GeneratedRegex(@"(\d{1,2}):(\d{2}):(\d{2})[.,](\d{1,3})\s*-->\s*(\d{1,2}):(\d{2}):(\d{2})[.,](\d{1,3})")]
    private static partial Regex TimeRangeRegex();

    public IReadOnlyList<TranscriptEntry> ParseFile(string path)
    {
        try
        {
            var text = File.ReadAllText(path, DetectEncoding(path));
            return Path.GetExtension(path).Equals(".vtt", StringComparison.OrdinalIgnoreCase)
                ? ParseVtt(text)
                : ParseSrt(text);
        }
        catch (Exception ex)
        {
            LoggingService.LogError($"Failed to parse subtitle file '{path}'.", ex);
            return Array.Empty<TranscriptEntry>();
        }
    }

    private static Encoding DetectEncoding(string path)
    {
        try
        {
            using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            reader.Peek();
            return reader.CurrentEncoding;
        }
        catch
        {
            return Encoding.UTF8;
        }
    }

    private static List<TranscriptEntry> ParseSrt(string content) => ParseCueBased(content);

    private static List<TranscriptEntry> ParseVtt(string content)
    {
        // WEBVTT files share the same "start --> end" cue structure as SRT once
        // the header and NOTE/STYLE blocks are stripped, so the same block
        // parser handles both formats.
        var withoutHeader = Regex.Replace(content, @"^WEBVTT.*?(\r?\n){2}", string.Empty,
            RegexOptions.Singleline);
        return ParseCueBased(withoutHeader);
    }

    private static List<TranscriptEntry> ParseCueBased(string content)
    {
        var entries = new List<TranscriptEntry>();
        var blocks = Regex.Split(content, @"\r?\n\r?\n");

        foreach (var block in blocks)
        {
            if (string.IsNullOrWhiteSpace(block))
            {
                continue;
            }

            if (TryParseBlock(block, out var entry))
            {
                entries.Add(entry!);
            }
        }

        return entries.OrderBy(e => e.Start).ToList();
    }

    private static bool TryParseBlock(string block, out TranscriptEntry? entry)
    {
        entry = null;

        var lines = block.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.Length > 0)
            .ToList();

        if (lines.Count == 0)
        {
            return false;
        }

        var timeLineIndex = lines.FindIndex(l => TimeRangeRegex().IsMatch(l));
        if (timeLineIndex < 0)
        {
            return false;
        }

        var match = TimeRangeRegex().Match(lines[timeLineIndex]);
        if (!TryParseTime(match, 1, out var start) || !TryParseTime(match, 5, out var end))
        {
            return false;
        }

        var textLines = lines.Skip(timeLineIndex + 1).ToList();
        if (textLines.Count == 0)
        {
            return false;
        }

        var text = string.Join(Environment.NewLine, textLines.Select(StripTags));

        entry = new TranscriptEntry { Start = start, End = end, Text = text };
        return true;
    }

    private static bool TryParseTime(Match match, int startGroup, out TimeSpan time)
    {
        time = TimeSpan.Zero;

        try
        {
            var hours = int.Parse(match.Groups[startGroup].Value, CultureInfo.InvariantCulture);
            var minutes = int.Parse(match.Groups[startGroup + 1].Value, CultureInfo.InvariantCulture);
            var seconds = int.Parse(match.Groups[startGroup + 2].Value, CultureInfo.InvariantCulture);
            var fraction = match.Groups[startGroup + 3].Value.PadRight(3, '0')[..3];
            var milliseconds = int.Parse(fraction, CultureInfo.InvariantCulture);

            time = new TimeSpan(0, hours, minutes, seconds, milliseconds);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string StripTags(string line) => Regex.Replace(line, "<[^>]+>", string.Empty).Trim();

    /// <summary>
    /// Finds subtitle files sitting next to a media file whose name starts with
    /// the media's base name (e.g. Meeting.srt, Meeting.en.srt for Meeting.mp4).
    /// Deliberately conservative - it never scans unrelated files in the folder.
    /// </summary>
    public IReadOnlyList<string> FindSiblingSubtitles(string mediaFilePath)
    {
        try
        {
            var directory = Path.GetDirectoryName(mediaFilePath);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return Array.Empty<string>();
            }

            var baseName = Path.GetFileNameWithoutExtension(mediaFilePath);

            return Directory.EnumerateFiles(directory)
                .Where(f => FileTypeHelper.IsSubtitleFile(f))
                .Where(f => Path.GetFileNameWithoutExtension(f)
                    .StartsWith(baseName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            LoggingService.LogError($"Failed to search for sibling subtitles of '{mediaFilePath}'.", ex);
            return Array.Empty<string>();
        }
    }
}
