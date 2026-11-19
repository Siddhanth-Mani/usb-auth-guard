using Microsoft.Extensions.Logging;

namespace USBAuthGuard.Core;

/// <summary>
/// Evaluates security rules from <see cref="PoliciesConfig"/> and executes
/// appropriate responses to USB events.
/// </summary>
public sealed class PolicyEngine
{
    private readonly ConfigManager _config;
    private readonly SystemController _system;
    private readonly StateEngine _state;
    private readonly ILogger<PolicyEngine> _logger;

    // Cancellation token for the lock delay timer
    private CancellationTokenSource? _lockDelayCts;
    private readonly object _delayLock = new();

    public PolicyEngine(
        ConfigManager config,
        SystemController system,
        StateEngine state,
        ILogger<PolicyEngine> logger)
    {
        _config = config;
        _system = system;
        _state  = state;
        _logger = logger;
    }

    // ── Policy Handlers ────────────────────────────────────────────────────

    /// <summary>
    /// Called when a trusted USB is inserted.
    /// Cancels any pending lock-delay and transitions to Unlocked.
    /// </summary>
    public void OnTrustedDeviceInserted(USBDevice device)
    {
        _logger.LogInformation("Policy: trusted device inserted — {Device}", device.DisplayName);
        CancelPendingLock();
        _state.TryTransition(SecurityState.Unlocked);
    }

    /// <summary>
    /// Called when a trusted USB is removed.
    /// Schedules a workstation lock after the configured delay.
    /// </summary>
    public void OnTrustedDeviceRemoved(USBDevice device)
    {
        _logger.LogInformation("Policy: trusted device removed — {Device}", device.DisplayName);

        if (!_config.PoliciesConfig.LockOnTrustedRemoval)
        {
            _logger.LogDebug("Policy: lock-on-removal is disabled, skipping.");
            return;
        }

        int delaySeconds = _config.DeviceConfig.LockDelaySeconds;
        ScheduleLock(delaySeconds);
    }

    /// <summary>
    /// Called when an untrusted USB is inserted.
    /// Optionally locks the workstation and always logs a warning.
    /// </summary>
    public void OnUntrustedDeviceInserted(USBDevice device)
    {
        _logger.LogWarning(
            "Policy: UNTRUSTED device inserted — {Model} | Key={Key}",
            device.Model, device.UniqueKey);

        if (_config.DeviceConfig.LockOnUntrustedInsert &&
            _config.PoliciesConfig.WarnOnUntrustedInsert)
        {
            _logger.LogWarning("Policy: locking workstation due to untrusted device.");
            _state.TryTransition(SecurityState.Locked);
            _system.LockWorkstation();
        }
    }

    /// <summary>Immediately locks without any delay (manual lock action).</summary>
    public void ForceLockNow()
    {
        CancelPendingLock();
        _state.TryTransition(SecurityState.Locked);
        _system.LockWorkstation();
    }

    // ── Private ────────────────────────────────────────────────────────────

    private void ScheduleLock(int delaySeconds)
    {
        lock (_delayLock)
        {
            CancelPendingLockUnsafe();
            _lockDelayCts = new CancellationTokenSource();
            var token = _lockDelayCts.Token;

            Task.Delay(TimeSpan.FromSeconds(Math.Max(0, delaySeconds)), token)
                .ContinueWith(t =>
                {
                    if (t.IsCanceled) return;
                    _logger.LogInformation("Policy: lock delay elapsed, locking workstation.");
                    _state.TryTransition(SecurityState.Locked);
                    _system.LockWorkstation();
                }, TaskScheduler.Default);

            _logger.LogInformation("Policy: workstation lock scheduled in {Delay}s.", delaySeconds);
        }
    }

    private void CancelPendingLock()
    {
        lock (_delayLock)
            CancelPendingLockUnsafe();
    }

    private void CancelPendingLockUnsafe()
    {
        if (_lockDelayCts is not null)
        {
            _lockDelayCts.Cancel();
            _lockDelayCts.Dispose();
            _lockDelayCts = null;
            _logger.LogDebug("Policy: pending lock timer cancelled.");
        }
    }
}
