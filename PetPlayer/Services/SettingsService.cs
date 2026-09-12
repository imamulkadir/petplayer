using System.IO;
using System.Text.Json;
using PetPlayer.Helpers;
using PetPlayer.Models;

namespace PetPlayer.Services;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as JSON under %LOCALAPPDATA%\PetPlayer.
/// A missing or corrupted settings file is never fatal - defaults are used instead
/// and the bad file is preserved alongside a ".corrupt" backup for diagnostics.
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppSettings Load()
    {
        try
        {
            PathHelper.EnsureDataDirectoriesExist();

            if (!File.Exists(PathHelper.SettingsFilePath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(PathHelper.SettingsFilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json);
            return settings ?? new AppSettings();
        }
        catch (Exception ex)
        {
            LoggingService.LogError("Failed to load settings.json; falling back to defaults.", ex);
            TryBackupCorruptSettings();
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            PathHelper.EnsureDataDirectoriesExist();
            var json = JsonSerializer.Serialize(settings, JsonOptions);

            // Write to a temp file first so a crash mid-write never corrupts the
            // existing settings file.
            var tempPath = PathHelper.SettingsFilePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Copy(tempPath, PathHelper.SettingsFilePath, overwrite: true);
            File.Delete(tempPath);
        }
        catch (Exception ex)
        {
            LoggingService.LogError("Failed to save settings.json.", ex);
        }
    }

    private static void TryBackupCorruptSettings()
    {
        try
        {
            if (File.Exists(PathHelper.SettingsFilePath))
            {
                File.Copy(PathHelper.SettingsFilePath, PathHelper.SettingsFilePath + ".corrupt", overwrite: true);
            }
        }
        catch
        {
            // Best-effort only.
        }
    }
}
