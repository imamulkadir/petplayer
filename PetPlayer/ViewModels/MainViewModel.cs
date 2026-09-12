using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetPlayer.Helpers;
using PetPlayer.Models;
using PetPlayer.Services;

namespace PetPlayer.ViewModels;

/// <summary>
/// Central state and command surface for the main player window. Owns no UI
/// types directly - window-level concerns (fullscreen chrome, cursor hiding,
/// dialogs) are requested via events that MainWindow's code-behind fulfils.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private static readonly SubtitleTrack OffTrack = new() { Id = -1, Name = "Off" };

    private readonly MediaPlayerService _mediaService;
    private readonly SubtitleService _subtitleService;
    private readonly TranscriptService _transcriptService;
    private readonly SettingsService _settingsService;
    private readonly Dispatcher _dispatcher;

    private readonly DispatcherTimer _overlayTimer;
    private bool _suppressTrackSelectionEvents;
    private int _lastNonOffSubtitleTrackId = -1;

    public AppSettings Settings { get; private set; }

    /// <summary>The LibVLC MediaPlayer instance, exposed only so the view can bind it to the VideoView control.</summary>
    public LibVLCSharp.Shared.MediaPlayer Player => _mediaService.Player;

    public event EventHandler? RequestOpenSettings;
    public event EventHandler? RequestExit;
    public event EventHandler<string>? RequestOpenFileLocation;
    public event EventHandler<string>? ErrorOccurred;

    public MainViewModel(MediaPlayerService mediaService, SubtitleService subtitleService,
        TranscriptService transcriptService, SettingsService settingsService)
    {
        _mediaService = mediaService;
        _subtitleService = subtitleService;
        _transcriptService = transcriptService;
        _settingsService = settingsService;
        _dispatcher = Dispatcher.CurrentDispatcher;

        Settings = _settingsService.Load();
        ApplyLoadedSettings();

        _overlayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PlaybackConstants.OverlayFadeStartMs) };
        _overlayTimer.Tick += (_, _) =>
        {
            _overlayTimer.Stop();
            IsOverlayVisible = false;
        };

        _mediaService.TimeChanged += OnTimeChanged;
        _mediaService.LengthChanged += OnLengthChanged;
        _mediaService.Playing += OnPlaying;
        _mediaService.Paused += OnPaused;
        _mediaService.Stopped += OnStoppedOrEnded;
        _mediaService.EndReached += OnStoppedOrEnded;
        _mediaService.PlaybackError += OnPlaybackError;
    }

    private void ApplyLoadedSettings()
    {
        SeekIntervalSeconds = Settings.SeekIntervalSeconds;
        Volume = Settings.Volume;
        IsMuted = Settings.IsMuted;
        PlaybackSpeed = Settings.PlaybackSpeed;
        IsAlwaysOnTop = Settings.AlwaysOnTop;
        IsTranscriptPanelVisible = Settings.TranscriptPanelVisible;
    }

    // ----- Observable state -----

    [ObservableProperty] private string _windowTitle = "Pet Player";
    [ObservableProperty] private string? _currentFilePath;
    [ObservableProperty] private bool _hasMedia;
    [ObservableProperty] private bool _isPlaying;

    [ObservableProperty] private double _seekBarMaximumMs = 1;
    [ObservableProperty] private double _seekBarValueMs;
    [ObservableProperty] private bool _isSeekBarDragging;
    [ObservableProperty] private string _positionText = "0:00";
    [ObservableProperty] private string _durationText = "0:00";

    [ObservableProperty] private int _volume;
    [ObservableProperty] private bool _isMuted;
    [ObservableProperty] private double _seekIntervalSeconds;
    [ObservableProperty] private double _playbackSpeed;
    [ObservableProperty] private string _playbackSpeedText = "1x";

    partial void OnPlaybackSpeedChanged(double value)
    {
        PlaybackSpeedText = $"{value:0.##}x";
        _mediaService.SetRate((float)value);
    }

    [ObservableProperty] private bool _isFullscreen;
    [ObservableProperty] private bool _isAlwaysOnTop;
    [ObservableProperty] private bool _isControlsVisible = true;
    [ObservableProperty] private bool _isTranscriptPanelVisible;

    [ObservableProperty] private string? _overlayText;
    [ObservableProperty] private bool _isOverlayVisible;

    [ObservableProperty] private long _subtitleDelayMs;
    [ObservableProperty] private ObservableCollection<SubtitleTrack> _subtitleTracks = new();
    [ObservableProperty] private SubtitleTrack? _selectedSubtitleTrack;
    [ObservableProperty] private ObservableCollection<AudioTrack> _audioTracks = new();
    [ObservableProperty] private AudioTrack? _selectedAudioTrack;

    [ObservableProperty] private ObservableCollection<TranscriptEntry> _transcriptEntries = new();
    [ObservableProperty] private TranscriptEntry? _activeTranscriptEntry;

    partial void OnSelectedSubtitleTrackChanged(SubtitleTrack? value)
    {
        if (_suppressTrackSelectionEvents || value is null)
        {
            return;
        }

        if (value.Id >= 0)
        {
            _lastNonOffSubtitleTrackId = value.Id;
        }

        if (value.Id < 0)
        {
            _mediaService.DisableSubtitles();
        }
        else if (value.IsExternal)
        {
            // External tracks are attached (and thereby selected) at load time;
            // re-selecting just re-applies the same slave id.
            _mediaService.SetSubtitleTrack(value.Id);
        }
        else
        {
            _mediaService.SetSubtitleTrack(value.Id);
        }
    }

    partial void OnVolumeChanged(int value) => _mediaService.SetVolume(value);

    partial void OnIsMutedChanged(bool value) => _mediaService.SetMute(value);

    partial void OnSelectedAudioTrackChanged(AudioTrack? value)
    {
        if (_suppressTrackSelectionEvents || value is null)
        {
            return;
        }

        _mediaService.SetAudioTrack(value.Id);
    }

    // ----- Loading media -----

    /// <summary>
    /// Entry point for every way a path can reach the player: Open dialog,
    /// drag-and-drop, command-line argument, or a hand-off from another
    /// launched instance. Subtitle files are attached instead of loaded when
    /// a video is already playing, per spec.
    /// </summary>
    public void OpenPath(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                RaiseError($"Could not find the file:\n{path}");
                return;
            }

            if (FileTypeHelper.IsSubtitleFile(path) && HasMedia)
            {
                AttachSubtitle(path, showOverlay: true);
                return;
            }

            LoadMedia(path);
        }
        catch (Exception ex)
        {
            LoggingService.LogError($"Failed to open path '{path}'.", ex);
            RaiseError("Pet Player could not open that file.");
        }
    }

    private void LoadMedia(string path)
    {
        try
        {
            _mediaService.SetSubtitleDelay(0);
            SubtitleDelayMs = 0;
            _transcriptService.Clear();
            TranscriptEntries = new ObservableCollection<TranscriptEntry>();
            ActiveTranscriptEntry = null;

            _mediaService.Load(path, autoplay: Settings.Autoplay);

            CurrentFilePath = path;
            HasMedia = true;
            WindowTitle = $"{Path.GetFileName(path)} - Pet Player";
            Settings.LastOpenedDirectory = Path.GetDirectoryName(path) ?? Settings.LastOpenedDirectory;

            _mediaService.SetVolume(Volume);
            _mediaService.SetMute(IsMuted);
            // PlaybackSpeed's own property-changed hook applies the rate for the
            // life of a Media instance, but a freshly assigned Media resets the
            // engine's rate, so it needs to be explicitly reapplied here too.
            _mediaService.SetRate((float)PlaybackSpeed);

            TryAutoLoadMatchingSubtitle(path);
            TryResumePosition(path);
        }
        catch (Exception ex)
        {
            LoggingService.LogError($"Failed to load media '{path}'.", ex);
            RaiseError("Pet Player could not play that file. It may be corrupted, unsupported, or unavailable.");
        }
    }

    private void TryAutoLoadMatchingSubtitle(string mediaPath)
    {
        if (!Settings.AutoLoadMatchingSubtitle)
        {
            return;
        }

        var candidates = _subtitleService.FindSiblingSubtitles(mediaPath);
        if (candidates.Count == 0)
        {
            return;
        }

        AttachSubtitle(candidates[0], showOverlay: false);
    }

    public void AttachSubtitle(string subtitlePath, bool showOverlay)
    {
        try
        {
            var entries = _subtitleService.ParseFile(subtitlePath);
            _transcriptService.SetEntries(entries);
            TranscriptEntries = new ObservableCollection<TranscriptEntry>(entries);

            var attached = _mediaService.AttachSubtitleFile(subtitlePath);
            if (!attached && entries.Count == 0)
            {
                RaiseError("That subtitle file could not be read. It may be malformed.");
                return;
            }

            if (attached)
            {
                RefreshTracks();
            }

            if (showOverlay)
            {
                ShowOverlay($"Subtitle loaded: {Path.GetFileName(subtitlePath)}");
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogError($"Failed to attach subtitle '{subtitlePath}'.", ex);
            RaiseError("That subtitle file could not be loaded.");
        }
    }

    private void TryResumePosition(string mediaPath)
    {
        if (!Settings.ResumePlaybackEnabled)
        {
            return;
        }

        if (Settings.ResumePositions.TryGetValue(mediaPath, out var savedMs) && savedMs > 0)
        {
            _mediaService.SeekTo(TimeSpan.FromMilliseconds(savedMs));
        }
    }

    private void RefreshTracks()
    {
        _suppressTrackSelectionEvents = true;
        try
        {
            var subtitleTracks = new ObservableCollection<SubtitleTrack> { OffTrack };
            foreach (var track in _mediaService.GetSubtitleTracks())
            {
                subtitleTracks.Add(track);
            }

            SubtitleTracks = subtitleTracks;
            var currentSpu = _mediaService.CurrentSubtitleTrackId;
            SelectedSubtitleTrack = subtitleTracks.FirstOrDefault(t => t.Id == currentSpu) ?? OffTrack;

            var audioTracks = new ObservableCollection<AudioTrack>(_mediaService.GetAudioTracks());
            AudioTracks = audioTracks;
            // LibVLC's own AudioTrackDescription list puts its built-in "Disable"
            // pseudo-track first, ahead of the real tracks - blindly taking
            // FirstOrDefault() here always selected "Disable" as checked in the
            // menu even while a real track was actively playing. Match the
            // track LibVLC itself reports as current instead (mirrors how
            // CurrentSubtitleTrackId/Player.Spu is used for subtitles above).
            var currentAudioTrackId = _mediaService.CurrentAudioTrackId;
            SelectedAudioTrack = audioTracks.FirstOrDefault(t => t.Id == currentAudioTrackId) ?? audioTracks.FirstOrDefault();
        }
        finally
        {
            _suppressTrackSelectionEvents = false;
        }
    }

    // ----- Playback event handlers (fire on background threads) -----

    private void OnTimeChanged(object? sender, long timeMs)
    {
        _dispatcher.BeginInvoke(() =>
        {
            PositionText = TimeFormatter.FormatMilliseconds(timeMs);

            if (!IsSeekBarDragging)
            {
                SeekBarValueMs = timeMs;
            }

            var active = _transcriptService.FindActiveEntry(TimeSpan.FromMilliseconds(timeMs));
            if (!Equals(active, ActiveTranscriptEntry))
            {
                ActiveTranscriptEntry = active;
            }
        });
    }

    private void OnLengthChanged(object? sender, long lengthMs)
    {
        _dispatcher.BeginInvoke(() =>
        {
            SeekBarMaximumMs = Math.Max(1, lengthMs);
            DurationText = TimeFormatter.FormatMilliseconds(lengthMs);
        });
    }

    private void OnPlaying(object? sender, EventArgs e)
    {
        _dispatcher.BeginInvoke(() =>
        {
            IsPlaying = true;
            IsControlsVisible = true;
            RefreshTracks();
        });
    }

    private void OnPaused(object? sender, EventArgs e)
    {
        _dispatcher.BeginInvoke(() =>
        {
            IsPlaying = false;
            IsControlsVisible = true;
        });
    }

    private void OnStoppedOrEnded(object? sender, EventArgs e)
    {
        _dispatcher.BeginInvoke(() =>
        {
            IsPlaying = false;
            IsControlsVisible = true;
            SaveResumePosition();
        });
    }

    private void OnPlaybackError(object? sender, string message)
    {
        _dispatcher.BeginInvoke(() =>
        {
            LoggingService.LogError($"Playback error: {message}");
            RaiseError("Pet Player ran into a problem playing this file.");
        });
    }

    private void RaiseError(string message) => ErrorOccurred?.Invoke(this, message);

    // ----- Seeking -----

    /// <summary>
    /// Seeks and immediately reflects the new position in the UI. While
    /// paused, LibVLC doesn't reliably raise TimeChanged after a seek (the
    /// frame updates but the event that drives PositionText/SeekBarValueMs
    /// does not), so the displayed time would otherwise only catch up once
    /// playback resumes. Updating it here directly from the requested
    /// position keeps the on-screen timer in sync regardless of playback state.
    /// </summary>
    public void SeekToMilliseconds(double milliseconds)
    {
        _mediaService.SeekTo(TimeSpan.FromMilliseconds(milliseconds));

        var clampedMs = Math.Clamp(milliseconds, 0, SeekBarMaximumMs);
        SeekBarValueMs = clampedMs;
        PositionText = TimeFormatter.FormatMilliseconds((long)clampedMs);
    }

    [RelayCommand]
    private void SeekForward() => SeekRelativeAndNotify(TimeSpan.FromSeconds(SeekIntervalSeconds), "sec");

    [RelayCommand]
    private void SeekBackward() => SeekRelativeAndNotify(TimeSpan.FromSeconds(-SeekIntervalSeconds), "sec");

    [RelayCommand]
    private void SeekForwardPrecise() => SeekRelativeAndNotify(TimeSpan.FromSeconds(PlaybackConstants.PreciseSeekSmallSeconds), "sec");

    [RelayCommand]
    private void SeekBackwardPrecise() => SeekRelativeAndNotify(TimeSpan.FromSeconds(-PlaybackConstants.PreciseSeekSmallSeconds), "sec");

    [RelayCommand]
    private void SeekForwardMedium() => SeekRelativeAndNotify(TimeSpan.FromSeconds(PlaybackConstants.PreciseSeekMediumSeconds), "sec");

    [RelayCommand]
    private void SeekBackwardMedium() => SeekRelativeAndNotify(TimeSpan.FromSeconds(-PlaybackConstants.PreciseSeekMediumSeconds), "sec");

    [RelayCommand]
    private void JumpForward() => SeekRelativeAndNotify(TimeSpan.FromSeconds(PlaybackConstants.JumpSeekSeconds), "sec");

    [RelayCommand]
    private void JumpBackward() => SeekRelativeAndNotify(TimeSpan.FromSeconds(-PlaybackConstants.JumpSeekSeconds), "sec");

    private void SeekRelativeAndNotify(TimeSpan delta, string unitSuffix)
    {
        if (!HasMedia)
        {
            return;
        }

        _mediaService.SeekRelative(delta);
        var sign = delta.TotalSeconds >= 0 ? "+" : "-";
        var magnitude = Math.Abs(delta.TotalSeconds);
        var formatted = magnitude % 1 == 0 ? magnitude.ToString("0") : magnitude.ToString("0.##");
        ShowOverlay($"{sign}{formatted} {unitSuffix}");
    }

    // ----- Playback / volume / speed -----

    [RelayCommand]
    private void PlayPause()
    {
        if (!HasMedia)
        {
            return;
        }

        _mediaService.TogglePlayPause();
    }

    [RelayCommand]
    private void VolumeUp() => ChangeVolume(Settings.VolumeStepPercent);

    [RelayCommand]
    private void VolumeDown() => ChangeVolume(-Settings.VolumeStepPercent);

    private void ChangeVolume(int deltaPercent)
    {
        IsMuted = false;
        Volume = Math.Clamp(Volume + deltaPercent, PlaybackConstants.MinVolumePercent, PlaybackConstants.MaxVolumePercent);
        ShowOverlay($"Volume {Volume}%");
    }

    [RelayCommand]
    private void ToggleMute()
    {
        IsMuted = !IsMuted;
        ShowOverlay(IsMuted ? "Muted" : $"Volume {Volume}%");
    }

    [RelayCommand]
    private void IncreaseSpeed() => ChangeSpeed(1);

    [RelayCommand]
    private void DecreaseSpeed() => ChangeSpeed(-1);

    private void ChangeSpeed(int direction)
    {
        var speeds = PlaybackConstants.PlaybackSpeeds;
        var currentIndex = Array.IndexOf(speeds, PlaybackSpeed);
        if (currentIndex < 0)
        {
            currentIndex = Array.IndexOf(speeds, PlaybackConstants.DefaultPlaybackSpeed);
        }

        var newIndex = Math.Clamp(currentIndex + direction, 0, speeds.Length - 1);
        PlaybackSpeed = speeds[newIndex];
        ShowOverlay(PlaybackSpeedText);
    }

    [RelayCommand]
    private void ResetSpeed()
    {
        PlaybackSpeed = PlaybackConstants.DefaultPlaybackSpeed;
        ShowOverlay(PlaybackSpeedText);
    }

    // ----- Subtitle sync -----

    [RelayCommand]
    private void ShiftSubtitleEarlierSmall() => ShiftSubtitleDelay(-PlaybackConstants.SubtitleSmallShiftMs);

    [RelayCommand]
    private void ShiftSubtitleLaterSmall() => ShiftSubtitleDelay(PlaybackConstants.SubtitleSmallShiftMs);

    [RelayCommand]
    private void ShiftSubtitleEarlierLarge() => ShiftSubtitleDelay(-PlaybackConstants.SubtitleLargeShiftMs);

    [RelayCommand]
    private void ShiftSubtitleLaterLarge() => ShiftSubtitleDelay(PlaybackConstants.SubtitleLargeShiftMs);

    private void ShiftSubtitleDelay(int deltaMs)
    {
        if (!HasMedia)
        {
            return;
        }

        SubtitleDelayMs += deltaMs;
        _mediaService.SetSubtitleDelay(SubtitleDelayMs);
        ShowOverlay($"Subtitle delay: {TimeFormatter.FormatDelta((int)SubtitleDelayMs)}");
    }

    [RelayCommand]
    private void ResetSubtitleDelay()
    {
        if (!HasMedia)
        {
            return;
        }

        SubtitleDelayMs = 0;
        _mediaService.SetSubtitleDelay(0);
        ShowOverlay("Subtitle delay reset");
    }

    [RelayCommand]
    private void ToggleShowSubtitles()
    {
        if (SelectedSubtitleTrack is { Id: >= 0 })
        {
            SelectedSubtitleTrack = OffTrack;
            ShowOverlay("Subtitles off");
        }
        else
        {
            // _lastNonOffSubtitleTrackId defaults to -1 until the user explicitly
            // picks a real track (the video's own initial default track is set
            // through a path that deliberately skips updating it - see
            // _suppressTrackSelectionEvents in RefreshTracks). Without the
            // "&& t.Id >= 0" guard here, that -1 default would match OffTrack
            // itself (whose Id is also -1), silently re-selecting Off while the
            // overlay still claimed "Subtitles on" - so the checkmark could
            // never actually turn on for a video whose subtitle track was never
            // manually changed.
            var target = SubtitleTracks.FirstOrDefault(t => t.Id == _lastNonOffSubtitleTrackId && t.Id >= 0)
                ?? SubtitleTracks.FirstOrDefault(t => t.Id >= 0);
            if (target is not null)
            {
                SelectedSubtitleTrack = target;
                ShowOverlay("Subtitles on");
            }
        }
    }

    [RelayCommand]
    private void ShiftSubtitlePositionUp() => ShiftSubtitlePosition(PlaybackConstants.SubtitlePositionStep);

    [RelayCommand]
    private void ShiftSubtitlePositionDown() => ShiftSubtitlePosition(-PlaybackConstants.SubtitlePositionStep);

    [RelayCommand]
    private void ResetSubtitlePosition()
    {
        Settings.SubtitleVerticalPosition = 0.92;
        ShowOverlay("Subtitle position reset (restart Pet Player to apply)");
    }

    private void ShiftSubtitlePosition(double delta)
    {
        Settings.SubtitleVerticalPosition = Math.Clamp(Settings.SubtitleVerticalPosition + delta, 0, 1);
        ShowOverlay($"Subtitle position: {Settings.SubtitleVerticalPosition:P0} (restart Pet Player to apply)");
    }

    // ----- Window / view state -----

    [RelayCommand]
    private void ToggleFullscreen() => IsFullscreen = !IsFullscreen;

    public void ExitFullscreen() => IsFullscreen = false;

    [RelayCommand]
    private void ToggleAlwaysOnTop()
    {
        IsAlwaysOnTop = !IsAlwaysOnTop;
        ShowOverlay(IsAlwaysOnTop ? "Always on top: on" : "Always on top: off");
    }

    [RelayCommand]
    private void ToggleTranscriptPanel() => IsTranscriptPanelVisible = !IsTranscriptPanelVisible;

    [RelayCommand]
    private void SeekToTranscriptEntry(TranscriptEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        _mediaService.SeekTo(entry.Start);
    }

    [RelayCommand]
    private void OpenFileLocation()
    {
        if (!string.IsNullOrEmpty(CurrentFilePath))
        {
            RequestOpenFileLocation?.Invoke(this, CurrentFilePath);
        }
    }

    [RelayCommand]
    private void OpenSettings() => RequestOpenSettings?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Exit() => RequestExit?.Invoke(this, EventArgs.Empty);

    public void NotifyUserActivity()
    {
        if (!IsControlsVisible)
        {
            IsControlsVisible = true;
        }
    }

    private void ShowOverlay(string text)
    {
        OverlayText = text;
        IsOverlayVisible = true;
        _overlayTimer.Stop();
        _overlayTimer.Start();
    }

    // ----- Lifecycle -----

    public void SaveResumePosition()
    {
        if (!Settings.ResumePlaybackEnabled || string.IsNullOrEmpty(CurrentFilePath))
        {
            return;
        }

        var remaining = _mediaService.Duration - _mediaService.Position;
        if (remaining.TotalSeconds <= PlaybackConstants.ResumeTailSkipSeconds)
        {
            Settings.ResumePositions.Remove(CurrentFilePath);
        }
        else if (_mediaService.Position.TotalSeconds > 1)
        {
            Settings.ResumePositions[CurrentFilePath] = (long)_mediaService.Position.TotalMilliseconds;

            while (Settings.ResumePositions.Count > PlaybackConstants.MaxRecentResumeEntries)
            {
                var oldestKey = Settings.ResumePositions.Keys.First();
                Settings.ResumePositions.Remove(oldestKey);
            }
        }
    }

    public void PersistSettings()
    {
        SaveResumePosition();

        Settings.SeekIntervalSeconds = SeekIntervalSeconds;
        Settings.Volume = Volume;
        Settings.IsMuted = IsMuted;
        Settings.PlaybackSpeed = PlaybackSpeed;
        Settings.AlwaysOnTop = IsAlwaysOnTop;
        Settings.TranscriptPanelVisible = IsTranscriptPanelVisible;

        _settingsService.Save(Settings);
    }

    public void Dispose()
    {
        _mediaService.TimeChanged -= OnTimeChanged;
        _mediaService.LengthChanged -= OnLengthChanged;
        _mediaService.Playing -= OnPlaying;
        _mediaService.Paused -= OnPaused;
        _mediaService.Stopped -= OnStoppedOrEnded;
        _mediaService.EndReached -= OnStoppedOrEnded;
        _mediaService.PlaybackError -= OnPlaybackError;
        _overlayTimer.Stop();
    }
}
