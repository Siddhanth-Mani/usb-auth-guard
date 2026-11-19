using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using USBAuthGuard.Core;
using USBAuthGuard.Logging;
using USBAuthGuard.Services;
using USBAuthGuard.UI;
using WpfApp = System.Windows.Application;

namespace USBAuthGuard;

/// <summary>
/// Application entry point.
/// Sets up the DI container, starts the background service, and launches the WPF app.
/// </summary>
public sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // WinForms required for NotifyIcon
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

        var host = BuildHost();
        var services = host.Services;

        // Resolve all services
        var bgService  = services.GetRequiredService<USBAuthBackgroundService>();
        var trayMgr    = services.GetRequiredService<TrayManager>();
        var config     = services.GetRequiredService<ConfigManager>();
        var auth       = services.GetRequiredService<AuthManager>();
        var monitor    = services.GetRequiredService<USBMonitor>();
        var vault      = services.GetRequiredService<VaultManager>();
        var startup    = services.GetRequiredService<StartupManager>();
        var appLogger  = services.GetRequiredService<AppLogger>();
        var state      = services.GetRequiredService<StateEngine>();
        var policy     = services.GetRequiredService<PolicyEngine>();

        // Build WPF application (must be on STA thread)
        var app = new WpfApp { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };

        // Create ViewModel and MainWindow
        var vm = new DashboardViewModel(state, auth, policy, monitor, appLogger);
        var mainWindow = new MainWindow(vm, auth, monitor, config, vault, startup, appLogger, bgService, policy, state);

        // Now wire tray actions with real window reference
        trayMgr.SetActions(
            showMainWindow: () =>
            {
                mainWindow.Show();
                mainWindow.Activate();
                if (mainWindow.WindowState == System.Windows.WindowState.Minimized)
                    mainWindow.WindowState = System.Windows.WindowState.Normal;
            },
            exitApp: () =>
            {
                trayMgr.Dispose();
                WpfApp.Current.Shutdown();
            });

        // Wire state-change balloon notifications
        state.StateChanged += (_, s) =>
        {
            var isUnlocked = s == SecurityState.Unlocked;
            trayMgr.UpdateTooltip(isUnlocked ? "Unlocked 🔓" : "Locked 🔒");
            trayMgr.ShowBalloon(
                "USBAuthGuard",
                isUnlocked
                    ? "Trusted USB detected — workstation active."
                    : "Trusted USB removed — workstation locked.",
                isUnlocked
                    ? System.Windows.Forms.ToolTipIcon.Info
                    : System.Windows.Forms.ToolTipIcon.Warning);
        };

        // Initialize tray icon
        trayMgr.Initialize();

        // Start background host (USB monitoring)
        _ = host.StartAsync();

        // Show main window on launch
        mainWindow.Show();

        // Run WPF message pump
        app.Run();

        // Graceful shutdown
        host.StopAsync().GetAwaiter().GetResult();
        host.Dispose();
        trayMgr.Dispose();
    }

    // ── DI Container ───────────────────────────────────────────────────────

    private static IHost BuildHost() =>
        Host.CreateDefaultBuilder()
            .ConfigureLogging(log =>
            {
                log.ClearProviders();
                log.AddConsole();
                log.SetMinimumLevel(LogLevel.Information);
            })
            .ConfigureServices(services =>
            {
                // Core singletons
                services.AddSingleton<ConfigManager>();
                services.AddSingleton<USBMonitor>();
                services.AddSingleton<AuthManager>();
                services.AddSingleton<StateEngine>();
                services.AddSingleton<SystemController>();
                services.AddSingleton<PolicyEngine>();
                services.AddSingleton<VaultManager>();

                // Register background service as singleton FIRST,
                // then add as hosted service from the same instance.
                // This prevents two instances being created.
                services.AddSingleton<USBAuthBackgroundService>();
                services.AddHostedService(sp => sp.GetRequiredService<USBAuthBackgroundService>());

                // Services
                services.AddSingleton<StartupManager>();

                // Logging
                services.AddSingleton<AppLogger>();
                services.AddSingleton<EventLogManager>();

                // UI — TrayManager registered with placeholder actions (patched after window creation)
                services.AddSingleton<TrayManager>(sp => new TrayManager(
                    sp.GetRequiredService<PolicyEngine>()));
            })
            .Build();
}
