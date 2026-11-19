using Microsoft.Win32;
using Microsoft.Extensions.Logging;

namespace USBAuthGuard.Services;

/// <summary>
/// Manages auto-start registration for USBAuthGuard via the Windows Registry.
/// Uses HKCU so no elevation is required.
/// </summary>
public sealed class StartupManager
{
    private const string RegistryKey  = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName      = "USBAuthGuard";

    private readonly ILogger<StartupManager> _logger;

    public StartupManager(ILogger<StartupManager> logger)
    {
        _logger = logger;
    }

    /// <summary>Returns true if USBAuthGuard is registered to start with Windows.</summary>
    public bool IsStartupEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, writable: false);
                return key?.GetValue(AppName) is not null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read startup registry key.");
                return false;
            }
        }
    }

    /// <summary>
    /// Registers USBAuthGuard to launch at Windows login.
    /// Uses the current executable path.
    /// </summary>
    public bool EnableStartup()
    {
        try
        {
            var exePath = Environment.ProcessPath
                ?? System.Reflection.Assembly.GetExecutingAssembly().Location;

            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, writable: true);
            if (key is null)
            {
                _logger.LogError("Cannot open Run registry key for writing.");
                return false;
            }

            key.SetValue(AppName, $"\"{exePath}\"");
            _logger.LogInformation("Startup enabled: {ExePath}", exePath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enable startup.");
            return false;
        }
    }

    /// <summary>Removes USBAuthGuard from Windows startup registry.</summary>
    public bool DisableStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, writable: true);
            if (key?.GetValue(AppName) is null)
            {
                _logger.LogDebug("Startup entry not present, nothing to remove.");
                return true;
            }

            key.DeleteValue(AppName, throwOnMissingValue: false);
            _logger.LogInformation("Startup disabled.");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to disable startup.");
            return false;
        }
    }
}
