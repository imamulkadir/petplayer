using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;
using PetPlayer.Helpers;

namespace PetPlayer.Services;

public enum WindowsIntegrationOutcome
{
    Success,
    Blocked,
    Failed
}

public sealed record WindowsIntegrationResult(WindowsIntegrationOutcome Outcome, string? Message = null);

/// <summary>
/// Registers/unregisters Pet Player as an available "Open With" application.
/// Writes exclusively under HKCU\Software\Classes\Applications\PetPlayer.exe -
/// this surfaces Pet Player in Explorer's "Open with" list for the listed
/// extensions without claiming default-app status for any of them, and never
/// touches HKEY_LOCAL_MACHINE. Every write is wrapped so a locked-down company
/// policy that blocks user registry writes degrades gracefully instead of
/// crashing the app.
/// </summary>
public sealed class WindowsIntegrationService
{
    private const string AppKeyPath = @"Software\Classes\Applications\PetPlayer.exe";
    private const string ShellCommandPath = AppKeyPath + @"\shell\open\command";
    private const string SupportedTypesPath = AppKeyPath + @"\SupportedTypes";

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    private const uint ShcneAssocChanged = 0x08000000;
    private const uint ShcnfIdList = 0x0000;

    public WindowsIntegrationResult Register()
    {
        try
        {
            var exePath = PathHelper.ExecutablePath;

            using var appKey = Registry.CurrentUser.CreateSubKey(AppKeyPath, writable: true)
                ?? throw new InvalidOperationException("Could not create Applications registry key.");
            appKey.SetValue("FriendlyAppName", "Pet Player");
            appKey.SetValue("DefaultIcon", $"{exePath},0");

            using var commandKey = Registry.CurrentUser.CreateSubKey(ShellCommandPath, writable: true)
                ?? throw new InvalidOperationException("Could not create shell\\open\\command registry key.");
            commandKey.SetValue(string.Empty, $"{PathHelper.QuoteForCommandLine(exePath)} \"%1\"");

            using var supportedTypesKey = Registry.CurrentUser.CreateSubKey(SupportedTypesPath, writable: true)
                ?? throw new InvalidOperationException("Could not create SupportedTypes registry key.");
            foreach (var extension in FileTypeHelper.VideoExtensions.Concat(FileTypeHelper.AudioExtensions))
            {
                supportedTypesKey.SetValue(extension, string.Empty);
            }

            NotifyShellOfChange();

            LoggingService.LogInfo("Registered Pet Player for Windows \"Open with\".");
            return new WindowsIntegrationResult(WindowsIntegrationOutcome.Success);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            LoggingService.LogWarning($"Open-With registration blocked by security policy: {ex.Message}");
            return new WindowsIntegrationResult(WindowsIntegrationOutcome.Blocked, BlockedMessage);
        }
        catch (Exception ex)
        {
            LoggingService.LogError("Failed to register Pet Player for Windows \"Open with\".", ex);
            return new WindowsIntegrationResult(WindowsIntegrationOutcome.Failed, ex.Message);
        }
    }

    public WindowsIntegrationResult Unregister()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(AppKeyPath, throwOnMissingSubKey: false);
            NotifyShellOfChange();

            LoggingService.LogInfo("Removed Pet Player from Windows \"Open with\".");
            return new WindowsIntegrationResult(WindowsIntegrationOutcome.Success);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            LoggingService.LogWarning($"Open-With removal blocked by security policy: {ex.Message}");
            return new WindowsIntegrationResult(WindowsIntegrationOutcome.Blocked, BlockedMessage);
        }
        catch (Exception ex)
        {
            LoggingService.LogError("Failed to remove Pet Player from Windows \"Open with\".", ex);
            return new WindowsIntegrationResult(WindowsIntegrationOutcome.Failed, ex.Message);
        }
    }

    public bool IsRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ShellCommandPath);
            return key is not null;
        }
        catch
        {
            return false;
        }
    }

    private static void NotifyShellOfChange()
    {
        try
        {
            SHChangeNotify(ShcneAssocChanged, ShcnfIdList, IntPtr.Zero, IntPtr.Zero);
        }
        catch
        {
            // Explorer will still pick up the change on next launch even if the
            // live notification fails.
        }
    }

    private const string BlockedMessage =
        "Pet Player could not be added to Windows \"Open with\" because this " +
        "device's security policy prevents user-level application registration." +
        "\n\nPet Player itself can still be used normally.";
}
