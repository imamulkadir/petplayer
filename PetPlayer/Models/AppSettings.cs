namespace PetPlayer.Models;

/// <summary>
/// Everything Pet Player persists to settings.json under %LOCALAPPDATA%\PetPlayer.
/// Kept as a plain data object so SettingsService can (de)serialize it directly.
/// </summary>
public sealed class AppSettings
{
    public double SeekIntervalSeconds { get; set; } = Helpers.PlaybackConstants.DefaultSeekIntervalSeconds;

    public int Volume { get; set; } = Helpers.PlaybackConstants.DefaultVolumePercent;

    public int VolumeStepPercent { get; set; } = Helpers.PlaybackConstants.DefaultVolumeStepPercent;

    public bool IsMuted { get; set; }

    public double PlaybackSpeed { get; set; } = Helpers.PlaybackConstants.DefaultPlaybackSpeed;

    public bool AlwaysOnTop { get; set; }

    public double WindowWidth { get; set; } = 1200;

    public double WindowHeight { get; set; } = 800;

    public double WindowLeft { get; set; } = -1;

    public double WindowTop { get; set; } = -1;

    public bool WindowMaximized { get; set; }

    public string LastOpenedDirectory { get; set; } = string.Empty;

    public int SubtitleFontSize { get; set; } = 22;

    public double SubtitleVerticalPosition { get; set; } = 0.92;

    public double SubtitleBackgroundOpacity { get; set; } = 0.35;

    public double SubtitleTextOpacity { get; set; } = 1.0;

    public bool SubtitleOutlineEnabled { get; set; } = true;

    public bool Autoplay { get; set; } = true;

    public bool AutoLoadMatchingSubtitle { get; set; } = true;

    public int ControlsHideDelayMs { get; set; } = Helpers.PlaybackConstants.ControlsHideDelayMs;

    public bool TranscriptPanelVisible { get; set; }

    public bool ResumePlaybackEnabled { get; set; }

    /// <summary>Recently played file paths mapped to their last known position, in milliseconds.</summary>
    public Dictionary<string, long> ResumePositions { get; set; } = new();

    public bool RegisteredForOpenWith { get; set; }
}
