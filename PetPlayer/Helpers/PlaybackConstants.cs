namespace PetPlayer.Helpers;

/// <summary>
/// Centralized playback tuning values so keyboard handling, view models and
/// settings all agree on the same numbers instead of duplicating literals.
/// </summary>
public static class PlaybackConstants
{
    public static readonly double[] SeekIntervalChoicesSeconds =
        { 0.25, 0.5, 1, 2, 3, 5, 10, 15, 30 };

    public const double DefaultSeekIntervalSeconds = 5.0;

    public const double PreciseSeekSmallSeconds = 0.25;
    public const double PreciseSeekMediumSeconds = 1.0;
    public const double JumpSeekSeconds = 10.0;

    public const int DefaultVolumeStepPercent = 5;
    public const int DefaultVolumePercent = 80;
    public const int MinVolumePercent = 0;
    public const int MaxVolumePercent = 100;

    public static readonly double[] PlaybackSpeeds =
        { 0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0, 2.5, 3.0, 4.0 };

    public const double DefaultPlaybackSpeed = 1.0;

    public const int SubtitleSmallShiftMs = 100;
    public const int SubtitleLargeShiftMs = 500;

    public const double SubtitlePositionStep = 0.05;

    public const int ControlsHideDelayMs = 3000;
    public const int CursorHideDelayMs = 2000;

    public const int OverlayFadeStartMs = 900;
    public const int OverlayFadeDurationMs = 300;

    public const int MaxRecentResumeEntries = 50;
    public const int ResumeTailSkipSeconds = 3;
}
