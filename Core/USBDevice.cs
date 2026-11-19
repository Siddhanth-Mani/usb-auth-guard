using System.Text.Json.Serialization;

namespace USBAuthGuard.Core;

/// <summary>
/// Represents a USB storage device detected by WMI.
/// </summary>
public sealed class USBDevice
{
    /// <summary>WMI DeviceID (e.g. USBSTOR\DISK&VEN_KINGSTON...)</summary>
    public string DeviceId { get; init; } = string.Empty;

    /// <summary>PnP Device ID — most reliable unique identifier across reboots.</summary>
    public string PnPDeviceId { get; init; } = string.Empty;

    /// <summary>Human-readable model / product name.</summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>Serial number extracted from PnPDeviceId.</summary>
    public string SerialNumber { get; init; } = string.Empty;

    /// <summary>Whether this device is currently in the trusted list.</summary>
    [JsonIgnore]
    public bool IsTrusted { get; set; }

    /// <summary>
    /// Normalized unique key used for whitelist comparison.
    /// Prefers serial number; falls back to sanitized PnPDeviceId.
    /// </summary>
    [JsonIgnore]
    public string UniqueKey =>
        string.IsNullOrWhiteSpace(SerialNumber)
            ? SanitizeKey(PnPDeviceId)
            : SanitizeKey(SerialNumber);

    /// <summary>Display-friendly label shown in the UI.</summary>
    [JsonIgnore]
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Model)
            ? $"USB Device [{UniqueKey[..Math.Min(12, UniqueKey.Length)]}]"
            : Model;

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>Extracts serial number from a PnP Device ID string.</summary>
    public static string ExtractSerial(string pnpId)
    {
        if (string.IsNullOrWhiteSpace(pnpId)) return string.Empty;

        // PnPDeviceId format: USBSTOR\DISK&VEN_X&PROD_Y&REV_Z\SERIAL&0
        var parts = pnpId.Split('\\');
        if (parts.Length >= 3)
        {
            var candidate = parts[^1]; // last segment
            // Strip the trailing &0 or similar suffix
            var ampIdx = candidate.LastIndexOf('&');
            if (ampIdx > 0)
                candidate = candidate[..ampIdx];
            return candidate.Trim();
        }
        return string.Empty;
    }

    private static string SanitizeKey(string raw) =>
        raw.ToUpperInvariant()
           .Replace(" ", "")
           .Replace("\\", "")
           .Replace("/", "")
           .Replace("&", "")
           .Trim();

    public override string ToString() =>
        $"{DisplayName} | Key={UniqueKey} | Trusted={IsTrusted}";
}
