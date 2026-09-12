using PetPlayer.Models;

namespace PetPlayer.Services;

/// <summary>
/// Holds the currently loaded transcript entries and resolves which one is
/// active for a given playback position. Kept separate from SubtitleService
/// (parsing) so the transcript panel's notion of "current line" doesn't leak
/// into subtitle file handling.
/// </summary>
public sealed class TranscriptService
{
    private IReadOnlyList<TranscriptEntry> _entries = Array.Empty<TranscriptEntry>();

    public IReadOnlyList<TranscriptEntry> Entries => _entries;

    public bool HasEntries => _entries.Count > 0;

    public void SetEntries(IReadOnlyList<TranscriptEntry> entries) => _entries = entries;

    public void Clear() => _entries = Array.Empty<TranscriptEntry>();

    /// <summary>Returns the entry active at the given position, or null if between/outside cues.</summary>
    public TranscriptEntry? FindActiveEntry(TimeSpan position) =>
        _entries.FirstOrDefault(e => e.Contains(position));
}
