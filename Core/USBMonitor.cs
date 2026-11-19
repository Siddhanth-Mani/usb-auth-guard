using System.Management;
using Microsoft.Extensions.Logging;

namespace USBAuthGuard.Core;

/// <summary>
/// Monitors USB storage device insertion and removal using WMI event watchers.
/// Raises <see cref="DeviceInserted"/> and <see cref="DeviceRemoved"/> events
/// on the thread-pool; consumers must marshal to UI thread as needed.
/// </summary>
public sealed class USBMonitor : IDisposable
{
    // ── WMI queries ────────────────────────────────────────────────────────
    private const string WmiScope     = @"\\.\root\CIMV2";
    private const string InsertQuery  =
        "SELECT * FROM __InstanceCreationEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_DiskDrive'";
    private const string RemoveQuery  =
        "SELECT * FROM __InstanceDeletionEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_DiskDrive'";
    private const string EnumQuery    =
        "SELECT DeviceID, PNPDeviceID, Model FROM Win32_DiskDrive WHERE InterfaceType = 'USB'";

    // ── Events ─────────────────────────────────────────────────────────────
    public event EventHandler<USBDevice>? DeviceInserted;
    public event EventHandler<USBDevice>? DeviceRemoved;

    // ── State ──────────────────────────────────────────────────────────────
    private ManagementEventWatcher? _insertWatcher;
    private ManagementEventWatcher? _removeWatcher;
    private readonly ILogger<USBMonitor> _logger;
    private bool _disposed;

    public USBMonitor(ILogger<USBMonitor> logger)
    {
        _logger = logger;
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>Starts WMI watchers. Call once during service start.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _logger.LogInformation("USBMonitor starting WMI watchers…");

        var scope = new ManagementScope(WmiScope);

        _insertWatcher = CreateWatcher(scope, InsertQuery, OnDeviceInserted);
        _removeWatcher = CreateWatcher(scope, RemoveQuery, OnDeviceRemoved);

        _insertWatcher.Start();
        _removeWatcher.Start();

        _logger.LogInformation("USBMonitor ready.");
    }

    /// <summary>Stops WMI watchers.</summary>
    public void Stop()
    {
        _insertWatcher?.Stop();
        _removeWatcher?.Stop();
        _logger.LogInformation("USBMonitor stopped.");
    }

    /// <summary>Returns all currently connected USB storage devices.</summary>
    public IReadOnlyList<USBDevice> GetConnectedDevices()
    {
        var devices = new List<USBDevice>();
        try
        {
            using var searcher = new ManagementObjectSearcher(WmiScope, EnumQuery);
            foreach (ManagementObject obj in searcher.Get())
            {
                var device = BuildDevice(obj);
                if (device is not null)
                    devices.Add(device);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enumerate USB devices.");
        }
        return devices;
    }

    // ── Private Helpers ────────────────────────────────────────────────────

    private static ManagementEventWatcher CreateWatcher(
        ManagementScope scope,
        string query,
        EventArrivedEventHandler handler)
    {
        var watcher = new ManagementEventWatcher(scope, new EventQuery(query));
        watcher.EventArrived += handler;
        return watcher;
    }

    private void OnDeviceInserted(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var instance = (ManagementBaseObject)e.NewEvent["TargetInstance"];
            var device = BuildDevice(instance);
            if (device is not null)
            {
                _logger.LogDebug("WMI: device inserted — {Device}", device);
                DeviceInserted?.Invoke(this, device);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing device insertion event.");
        }
    }

    private void OnDeviceRemoved(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var instance = (ManagementBaseObject)e.NewEvent["TargetInstance"];
            var device = BuildDevice(instance);
            if (device is not null)
            {
                _logger.LogDebug("WMI: device removed — {Device}", device);
                DeviceRemoved?.Invoke(this, device);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing device removal event.");
        }
    }

    private static USBDevice? BuildDevice(ManagementBaseObject obj)
    {
        try
        {
            var deviceId  = obj["DeviceID"]?.ToString()  ?? string.Empty;
            var pnpId     = obj["PNPDeviceID"]?.ToString() ?? string.Empty;
            var model     = obj["Model"]?.ToString()     ?? string.Empty;
            var serial    = USBDevice.ExtractSerial(pnpId);

            return new USBDevice
            {
                DeviceId     = deviceId,
                PnPDeviceId  = pnpId,
                Model        = model.Trim(),
                SerialNumber = serial
            };
        }
        catch
        {
            return null;
        }
    }

    // ── IDisposable ────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _insertWatcher?.Dispose();
        _removeWatcher?.Dispose();
    }
}
