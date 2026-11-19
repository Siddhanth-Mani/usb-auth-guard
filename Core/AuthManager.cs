using Microsoft.Extensions.Logging;

namespace USBAuthGuard.Core;

/// <summary>
/// Validates USB devices against the trusted device whitelist.
/// Supports add/remove operations that persist immediately via <see cref="ConfigManager"/>.
/// </summary>
public sealed class AuthManager
{
    private readonly ConfigManager _config;
    private readonly ILogger<AuthManager> _logger;

    // In-memory set for fast O(1) lookups
    private readonly HashSet<string> _trustedKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public AuthManager(ConfigManager config, ILogger<AuthManager> logger)
    {
        _config = config;
        _logger = logger;
        Reload();
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>Returns true if the device's UniqueKey is in the trust list.</summary>
    public bool IsTrusted(USBDevice device)
    {
        lock (_lock)
        {
            bool trusted = _trustedKeys.Contains(device.UniqueKey);
            device.IsTrusted = trusted;
            return trusted;
        }
    }

    /// <summary>Adds a device to the trusted list and persists the change.</summary>
    public void AddTrustedDevice(USBDevice device)
    {
        lock (_lock)
        {
            if (_trustedKeys.Add(device.UniqueKey))
            {
                _logger.LogInformation("Trusted device added: {Key} ({Model})",
                    device.UniqueKey, device.Model);
                PersistTrustedList();
            }
        }
    }

    /// <summary>Removes a device from the trusted list by its UniqueKey.</summary>
    public bool RemoveTrustedDevice(string uniqueKey)
    {
        lock (_lock)
        {
            if (_trustedKeys.Remove(uniqueKey))
            {
                _logger.LogInformation("Trusted device removed: {Key}", uniqueKey);
                PersistTrustedList();
                return true;
            }
            return false;
        }
    }

    /// <summary>Returns a snapshot of all trusted device keys.</summary>
    public IReadOnlyList<string> GetTrustedKeys()
    {
        lock (_lock)
            return [.. _trustedKeys];
    }

    /// <summary>Reloads trusted list from config file.</summary>
    public void Reload()
    {
        lock (_lock)
        {
            _trustedKeys.Clear();
            foreach (var key in _config.DeviceConfig.TrustedDevices)
                _trustedKeys.Add(key);
            _logger.LogInformation("AuthManager loaded {Count} trusted device(s).", _trustedKeys.Count);
        }
    }

    // ── Private ────────────────────────────────────────────────────────────

    private void PersistTrustedList()
    {
        _config.DeviceConfig.TrustedDevices = [.. _trustedKeys];
        _config.SaveDeviceConfig();
    }
}
