using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;
using PetPlayer.Helpers;
using PetPlayer.Models;

namespace PetPlayer.Services;

/// <summary>
/// Owns the single LibVLC/MediaPlayer pair for the application's lifetime and is
/// the only place that talks to LibVLCSharp directly. All playback operations,
/// track enumeration and disposal go through here so the rest of the app never
/// depends on LibVLC types directly.
///
/// LibVLC event callbacks fire on background threads; this service does not
/// touch WPF objects and simply re-raises events as-is. Subscribers (view models)
/// are responsible for dispatching to the UI thread.
/// </summary>
public sealed class MediaPlayerService : IDisposable
{
    private LibVLC? _libVlc;
    private Media? _currentMedia;
    private bool _disposed;

    public MediaPlayer Player { get; private set; } = null!;

    public event EventHandler<long>? TimeChanged;
    public event EventHandler<long>? LengthChanged;
    public event EventHandler? Playing;
    public event EventHandler? Paused;
    public event EventHandler? Stopped;
    public event EventHandler? EndReached;
    public event EventHandler<string>? PlaybackError;

    public void Initialize(AppSettings settings)
    {
        var libVlcDirectory = ResolveLibVlcDirectory();

        try
        {
            if (libVlcDirectory is not null)
            {
                Core.Initialize(libVlcDirectory);
            }
            else
            {
                Core.Initialize();
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to initialize the LibVLC video engine.", ex);
        }

        var options = new List<string> { "--no-video-title-show", "--quiet-synchro" };
        options.AddRange(BuildSubtitleAppearanceOptions(settings));

        _libVlc = new LibVLC(enableDebugLogs: false, options.ToArray());
        Player = new MediaPlayer(_libVlc);

        Player.TimeChanged += (_, e) => TimeChanged?.Invoke(this, e.Time);
        Player.LengthChanged += (_, e) => LengthChanged?.Invoke(this, e.Length);
        Player.Playing += (_, _) => Playing?.Invoke(this, EventArgs.Empty);
        Player.Paused += (_, _) => Paused?.Invoke(this, EventArgs.Empty);
        Player.Stopped += (_, _) => Stopped?.Invoke(this, EventArgs.Empty);
        Player.EndReached += (_, _) => EndReached?.Invoke(this, EventArgs.Empty);
        Player.EncounteredError += (_, _) =>
            PlaybackError?.Invoke(this, "LibVLC encountered a playback error.");
    }

    /// <summary>
    /// Resolves the bundled libvlc runtime directory relative to the application's
    /// own folder so Pet Player never depends on a system-wide VLC install.
    /// Falls back to LibVLCSharp's built-in auto-detection if the expected
    /// bundled layout isn't found (e.g. during local `dotnet run`).
    /// </summary>
    private static string? ResolveLibVlcDirectory()
    {
        var architectureFolder = RuntimeInformation.ProcessArchitecture == Architecture.X86 ? "win-x86" : "win-x64";
        var candidate = Path.Combine(PathHelper.AppDirectory, "libvlc", architectureFolder);
        return Directory.Exists(candidate) ? candidate : null;
    }

    /// <summary>
    /// Translates the user's subtitle appearance settings into LibVLC's
    /// freetype text-renderer options, keeping Pet Player from having to
    /// build any subtitle rendering of its own (spec explicitly discourages
    /// that). These are core/module config for the freetype text renderer,
    /// not input-item config - confirmed empirically (loaded a real video at
    /// two wildly different settings and screenshotted it: identical
    /// rendering both times) that passing them via Media.AddOption's ":opt="
    /// per-item syntax is silently ignored. Passed as global "--opt=" options
    /// to the LibVLC instance instead, which does take effect - the trade-off
    /// is they're read once at startup, so a Settings change needs an app
    /// restart to apply (see the note in SettingsWindow.xaml).
    /// </summary>
    private static string[] BuildSubtitleAppearanceOptions(AppSettings settings)
    {
        // The extra *0.75 shrinks subtitles below whatever SubtitleFontSize a user
        // already has saved, since the previous default read as too large.
        var textScale = Math.Clamp((int)(settings.SubtitleFontSize / 22.0 * 100 * 0.75), 25, 500);
        var textOpacity = Math.Clamp((int)(settings.SubtitleTextOpacity * 255), 0, 255);
        // Always fully transparent - just the text, no background box behind it
        // (the Settings window's background-opacity slider was removed to match).
        const int backgroundOpacity = 0;
        var outlineThickness = settings.SubtitleOutlineEnabled ? 4 : 0;
        // Scaled down further still (was *200, then *175, then *120) so subtitles
        // sit closer to the bottom edge, regardless of what SubtitleVerticalPosition
        // a user already has saved (sub-margin is a bottom gap in px - smaller is lower).
        var bottomMargin = Math.Clamp((int)(settings.SubtitleVerticalPosition * 60), 0, 300);

        return new[]
        {
            $"--sub-text-scale={textScale}",
            $"--freetype-opacity={textOpacity}",
            $"--freetype-background-opacity={backgroundOpacity}",
            $"--freetype-outline-thickness={outlineThickness}",
            $"--sub-margin={bottomMargin}",
        };
    }

    public bool IsPlaying => Player.IsPlaying;

    public TimeSpan Duration => TimeSpan.FromMilliseconds(Math.Max(0, Player.Length));

    public TimeSpan Position => TimeSpan.FromMilliseconds(Math.Max(0, Player.Time));

    public void Load(string filePath, bool autoplay)
    {
        ReleaseCurrentMedia();

        _currentMedia = new Media(_libVlc!, new Uri(filePath));
        Player.Media = _currentMedia;

        if (autoplay)
        {
            Player.Play();
        }
    }

    public void TogglePlayPause()
    {
        if (Player.Media is null)
        {
            return;
        }

        if (Player.IsPlaying)
        {
            Player.Pause();
        }
        else
        {
            Player.Play();
        }
    }

    public void Play() => Player.Play();

    public void Pause() => Player.Pause();

    public void Stop() => Player.Stop();

    public void SeekTo(TimeSpan position)
    {
        if (Player.Media is null || !Player.IsSeekable)
        {
            return;
        }

        var clampedMs = ClampToDuration((long)position.TotalMilliseconds);
        Player.Time = clampedMs;
    }

    public void SeekRelative(TimeSpan delta)
    {
        if (Player.Media is null || !Player.IsSeekable)
        {
            return;
        }

        var targetMs = ClampToDuration(Player.Time + (long)delta.TotalMilliseconds);
        Player.Time = targetMs;
    }

    private long ClampToDuration(long ms)
    {
        var duration = Math.Max(0, Player.Length);
        if (duration <= 0)
        {
            return Math.Max(0, ms);
        }

        return Math.Clamp(ms, 0, duration);
    }

    /// <summary>
    /// LibVLC's own Volume property is a LINEAR amplitude multiplier (50% = half the
    /// raw sample amplitude, roughly -6dB), but perceived loudness is closer to
    /// logarithmic - a straight passthrough makes the lower half of the UI slider
    /// sound much quieter than its position suggests (50% reads as "barely audible"
    /// rather than a normal mid-level volume). Mapping the UI's linear 0-100% through
    /// a square-root curve before handing it to LibVLC compensates for that (perceived
    /// loudness roughly follows the square root of the amplitude ratio): 0% stays
    /// silent, 100% stays at LibVLC's normal unboosted 100% (sqrt(1) = 1, so this can
    /// never clip/distort), and everything in between gets noticeably more audible/
    /// natural instead of disappearing into near-silence.
    /// </summary>
    public void SetVolume(int volumePercent)
    {
        var uiPercent = Math.Clamp(volumePercent, PlaybackConstants.MinVolumePercent, PlaybackConstants.MaxVolumePercent);
        Player.Volume = (int)Math.Round(100.0 * Math.Sqrt(uiPercent / 100.0));
    }

    public void SetMute(bool muted) => Player.Mute = muted;

    public void SetRate(float rate) => Player.SetRate(rate);

    public IReadOnlyList<Models.SubtitleTrack> GetSubtitleTracks()
    {
        if (Player.Media is null)
        {
            return Array.Empty<Models.SubtitleTrack>();
        }

        return Player.SpuDescription
            .Select(d => new Models.SubtitleTrack { Id = d.Id, Name = d.Name, IsExternal = false })
            .ToList();
    }

    public int CurrentSubtitleTrackId => Player.Spu;

    public void SetSubtitleTrack(int id) => Player.SetSpu(id);

    public void DisableSubtitles() => Player.SetSpu(-1);

    public bool AttachSubtitleFile(string subtitlePath)
    {
        try
        {
            var uri = new Uri(subtitlePath).AbsoluteUri;
            return Player.AddSlave(MediaSlaveType.Subtitle, uri, true);
        }
        catch (Exception ex)
        {
            LoggingService.LogError($"Failed to attach subtitle file '{subtitlePath}'.", ex);
            return false;
        }
    }

    public void SetSubtitleDelay(long milliseconds) => Player.SetSpuDelay(milliseconds * 1000);

    public long GetSubtitleDelayMilliseconds() => Player.SpuDelay / 1000;

    public IReadOnlyList<Models.AudioTrack> GetAudioTracks()
    {
        if (Player.Media is null)
        {
            return Array.Empty<Models.AudioTrack>();
        }

        return Player.AudioTrackDescription
            .Select(d => new Models.AudioTrack { Id = d.Id, Name = d.Name })
            .ToList();
    }

    public int CurrentAudioTrackId => Player.AudioTrack;

    public void SetAudioTrack(int id) => Player.SetAudioTrack(id);

    private void ReleaseCurrentMedia()
    {
        if (_currentMedia is null)
        {
            return;
        }

        Player.Stop();
        _currentMedia.Dispose();
        _currentMedia = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            ReleaseCurrentMedia();
            Player?.Dispose();
            _libVlc?.Dispose();
        }
        catch (Exception ex)
        {
            LoggingService.LogError("Error while disposing MediaPlayerService.", ex);
        }
    }
}
