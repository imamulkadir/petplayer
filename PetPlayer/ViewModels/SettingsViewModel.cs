using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetPlayer.Helpers;
using PetPlayer.Models;
using PetPlayer.Services;

namespace PetPlayer.ViewModels;

/// <summary>Backs the Settings window. Edits a working copy of AppSettings and only writes it back on Save.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly WindowsIntegrationService _integrationService;

    public double[] SeekIntervalChoices => PlaybackConstants.SeekIntervalChoicesSeconds;

    [ObservableProperty] private double _seekIntervalSeconds;
    [ObservableProperty] private int _volumeStepPercent;
    [ObservableProperty] private bool _autoplay;
    [ObservableProperty] private bool _autoLoadMatchingSubtitle;
    [ObservableProperty] private bool _resumePlaybackEnabled;

    [ObservableProperty] private int _subtitleFontSize;
    [ObservableProperty] private double _subtitleVerticalPosition;
    [ObservableProperty] private double _subtitleBackgroundOpacity;
    [ObservableProperty] private double _subtitleTextOpacity;
    [ObservableProperty] private bool _subtitleOutlineEnabled;

    [ObservableProperty] private bool _showTranscriptByDefault;

    [ObservableProperty] private bool _isRegisteredForOpenWith;
    [ObservableProperty] private string? _integrationStatusMessage;

    public string AppVersionText =>
        $"Version {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0"}";

    public string AppDescriptionText => "Pet Player - a lightweight, keyboard-first video player.";

    /// <summary>Raised after Save so the main window can re-apply live-affecting values (seek interval, etc).</summary>
    public event EventHandler? SettingsSaved;

    public SettingsViewModel(AppSettings settings, SettingsService settingsService, WindowsIntegrationService integrationService)
    {
        _settings = settings;
        _settingsService = settingsService;
        _integrationService = integrationService;

        _seekIntervalSeconds = settings.SeekIntervalSeconds;
        _volumeStepPercent = settings.VolumeStepPercent;
        _autoplay = settings.Autoplay;
        _autoLoadMatchingSubtitle = settings.AutoLoadMatchingSubtitle;
        _resumePlaybackEnabled = settings.ResumePlaybackEnabled;

        _subtitleFontSize = settings.SubtitleFontSize;
        _subtitleVerticalPosition = settings.SubtitleVerticalPosition;
        _subtitleBackgroundOpacity = settings.SubtitleBackgroundOpacity;
        _subtitleTextOpacity = settings.SubtitleTextOpacity;
        _subtitleOutlineEnabled = settings.SubtitleOutlineEnabled;

        _showTranscriptByDefault = settings.TranscriptPanelVisible;

        _isRegisteredForOpenWith = _integrationService.IsRegistered();
    }

    [RelayCommand]
    private void RegisterOpenWith()
    {
        var result = _integrationService.Register();
        IsRegisteredForOpenWith = _integrationService.IsRegistered();
        IntegrationStatusMessage = result.Outcome == WindowsIntegrationOutcome.Success
            ? "Pet Player is now available in Windows \"Open with\"."
            : result.Message ?? "Pet Player could not update the Windows \"Open with\" registration.";
    }

    [RelayCommand]
    private void UnregisterOpenWith()
    {
        var result = _integrationService.Unregister();
        IsRegisteredForOpenWith = _integrationService.IsRegistered();
        IntegrationStatusMessage = result.Outcome == WindowsIntegrationOutcome.Success
            ? "Pet Player has been removed from Windows \"Open with\"."
            : result.Message ?? "Pet Player could not update the Windows \"Open with\" registration.";
    }

    [RelayCommand]
    private void Save()
    {
        _settings.SeekIntervalSeconds = SeekIntervalSeconds > 0 ? SeekIntervalSeconds : PlaybackConstants.DefaultSeekIntervalSeconds;
        _settings.VolumeStepPercent = Math.Clamp(VolumeStepPercent, 1, 50);
        _settings.Autoplay = Autoplay;
        _settings.AutoLoadMatchingSubtitle = AutoLoadMatchingSubtitle;
        _settings.ResumePlaybackEnabled = ResumePlaybackEnabled;

        _settings.SubtitleFontSize = Math.Clamp(SubtitleFontSize, 10, 72);
        _settings.SubtitleVerticalPosition = Math.Clamp(SubtitleVerticalPosition, 0, 1);
        _settings.SubtitleBackgroundOpacity = Math.Clamp(SubtitleBackgroundOpacity, 0, 1);
        _settings.SubtitleTextOpacity = Math.Clamp(SubtitleTextOpacity, 0, 1);
        _settings.SubtitleOutlineEnabled = SubtitleOutlineEnabled;

        _settings.TranscriptPanelVisible = ShowTranscriptByDefault;

        _settings.RegisteredForOpenWith = IsRegisteredForOpenWith;

        _settingsService.Save(_settings);
        SettingsSaved?.Invoke(this, EventArgs.Empty);
    }
}
