using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using USBAuthGuard.Core;
using USBAuthGuard.Logging;
using USBAuthGuard.Services;
using FolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;
using MessageBox = System.Windows.MessageBox;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;
using WpfStyle = System.Windows.Style;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace USBAuthGuard.UI;

public partial class MainWindow : Window
{
    // ── Services ───────────────────────────────────────────────────────────
    private readonly DashboardViewModel     _vm;
    private readonly AuthManager            _auth;
    private readonly USBMonitor             _monitor;
    private readonly ConfigManager          _config;
    private readonly VaultManager           _vault;
    private readonly StartupManager         _startup;
    private readonly AppLogger              _appLogger;
    private readonly USBAuthBackgroundService _bgService;
    private readonly PolicyEngine           _policy;
    private readonly StateEngine            _state;

    // ── Nav state ──────────────────────────────────────────────────────────
    private string _currentPage = "Dashboard";
    private readonly Dictionary<string, WpfButton> _navButtons;
    private readonly Dictionary<string, UIElement> _pages;

    // ── Auto-refresh timer ─────────────────────────────────────────────────
    private readonly DispatcherTimer _refreshTimer;

    // ── Constructor ────────────────────────────────────────────────────────
    public MainWindow(
        DashboardViewModel vm,
        AuthManager auth,
        USBMonitor monitor,
        ConfigManager config,
        VaultManager vault,
        StartupManager startup,
        AppLogger appLogger,
        USBAuthBackgroundService bgService,
        PolicyEngine policy,
        StateEngine state)
    {
        InitializeComponent();

        _vm        = vm;
        _auth      = auth;
        _monitor   = monitor;
        _config    = config;
        _vault     = vault;
        _startup   = startup;
        _appLogger = appLogger;
        _bgService = bgService;
        _policy    = policy;
        _state     = state;

        // Build nav lookups
        _navButtons = new Dictionary<string, WpfButton>
        {
            ["Dashboard"] = BtnNavDashboard,
            ["Devices"]   = BtnNavDevices,
            ["Vault"]     = BtnNavVault,
            ["Logs"]      = BtnNavLogs,
            ["Settings"]  = BtnNavSettings,
            ["Admin"]     = BtnNavAdmin,
        };
        _pages = new Dictionary<string, UIElement>
        {
            ["Dashboard"] = PageDashboard,
            ["Devices"]   = PageDevices,
            ["Vault"]     = PageVault,
            ["Logs"]      = PageLogs,
            ["Settings"]  = PageSettings,
        };

        // Subscribe to events — all fire on background threads, must dispatch
        _state.StateChanged    += (_, _) => Dispatcher.BeginInvoke(RefreshDashboard);
        _appLogger.EntryLogged += (_, e) => Dispatcher.BeginInvoke(() => OnNewLogEntry(e));

        // Minimize to tray on close
        Closing += (_, e) => { e.Cancel = true; Hide(); };

        // Auto-refresh every 5 seconds
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _refreshTimer.Tick += (_, _) => RefreshCurrentPage();
        _refreshTimer.Start();

        // Initial population
        LoadSettings();
        RefreshDashboard();
        RefreshVaultStatus();
    }

    // ── Navigation ─────────────────────────────────────────────────────────

