using System.IO;

namespace USBAuthGuard.Logging;

/// <summary>
/// Represents a single security log entry.
/// </summary>
public sealed record LogEntry(
    DateTime Timestamp,
    string Level,
    string Module,
    string Message);

/// <summary>
/// Thread-safe rotating file logger that writes to logs/security.log
/// beside the application executable.
///
/// Format: [2024-01-15 14:32:01] [INFO ] [Auth   ] Trusted USB inserted: Kingston DT100
/// </summary>
public sealed class AppLogger
{
    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB
    private const int  MaxArchiveFiles  = 5;

    private readonly string _logPath;
    private readonly string _logDir;
    private readonly ReaderWriterLockSlim _rwLock = new();

    // In-memory ring buffer for dashboard display (last 500 entries)
    private readonly Queue<LogEntry> _recentEntries = new();
    private const int MaxRecentEntries = 500;

    public event EventHandler<LogEntry>? EntryLogged;

    public AppLogger()
    {
        _logDir  = Path.Combine(AppContext.BaseDirectory, "logs");
        _logPath = Path.Combine(_logDir, "security.log");
        Directory.CreateDirectory(_logDir);
    }

    // ── Public API ─────────────────────────────────────────────────────────

    public void Log(string level, string module, string message)
    {
        var entry = new LogEntry(DateTime.Now, level.ToUpper(), module, message);
        WriteToFile(entry);
        AddToBuffer(entry);
        EntryLogged?.Invoke(this, entry);
    }

    public IReadOnlyList<LogEntry> GetRecentEntries()
    {
        _rwLock.EnterReadLock();
        try { return [.. _recentEntries]; }
        finally { _rwLock.ExitReadLock(); }
    }

    public string GetLogFilePath() => _logPath;

    // ── Private ────────────────────────────────────────────────────────────

    private void WriteToFile(LogEntry entry)
    {
        _rwLock.EnterWriteLock();
        try
        {
            RotateIfNeeded();
            var line = FormatEntry(entry);
            File.AppendAllText(_logPath, line + Environment.NewLine);
        }
        catch
        {
            // Swallow — logging should never crash the app
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }

    private void AddToBuffer(LogEntry entry)
    {
        _rwLock.EnterWriteLock();
        try
        {
            _recentEntries.Enqueue(entry);
            while (_recentEntries.Count > MaxRecentEntries)
                _recentEntries.Dequeue();
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(_logPath)) return;
        var info = new FileInfo(_logPath);
        if (info.Length < MaxFileSizeBytes) return;

        // Shift archives: .5 deleted, .4→.5, .3→.4 … .1→.2, current→.1
        for (int i = MaxArchiveFiles; i >= 1; i--)
        {
            var src  = i == 1 ? _logPath : $"{_logPath}.{i - 1}";
            var dest = $"{_logPath}.{i}";
            if (File.Exists(src))
            {
                if (File.Exists(dest)) File.Delete(dest);
                File.Move(src, dest);
            }
        }
    }

    private static string FormatEntry(LogEntry e) =>
        $"[{e.Timestamp:yyyy-MM-dd HH:mm:ss}] [{e.Level,-5}] [{e.Module,-10}] {e.Message}";
}
