namespace PetPlayer.Models;

/// <summary>An embedded or externally-attached subtitle track exposed to the UI.</summary>
public sealed class SubtitleTrack
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool IsExternal { get; init; }

    public override string ToString() => Name;
}
