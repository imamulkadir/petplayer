using System.Windows;
using System.Windows.Threading;
using PetPlayer.Helpers;
using PetPlayer.Services;
using PetPlayer.ViewModels;
using PetPlayer.Views;

namespace PetPlayer;

public partial class App : Application
{
    private MediaPlayerService? _mediaPlayerService;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LoggingService.LogError("Unobserved task exception.", args.Exception);
            args.SetObserved();
        };

        PathHelper.EnsureDataDirectoriesExist();

        // Every launch runs as its own fully independent process/window - opening
        // another video (a second "Open with", a double-click on another file, etc.)
        // must never replace what's already playing in an existing window, so this
        // deliberately does not gate on (or hand off to) any already-running instance.
        var requestedFilePath = e.Args.Length > 0 ? e.Args[0] : null;

        var settingsService = new SettingsService();
        var startupSettings = settingsService.Load();

        _mediaPlayerService = new MediaPlayerService();

        try
        {
            _mediaPlayerService.Initialize(startupSettings);
        }
        catch (Exception ex)
        {
            LoggingService.LogError("Failed to initialize the LibVLC video engine.", ex);
            MessageBox.Show(
                "Pet Player could not initialize its video engine and cannot continue.\n\n" +
                "Please make sure Pet Player was extracted/copied completely (including the 'libvlc' folder) " +
                "and try again.",
                "Pet Player", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        // Wrapped explicitly (rather than left to the global DispatcherUnhandledException
        // handler, which only logs/shows a message and does not shut down) so a failure
        // here always results in a clean shutdown instead of an invisible running process.
        try
        {
            var subtitleService = new SubtitleService();
            var transcriptService = new TranscriptService();
            var integrationService = new WindowsIntegrationService();

            var mainViewModel = new MainViewModel(_mediaPlayerService, subtitleService, transcriptService, settingsService);

            mainViewModel.RequestExit += (_, _) => Shutdown();
            mainViewModel.RequestOpenFileLocation += (_, path) => OpenFileLocation(path);
            mainViewModel.RequestOpenSettings += (_, _) => ShowSettingsWindow(mainViewModel, settingsService, integrationService);
            mainViewModel.ErrorOccurred += (_, message) => ShowError(message);

            _mainWindow = new MainWindow(mainViewModel);
            MainWindow = _mainWindow;

            _mainWindow.Show();

            if (!string.IsNullOrWhiteSpace(requestedFilePath))
            {
                mainViewModel.OpenPath(requestedFilePath);
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogError("Failed to start up Pet Player's main window.", ex);
            MessageBox.Show(
                "Pet Player ran into a problem while starting and needs to close.\n\n" +
                "Technical details have been saved to the log file.",
                "Pet Player", MessageBoxButton.OK, MessageBoxImage.Error);

            _mediaPlayerService?.Dispose();
            Shutdown(1);
        }
    }

    private void ShowSettingsWindow(MainViewModel mainViewModel, SettingsService settingsService, WindowsIntegrationService integrationService)
    {
        var settingsViewModel = new SettingsViewModel(mainViewModel.Settings, settingsService, integrationService);
        settingsViewModel.SettingsSaved += (_, _) =>
        {
            mainViewModel.SeekIntervalSeconds = mainViewModel.Settings.SeekIntervalSeconds;
        };

        var settingsWindow = new SettingsWindow(settingsViewModel)
        {
            Owner = _mainWindow
        };
        settingsWindow.ShowDialog();
    }

    private static void OpenFileLocation(string filePath)
    {
        try
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\"");
        }
        catch (Exception ex)
        {
            LoggingService.LogError($"Failed to open file location for '{filePath}'.", ex);
        }
    }

    private void ShowError(string message)
    {
        MessageBox.Show(_mainWindow, message, "Pet Player", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LoggingService.LogError("Unhandled UI exception.", e.Exception);
        MessageBox.Show(
            "Pet Player ran into an unexpected problem and needs to recover.\n\n" +
            "Technical details have been saved to the log file.",
            "Pet Player", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        LoggingService.LogError("Unhandled fatal exception.", e.ExceptionObject as Exception);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // MainWindow.Closing already persisted settings and disposed the view model.
        _mediaPlayerService?.Dispose();

        base.OnExit(e);
    }
}
