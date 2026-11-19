using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace USBAuthGuard.Logging;

/// <summary>
/// Writes security events to the Windows Event Log under source "USBAuthGuard".
///
/// NOTE: Creating a new Event Log source requires one-time elevated execution
/// or a system installer step. If running without elevation, this class will
/// gracefully fall back to file logging only.
/// </summary>
public sealed class EventLogManager
{
    private const string Source  = "USBAuthGuard";
    private const string LogName = "Application";

    private readonly bool _available;
    private readonly ILogger<EventLogManager> _logger;

    public EventLogManager(ILogger<EventLogManager> logger)
    {
        _logger = logger;
        _available = TryEnsureSource();
    }

    // ── Public API ─────────────────────────────────────────────────────────

    public void WriteEvent(string message, EventLogEntryType type = EventLogEntryType.Information, int eventId = 1000)
    {
        if (!_available) return;

        try
        {
            EventLog.WriteEntry(Source, message, type, eventId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EventLog write failed — {Message}", message);
        }
    }

    // ── Private ────────────────────────────────────────────────────────────

    private bool TryEnsureSource()
    {
        try
        {
            if (!EventLog.SourceExists(Source))
            {
                EventLog.CreateEventSource(Source, LogName);
                _logger.LogInformation("Windows Event Log source '{Source}' created.", Source);
            }
            return true;
        }
        catch (System.Security.SecurityException)
        {
            _logger.LogWarning(
                "Cannot create Event Log source — insufficient permissions. " +
                "Run once as Administrator to register the source. " +
                "Windows Event Log integration disabled for this session.");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Event Log source check failed. Integration disabled.");
            return false;
        }
    }
}
