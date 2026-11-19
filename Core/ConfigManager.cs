using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace USBAuthGuard.Core;

// ── Config models ──────────────────────────────────────────────────────────

public sealed class DeviceConfig
{
    [JsonPropertyName("trustedDevices")]
    public List<string> TrustedDevices { get; set; } = [];

    [JsonPropertyName("lockDelaySeconds")]
    public int LockDelaySeconds { get; set; } = 5;

    [JsonPropertyName("lockOnUntrustedInsert")]
    public bool LockOnUntrustedInsert { get; set; } = false;
}

public sealed class PoliciesConfig
{
    [JsonPropertyName("lockOnTrustedRemoval")]
    public bool LockOnTrustedRemoval { get; set; } = true;

    [JsonPropertyName("warnOnUntrustedInsert")]
    public bool WarnOnUntrustedInsert { get; set; } = true;

    [JsonPropertyName("vaultFolder")]
    public string VaultFolder { get; set; } = string.Empty;

    [JsonPropertyName("enableEventLog")]
    public bool EnableEventLog { get; set; } = true;

    [JsonPropertyName("startWithWindows")]
    public bool StartWithWindows { get; set; } = false;
}

// ── ConfigManager ──────────────────────────────────────────────────────────

/// <summary>
/// Loads and persists JSON configuration files.
/// Thread-safe writes via a dedicated lock.
/// Config files are stored next to the executable in Config/.
/// </summary>
public sealed class ConfigManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _deviceConfigPath;
    private readonly string _policiesConfigPath;
    private readonly object _fileLock = new();
    private readonly ILogger<ConfigManager> _logger;

    public DeviceConfig  DeviceConfig  { get; private set; } = new();
    public PoliciesConfig PoliciesConfig { get; private set; } = new();

    public ConfigManager(ILogger<ConfigManager> logger)
    {
        _logger = logger;

        // Config folder lives beside the executable (or project root when debugging)
        var baseDir = AppContext.BaseDirectory;
        var configDir = Path.Combine(baseDir, "Config");
        Directory.CreateDirectory(configDir);

        _deviceConfigPath  = Path.Combine(configDir, "trusted_devices.json");
        _policiesConfigPath = Path.Combine(configDir, "policies.json");

        Load();
    }

    // ── Public API ─────────────────────────────────────────────────────────

    public void Load()
    {
        DeviceConfig   = LoadFile<DeviceConfig>(_deviceConfigPath)   ?? new DeviceConfig();
        PoliciesConfig = LoadFile<PoliciesConfig>(_policiesConfigPath) ?? new PoliciesConfig();
        _logger.LogInformation("Config loaded. {Count} trusted device(s).", DeviceConfig.TrustedDevices.Count);
    }

    public void SaveDeviceConfig()  => SaveFile(_deviceConfigPath,  DeviceConfig);
    public void SavePoliciesConfig() => SaveFile(_policiesConfigPath, PoliciesConfig);

    // ── Private ────────────────────────────────────────────────────────────

    private T? LoadFile<T>(string path) where T : class
    {
        lock (_fileLock)
        {
            if (!File.Exists(path))
            {
                _logger.LogWarning("Config file not found: {Path}. Using defaults.", path);
                return null;
            }

            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<T>(json, JsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse config file {Path}.", path);
                return null;
            }
        }
    }

    private void SaveFile<T>(string path, T obj)
    {
        lock (_fileLock)
        {
            try
            {
                var json = JsonSerializer.Serialize(obj, JsonOptions);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save config file {Path}.", path);
            }
        }
    }
}
