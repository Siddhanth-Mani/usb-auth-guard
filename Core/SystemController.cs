using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace USBAuthGuard.Core;

/// <summary>
/// Interfaces with the Windows user32.dll API to control workstation state.
/// </summary>
/// <remarks>
/// SECURITY NOTE: Windows intentionally prevents applications from
/// programmatically *unlocking* a workstation — only the user can
/// authenticate via the Windows login screen. This is by design and
/// cannot be circumvented without kernel-level access.
/// </remarks>
public sealed class SystemController
{
    private readonly ILogger<SystemController> _logger;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool LockWorkStation();

    public SystemController(ILogger<SystemController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Locks the current Windows session immediately.
    /// Equivalent to Win+L or Ctrl+Alt+Del → Lock.
    /// </summary>
    /// <returns>True if the lock call succeeded.</returns>
    public bool LockWorkstation()
    {
        _logger.LogInformation("SystemController: locking workstation…");
        bool success = LockWorkStation();
        if (!success)
        {
            int err = Marshal.GetLastWin32Error();
            _logger.LogError("LockWorkStation() failed with Win32 error {Error}.", err);
        }
        else
        {
            _logger.LogInformation("Workstation locked successfully.");
        }
        return success;
    }
}
