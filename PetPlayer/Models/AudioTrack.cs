namespace PetPlayer.Models;

/// <summary>An audio track exposed to the UI's Audio Track submenu.</summary>
public sealed class AudioTrack
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;

    public override string ToString() => Name;
}
