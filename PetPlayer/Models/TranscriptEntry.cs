namespace PetPlayer.Models;

/// <summary>A single timestamped line parsed from an SRT/VTT file, used by the transcript panel.</summary>
public sealed class TranscriptEntry
{
    public required TimeSpan Start { get; init; }
    public required TimeSpan End { get; init; }
    public required string Text { get; init; }

    public bool Contains(TimeSpan position) => position >= Start && position < End;
}
