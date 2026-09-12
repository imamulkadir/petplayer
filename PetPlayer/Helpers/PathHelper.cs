using System.IO;

namespace PetPlayer.Helpers;

/// <summary>
/// Centralizes all filesystem locations Pet Player touches so every service
/// agrees on where the app lives and where its per-user data goes.
/// </summary>
public static class PathHelper
{
    /// <summary>Directory containing PetPlayer.exe - used to resolve the bundled libvlc runtime.</summary>
    public static string AppDirectory { get; } =
        AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

    public static string ExecutablePath { get; } =
        Path.Combine(AppDirectory, "PetPlayer.exe");

    public static string LocalAppDataRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PetPlayer");

    public static string LogsDirectory { get; } = Path.Combine(LocalAppDataRoot, "logs");

    public static string CacheDirectory { get; } = Path.Combine(LocalAppDataRoot, "cache");

    public static string SettingsFilePath { get; } = Path.Combine(LocalAppDataRoot, "settings.json");

    public static void EnsureDataDirectoriesExist()
    {
        Directory.CreateDirectory(LocalAppDataRoot);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(CacheDirectory);
    }

    /// <summary>
    /// Quotes a path for safe use inside a Windows shell command line / registry
    /// command value. Wraps in double quotes and escapes any embedded quote.
    /// </summary>
    public static string QuoteForCommandLine(string path) =>
        $"\"{path.Replace("\"", "\\\"")}\"";
}
