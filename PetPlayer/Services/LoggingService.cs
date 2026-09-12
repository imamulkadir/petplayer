using System.IO;
using PetPlayer.Helpers;

namespace PetPlayer.Services;

/// <summary>
/// Minimal file logger. Never throws - a logging failure must not take down
/// the player. One log file per day under %LOCALAPPDATA%\PetPlayer\logs.
/// </summary>
public static class LoggingService
{
    private static readonly object SyncRoot = new();

    public static void LogInfo(string message) => Write("INFO", message);

    public static void LogWarning(string message) => Write("WARN", message);

    public static void LogError(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message}{Environment.NewLine}{exception}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (SyncRoot)
            {
                PathHelper.EnsureDataDirectoriesExist();
                var fileName = $"petplayer-{DateTime.Now:yyyy-MM-dd}.log";
                var path = Path.Combine(PathHelper.LogsDirectory, fileName);
                var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(path, line);
            }
        }
        catch
        {
            // Logging must never crash the application.
        }
    }
}