    private void BtnNav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton btn) return;
        var page = btn.Tag?.ToString() ?? "Dashboard";

        if (page == "Admin")
        {
            OpenAdminPanel();
            return;
        }

        NavigateTo(page);
    }

    private void NavigateTo(string page)
    {
        _currentPage = page;

        // Toggle page visibility
        foreach (var (key, el) in _pages)
            el.Visibility = key == page ? Visibility.Visible : Visibility.Collapsed;

        // Update nav WpfButton styles
        var activeStyle = (WpfStyle)Resources["NavBtnActive"];
        var inactiveStyle = (WpfStyle)Resources["NavBtn"];
        foreach (var (key, btn) in _navButtons)
        {
            if (key == "Admin") continue; // admin is always NavBtn
            btn.Style = key == page ? activeStyle : inactiveStyle;
        }

        // Page-specific load
        switch (page)
        {
            case "Devices":  RefreshDevicePage(); break;
            case "Logs":     RefreshLogPage();    break;
            case "Vault":    RefreshVaultStatus(); break;
            case "Settings": LoadSettings();      break;
        }
    }

    private void RefreshCurrentPage()
    {
        switch (_currentPage)
        {
            case "Dashboard": RefreshDashboard(); break;
            case "Devices":   RefreshDevicePage(); break;
            case "Logs":      RefreshLogPage();    break;
        }
    }

    private void OpenAdminPanel()
    {
        var panel = new AdminPanel(
            _policy, _state, _auth, _config, _startup, _appLogger, _bgService)
        {
            Owner = this
        };
        panel.ShowDialog();
        RefreshDashboard();
    }

    // ── Dashboard ──────────────────────────────────────────────────────────

    private void RefreshDashboard()
    {
        var locked = _state.CurrentState == SecurityState.Locked;

        // Status hero
        TxtStatus.Text       = locked ? "🔒  LOCKED" : "🔓  UNLOCKED";
        TxtStatus.Foreground = locked
            ? new SolidColorBrush(WpfColor.FromRgb(0xEF, 0x44, 0x44))
            : new SolidColorBrush(WpfColor.FromRgb(0x22, 0xC5, 0x5E));
        TxtStatusIcon.Text   = locked ? "🔒" : "🔓";

        // Sidebar state indicator
        TxtSidebarState.Text       = locked ? "● Locked" : "● Unlocked";
        TxtSidebarState.Foreground = locked
            ? new SolidColorBrush(WpfColor.FromRgb(0xEF, 0x44, 0x44))
            : new SolidColorBrush(WpfColor.FromRgb(0x22, 0xC5, 0x5E));

        // Connected devices
        var connected = _monitor.GetConnectedDevices();
        TxtDeviceInfo.Text    = connected.Count == 0
            ? "No USB drives detected"
            : string.Join(", ", connected.Select(d => d.DisplayName));

        // Stats
        TxtStatTrusted.Text   = _auth.GetTrustedKeys().Count.ToString();
        TxtStatConnected.Text = connected.Count.ToString();
        TxtStatLogs.Text      = _appLogger.GetRecentEntries().Count.ToString();

        // Vault stat
        var vaultFolder = _config.PoliciesConfig.VaultFolder;
        TxtStatVault.Text       = string.IsNullOrWhiteSpace(vaultFolder) ? "OFF" : "ON";
        TxtStatVault.Foreground = string.IsNullOrWhiteSpace(vaultFolder)
            ? (SolidColorBrush)Resources["TextMuted"]
            : new SolidColorBrush(WpfColor.FromRgb(0x22, 0xC5, 0x5E));

        // Subtitle
        TxtDashSubtitle.Text = locked
            ? "Insert your trusted USB to unlock."
            : $"Authenticated via USB — {connected.FirstOrDefault(d => _auth.IsTrusted(d))?.DisplayName ?? "trusted device"}.";
    }

    private void OnNewLogEntry(LogEntry entry)
    {
        // Prepend to dashboard live feed
        var line = $"[{entry.Timestamp:HH:mm:ss}] [{entry.Level,-5}] {entry.Module}: {entry.Message}";
        LstDashLogs.Items.Insert(0, line);
        while (LstDashLogs.Items.Count > 80)
            LstDashLogs.Items.RemoveAt(LstDashLogs.Items.Count - 1);

        TxtStatLogs.Text = _appLogger.GetRecentEntries().Count.ToString();

        // If log page is open, append there too
        if (_currentPage == "Logs")
        {
            LstAllLogs.Items.Insert(0, line);
            while (LstAllLogs.Items.Count > 500)
                LstAllLogs.Items.RemoveAt(LstAllLogs.Items.Count - 1);
        }
    }

    private void BtnLockNow_Click(object s, RoutedEventArgs e)
    {
        _policy.ForceLockNow();
        _appLogger.Log("INFO", "UI", "Manual lock triggered.");
    }

    private void BtnRefreshDashboard_Click(object s, RoutedEventArgs e) => RefreshDashboard();

    // ── Devices Page ───────────────────────────────────────────────────────

    private void RefreshDevicePage()
    {
        // Connected drives
        var connected = _monitor.GetConnectedDevices();
        LstConnectedDevices.Items.Clear();
        if (connected.Count == 0)
        {
            LstConnectedDevices.Items.Add("No USB storage devices detected.");
        }
        else
        {
            foreach (var d in connected)
                LstConnectedDevices.Items.Add($"{d.DisplayName}   [{d.UniqueKey}]");
        }

        // Trusted keys
        LstTrustedKeys.Items.Clear();
        var keys = _auth.GetTrustedKeys();
        if (keys.Count == 0)
            LstTrustedKeys.Items.Add("No trusted devices configured yet.");
        else
            foreach (var k in keys)
                LstTrustedKeys.Items.Add(k);
    }

    private void BtnScanDevices_Click(object s, RoutedEventArgs e) => RefreshDevicePage();

    private void BtnTrustDevice_Click(object s, RoutedEventArgs e)
    {
        if (LstConnectedDevices.SelectedItem is not string selected) return;
        if (selected.StartsWith("No USB")) return;

        // Parse key from "ModelName   [KEY]"
        var start = selected.LastIndexOf('[');
        var end   = selected.LastIndexOf(']');
        if (start < 0 || end < 0 || end <= start) return;

        var key     = selected[(start + 1)..end].Trim();
        var devices = _monitor.GetConnectedDevices();
        var device  = devices.FirstOrDefault(d => d.UniqueKey == key);
        if (device is null)
        {
            MessageBox.Show("Could not match the selected item to a device. Try clicking Scan first.",
                "Trust Device", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _auth.AddTrustedDevice(device);
        _appLogger.Log("INFO", "UI", $"Device trusted: {device.DisplayName} [{device.UniqueKey}]");
        RefreshDevicePage();
        RefreshDashboard();
        MessageBox.Show($"✅ Device trusted:\n{device.DisplayName}",
            "USBAuthGuard", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnRemoveTrustedKey_Click(object s, RoutedEventArgs e)
    {
        if (LstTrustedKeys.SelectedItem is not string key) return;
        if (key.StartsWith("No trusted")) return;

        var result = MessageBox.Show(
            $"Remove this trusted device?\n\n{key}\n\nYou will need to re-trust it to unlock your workstation.",
            "Confirm Remove", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        _auth.RemoveTrustedDevice(key);
        _appLogger.Log("INFO", "UI", $"Device removed: {key}");
        RefreshDevicePage();
        RefreshDashboard();
    }

    // ── Vault Page ─────────────────────────────────────────────────────────

    private void RefreshVaultStatus()
    {
        var folder = _config.PoliciesConfig.VaultFolder;
        TxtVaultPath.Text = string.IsNullOrWhiteSpace(folder) ? "No folder selected" : folder;

        if (string.IsNullOrWhiteSpace(folder))
        {
            TxtVaultStatus.Text = "Vault is not configured. Select a folder above.";
            TxtVaultStatus.Foreground = (SolidColorBrush)Resources["TextMuted"];
        }
        else
        {
            var encCount = Directory.Exists(folder)
                ? Directory.GetFiles(folder, "*.vlt").Length
                : 0;
            var rawCount = Directory.Exists(folder)
                ? Directory.GetFiles(folder).Length - encCount
                : 0;

            TxtVaultStatus.Text = encCount > 0
                ? $"🔒 Vault is LOCKED — {encCount} encrypted file(s), {rawCount} unencrypted file(s)."
                : $"🔓 Vault is UNLOCKED — {rawCount} file(s) in folder (not encrypted).";
            TxtVaultStatus.Foreground = encCount > 0
                ? new SolidColorBrush(WpfColor.FromRgb(0xEF, 0x44, 0x44))
                : new SolidColorBrush(WpfColor.FromRgb(0x22, 0xC5, 0x5E));
        }
    }

    private void BtnBrowseVault_Click(object s, RoutedEventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description            = "Select the folder to protect with USBAuthGuard Vault",
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            _config.PoliciesConfig.VaultFolder = dialog.SelectedPath;
            _config.SavePoliciesConfig();
            _appLogger.Log("INFO", "Vault", $"Vault folder set: {dialog.SelectedPath}");
            RefreshVaultStatus();
            RefreshDashboard();
        }
    }

    private void BtnLockVault_Click(object s, RoutedEventArgs e)
    {
        var folder = _config.PoliciesConfig.VaultFolder;
        if (string.IsNullOrWhiteSpace(folder))
        { MessageBox.Show("No vault folder configured.", "Vault", MessageBoxButton.OK, MessageBoxImage.Information); return; }

        var key = _bgService.GetActiveDeviceKey();
        if (key is null)
        { MessageBox.Show("No trusted USB connected.\nInsert your trusted USB drive to use the vault.", "Vault", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

        var result = _vault.LockVault(folder, key);
        if (result.Success)
            _appLogger.Log("INFO", "Vault", $"Vault locked — {result.ProcessedFiles} file(s) encrypted.");
        else
            _appLogger.Log("WARN", "Vault", $"Vault lock had {result.Errors.Count} error(s): {string.Join("; ", result.Errors)}");

        RefreshVaultStatus();
    }

    private void BtnUnlockVault_Click(object s, RoutedEventArgs e)
    {
        var folder = _config.PoliciesConfig.VaultFolder;
        if (string.IsNullOrWhiteSpace(folder))
        { MessageBox.Show("No vault folder configured.", "Vault", MessageBoxButton.OK, MessageBoxImage.Information); return; }

        var key = _bgService.GetActiveDeviceKey();
        if (key is null)
        { MessageBox.Show("No trusted USB connected.\nInsert your trusted USB drive to decrypt vault files.", "Vault", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

        var result = _vault.UnlockVault(folder, key);
        if (result.Success)
            _appLogger.Log("INFO", "Vault", $"Vault unlocked — {result.ProcessedFiles} file(s) decrypted.");
        else
            _appLogger.Log("WARN", "Vault", $"Vault unlock had {result.Errors.Count} error(s): {string.Join("; ", result.Errors)}");

        RefreshVaultStatus();
    }

    // ── Logs Page ──────────────────────────────────────────────────────────

    private void RefreshLogPage()
    {
        LstAllLogs.Items.Clear();
        foreach (var entry in _appLogger.GetRecentEntries().Reverse())
            LstAllLogs.Items.Add(
                $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss}] [{entry.Level,-5}] [{entry.Module,-10}] {entry.Message}");
    }

    private void BtnRefreshLogs_Click(object s, RoutedEventArgs e) => RefreshLogPage();

    private void BtnClearLogs_Click(object s, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "Clear all log entries? This cannot be undone.",
            "Clear Logs", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        var path = _appLogger.GetLogFilePath();
        if (File.Exists(path)) File.WriteAllText(path, string.Empty);
        LstAllLogs.Items.Clear();
        LstDashLogs.Items.Clear();
        _appLogger.Log("INFO", "UI", "Log file cleared by user.");
    }

    private void BtnOpenLogFile_Click(object s, RoutedEventArgs e)
    {
        var path = _appLogger.GetLogFilePath();
        if (File.Exists(path))
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        else
            MessageBox.Show("Log file not found yet.", "Logs", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ── Settings Page ──────────────────────────────────────────────────────

    private void LoadSettings()
    {
        TxtLockDelay.Text              = _config.DeviceConfig.LockDelaySeconds.ToString();
        ChkLockOnRemoval.IsChecked     = _config.PoliciesConfig.LockOnTrustedRemoval;
        ChkLockOnUntrusted.IsChecked   = _config.DeviceConfig.LockOnUntrustedInsert;
        ChkWarnUntrusted.IsChecked     = _config.PoliciesConfig.WarnOnUntrustedInsert;
        ChkStartWithWindows.IsChecked  = _startup.IsStartupEnabled;
        TxtSettingsSaved.Text          = string.Empty;
    }

    private void BtnSaveSettings_Click(object s, RoutedEventArgs e)
    {
        if (!int.TryParse(TxtLockDelay.Text, out int delay) || delay < 0)
        {
            MessageBox.Show("Lock delay must be a non-negative integer.", "Settings",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _config.DeviceConfig.LockDelaySeconds       = delay;
        _config.DeviceConfig.LockOnUntrustedInsert  = ChkLockOnUntrusted.IsChecked == true;
        _config.PoliciesConfig.LockOnTrustedRemoval = ChkLockOnRemoval.IsChecked == true;
        _config.PoliciesConfig.WarnOnUntrustedInsert = ChkWarnUntrusted.IsChecked == true;
        _config.SaveDeviceConfig();
        _config.SavePoliciesConfig();

        if (ChkStartWithWindows.IsChecked == true)
            _startup.EnableStartup();
        else
            _startup.DisableStartup();

        _appLogger.Log("INFO", "Settings", "Settings saved.");
        TxtSettingsSaved.Text = "✅ Settings saved successfully.";
    }

    private void BtnResetSettings_Click(object s, RoutedEventArgs e)
    {
        TxtLockDelay.Text             = "5";
        ChkLockOnRemoval.IsChecked    = true;
        ChkLockOnUntrusted.IsChecked  = false;
        ChkWarnUntrusted.IsChecked    = true;
        ChkStartWithWindows.IsChecked = false;
        TxtSettingsSaved.Text         = "Defaults loaded — click Save to apply.";
    }
}
