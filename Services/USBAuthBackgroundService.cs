using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using USBAuthGuard.Core;
using USBAuthGuard.Logging;

namespace USBAuthGuard.Services;

/// <summary>
/// Background service that wires together all core modules:
/// USBMonitor → AuthManager → PolicyEngine → VaultManager → Logger
///
/// Runs for the lifetime of the application host.
/// </summary>
public sealed class USBAuthBackgroundService : BackgroundService
{
    private readonly USBMonitor _monitor;
    private readonly AuthManager _auth;
    private readonly PolicyEngine _policy;
    private readonly StateEngine _state;
    private readonly VaultManager _vault;
    private readonly ConfigManager _config;
    private readonly AppLogger _appLogger;
    private readonly EventLogManager _eventLog;
    private readonly ILogger<USBAuthBackgroundService> _logger;

    // Track which trusted key is currently "active" for vault unlock
    private string? _activeDeviceKey;
    private readonly object _activeKeyLock = new();

    public USBAuthBackgroundService(
        USBMonitor monitor,
        AuthManager auth,
        PolicyEngine policy,
        StateEngine state,
        VaultManager vault,
        ConfigManager config,
        AppLogger appLogger,
        EventLogManager eventLog,
        ILogger<USBAuthBackgroundService> logger)
    {
        _monitor   = monitor;
        _auth      = auth;
        _policy    = policy;
        _state     = state;
        _vault     = vault;
        _config    = config;
        _appLogger = appLogger;
        _eventLog  = eventLog;
        _logger    = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("USBAuthGuard background service starting.");
        _appLogger.Log("INFO", "Service", "USBAuthGuard started.");

        // Subscribe to USB events
        _monitor.DeviceInserted += OnDeviceInserted;
        _monitor.DeviceRemoved  += OnDeviceRemoved;

        // Enumerate already-connected devices on startup
        var connected = _monitor.GetConnectedDevices();
        bool anyTrusted = false;
        foreach (var device in connected)
        {
            if (_auth.IsTrusted(device))
            {
                anyTrusted = true;
                SetActiveKey(device.UniqueKey);
                _appLogger.Log("INFO", "Startup", $"Trusted USB already connected: {device.DisplayName}");
            }
        }

        _state.InitializeState(anyTrusted ? SecurityState.Unlocked : SecurityState.Locked);

        // Start WMI watchers
        _monitor.Start();

        // Keep the service alive until cancelled
        await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _monitor.DeviceInserted -= OnDeviceInserted;
        _monitor.DeviceRemoved  -= OnDeviceRemoved;
        _monitor.Stop();
        _appLogger.Log("INFO", "Service", "USBAuthGuard stopped.");
        await base.StopAsync(cancellationToken);
    }

    // ── Event Handlers ─────────────────────────────────────────────────────

    private void OnDeviceInserted(object? sender, USBDevice device)
    {
        if (_auth.IsTrusted(device))
        {
            _appLogger.Log("INFO", "Auth", $"Trusted USB inserted: {device.DisplayName}");
            _eventLog.WriteEvent("Trusted USB inserted", System.Diagnostics.EventLogEntryType.Information);

            SetActiveKey(device.UniqueKey);
            _policy.OnTrustedDeviceInserted(device);

            // Unlock vault if configured
            var vaultFolder = _config.PoliciesConfig.VaultFolder;
            if (!string.IsNullOrWhiteSpace(vaultFolder))
            {
                _appLogger.Log("INFO", "Vault", $"Unlocking vault: {vaultFolder}");
                _vault.UnlockVault(vaultFolder, device.UniqueKey);
            }
        }
        else
        {
            _appLogger.Log("WARN", "Auth",
                $"UNTRUSTED USB inserted: {device.DisplayName} [{device.UniqueKey}]");
            _eventLog.WriteEvent($"Untrusted USB inserted: {device.UniqueKey}",
                System.Diagnostics.EventLogEntryType.Warning);

            _policy.OnUntrustedDeviceInserted(device);
        }
    }

    private void OnDeviceRemoved(object? sender, USBDevice device)
    {
        if (_auth.IsTrusted(device))
        {
            _appLogger.Log("INFO", "Auth", $"Trusted USB removed: {device.DisplayName}");
            _eventLog.WriteEvent("Trusted USB removed", System.Diagnostics.EventLogEntryType.Warning);

            // Lock vault before locking workstation
            var vaultFolder = _config.PoliciesConfig.VaultFolder;
            if (!string.IsNullOrWhiteSpace(vaultFolder))
            {
                _appLogger.Log("INFO", "Vault", $"Locking vault: {vaultFolder}");
                _vault.LockVault(vaultFolder, device.UniqueKey);
            }

            ClearActiveKey();
            _policy.OnTrustedDeviceRemoved(device);
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void SetActiveKey(string key)
    {
        lock (_activeKeyLock) _activeDeviceKey = key;
    }

    private void ClearActiveKey()
    {
        lock (_activeKeyLock) _activeDeviceKey = null;
    }

    public string? GetActiveDeviceKey()
    {
        lock (_activeKeyLock) return _activeDeviceKey;
    }
}
