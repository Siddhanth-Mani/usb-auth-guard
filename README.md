# USBAuthGuard

Use a USB drive you trust as a physical key for your Windows PC. Pull the key out and the workstation locks. Plug in an unknown drive and you get a warning.

Built with **C#, .NET 8 and WPF**.

## Features

- **Trusted devices:** register USB drives by serial number
- **Lock on removal:** locks Windows (`LockWorkStation`) when a trusted key is removed, after a configurable delay
- **Untrusted device alerts:** warns when an unknown USB drive is plugged in, and can optionally lock
- **File vault:** encrypts a chosen folder with AES-256-CBC; files decrypt only while the trusted key is present
- **Background service and tray icon:** runs quietly, with dashboard and admin panel windows
- **Event logging:** writes to a log file and, optionally, the Windows Event Log
- **Start with Windows:** optional

## How it works

`USBMonitor` watches WMI (`Win32_DiskDrive`) for USB insert and remove events, then `PolicyEngine` decides what to do from `Config/policies.json` and `Config/trusted_devices.json`. `StateEngine` tracks whether the trusted key is present, and `SystemController` performs the lock. Services are wired together with `Microsoft.Extensions.Hosting` dependency injection.

## Run it

Requires Windows 10/11 and the .NET 8 SDK.

```bash
dotnet run --configuration Release
```

## Limitations

- The vault key is derived (PBKDF2-SHA256, 100,000 iterations) from the USB drive's **serial number**, which anyone holding the drive can read. Treat the vault as tamper deterrence, not strong encryption. A real product should add a user PIN or a key stored on the drive.
- USB serial numbers can be spoofed with specialised hardware.
- Windows only.

## Author

**Siddhanth Kartik Mani**: [portfolio](https://siddhanth.ryzn.pro) · [GitHub](https://github.com/Siddhanth-Mani)
