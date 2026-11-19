using System.IO;
using System.Windows;
using MessageBox = System.Windows.MessageBox;
using USBAuthGuard.Core;
using USBAuthGuard.Logging;
using USBAuthGuard.Services;

namespace USBAuthGuard.UI;

public partial class AdminPanel : Window
{
    private readonly PolicyEngine _policy;
    private readonly StateEngine _state;
    private readonly AuthManager _auth;
    private readonly ConfigManager _config;
    private readonly StartupManager _startup;
    private readonly AppLogger _appLogger;
    private readonly USBAuthBackgroundService _bgService;

    public AdminPanel(
        PolicyEngine policy,
        StateEngine state,
        AuthManager auth,
        ConfigManager config,
        StartupManager startup,
        AppLogger appLogger,
        USBAuthBackgroundService bgService)
    {
        InitializeComponent();
        _policy    = policy;
        _state     = state;
        _auth      = auth;
        _config    = config;
        _startup   = startup;
        _appLogger = appLogger;
        _bgService = bgService;

        RefreshState();
    }

    private void RefreshState()
    {
        TxtAdminState.Text   = _state.CurrentState.ToString();
        TxtAdminDevice.Text  = _bgService.GetActiveDeviceKey() ?? "None";
        TxtAdminTrusted.Text = _auth.GetTrustedKeys().Count.ToString();
        TxtStartupStatus.Text = $"Startup: {(_startup.IsStartupEnabled ? "Enabled" : "Disabled")}";
    }

    private void BtnLock_Click(object s, RoutedEventArgs e)
    {
        _policy.ForceLockNow();
        _appLogger.Log("INFO", "Admin", "Manual lock triggered via Admin Panel.");
        RefreshState();
    }

    private void BtnClearLogs_Click(object s, RoutedEventArgs e)
    {
        var logPath = _appLogger.GetLogFilePath();
        if (File.Exists(logPath))
        {
            File.WriteAllText(logPath, string.Empty);
            _appLogger.Log("INFO", "Admin", "Security log cleared by admin.");
        }
        MessageBox.Show("Log file cleared.", "Admin Panel", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnReloadCfg_Click(object s, RoutedEventArgs e)
    {
        _config.Load();
        _auth.Reload();
        _appLogger.Log("INFO", "Admin", "Configuration reloaded.");
        RefreshState();
        MessageBox.Show("Configuration reloaded.", "Admin Panel", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnEnableStartup_Click(object s, RoutedEventArgs e)
    {
        bool ok = _startup.EnableStartup();
        TxtStartupStatus.Text = ok ? "Startup: Enabled ✅" : "Startup: Failed to enable ❌";
        _appLogger.Log("INFO", "Admin", $"Startup enable attempt: {ok}");
    }

    private void BtnDisableStartup_Click(object s, RoutedEventArgs e)
    {
        bool ok = _startup.DisableStartup();
        TxtStartupStatus.Text = ok ? "Startup: Disabled ✅" : "Startup: Failed to disable ❌";
        _appLogger.Log("INFO", "Admin", $"Startup disable attempt: {ok}");
    }

    private void BtnClose_Click(object s, RoutedEventArgs e) => Close();
}
