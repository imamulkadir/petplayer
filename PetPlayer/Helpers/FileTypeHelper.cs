using System.IO;

namespace PetPlayer.Helpers;

/// <summary>
/// Extension-based classification used only for routing (drag/drop, "Open With"
/// registration, sibling-subtitle discovery). Actual decodability is always left
/// to LibVLC - this never blocks playback of an unlisted extension.
/// </summary>
public static class FileTypeHelper
{
    public static readonly string[] VideoExtensions =
    {
        ".mp4", ".mkv", ".avi", ".mov", ".webm", ".wmv", ".mpeg", ".mpg",
        ".m4v", ".flv", ".ts", ".m2ts", ".mts", ".3gp", ".ogv", ".vob"
    };

    public static readonly string[] AudioExtensions =
    {
        ".mp3", ".aac", ".flac", ".wav", ".ogg", ".m4a", ".wma"
    };

    public static readonly string[] SubtitleExtensions =
    {
        ".srt", ".vtt", ".ass", ".ssa", ".sub"
    };

    public static IEnumerable<string> MediaExtensions => VideoExtensions.Concat(AudioExtensions);

    public static bool IsSubtitleFile(string path) =>
        SubtitleExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static bool IsLikelyMediaFile(string path) =>
        MediaExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}
