using System.Drawing;
using System.IO;
using System.Windows.Forms;
using USBAuthGuard.Core;
using WpfApplication = System.Windows.Application;

namespace USBAuthGuard.UI;

/// <summary>
/// Manages the system tray icon and context menu.
/// Actions are set post-construction via <see cref="SetActions"/> once the main window exists.
/// </summary>
public sealed class TrayManager : IDisposable
{
    private NotifyIcon? _notifyIcon;
    private readonly PolicyEngine _policy;
    private Action _showMainWindow = () => { };
    private Action _exitApp = () => { };
    private bool _disposed;

    public TrayManager(PolicyEngine policy)
    {
        _policy = policy;
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>Patches the window actions after the main window is created.</summary>
    public void SetActions(Action showMainWindow, Action exitApp)
    {
        _showMainWindow = showMainWindow;
        _exitApp = exitApp;
    }

    public void Initialize()
    {
        _notifyIcon = new NotifyIcon
        {
            Text    = "USBAuthGuard — Running",
            Visible = true,
            Icon    = LoadIcon()
        };

        _notifyIcon.DoubleClick          += (_, _) => InvokeOnUiThread(_showMainWindow);
        _notifyIcon.ContextMenuStrip      = BuildContextMenu();
    }

    public void ShowBalloon(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        _notifyIcon?.ShowBalloonTip(4000, title, message, icon);
    }

    public void UpdateTooltip(string text)
    {
        if (_notifyIcon is null) return;
        var full = $"USBAuthGuard — {text}";
        _notifyIcon.Text = full.Length > 63 ? full[..63] : full;
    }

    // ── Private ────────────────────────────────────────────────────────────

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip
        {
            BackColor = System.Drawing.Color.FromArgb(26, 29, 46),
            ForeColor = System.Drawing.Color.White,
            RenderMode = ToolStripRenderMode.System
        };

        var header = new ToolStripLabel("🔐  USBAuthGuard")
        {
            Font      = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = System.Drawing.Color.FromArgb(99, 102, 241)
        };
        menu.Items.Add(header);
        menu.Items.Add(new ToolStripSeparator());

        AddItem(menu, "📊  Open Dashboard", (_, _) => InvokeOnUiThread(_showMainWindow));
        AddItem(menu, "🔒  Lock Now",        (_, _) => _policy.ForceLockNow());
        menu.Items.Add(new ToolStripSeparator());
        AddItem(menu, "❌  Exit",             (_, _) => InvokeOnUiThread(_exitApp));

        return menu;
    }

    private static void AddItem(ContextMenuStrip menu, string text, EventHandler onClick)
    {
        var item = new ToolStripMenuItem(text)
        {
            ForeColor = System.Drawing.Color.White,
            BackColor = System.Drawing.Color.FromArgb(26, 29, 46)
        };
        item.Click += onClick;
        menu.Items.Add(item);
    }

    private static void InvokeOnUiThread(Action action)
    {
        var dispatcher = WpfApplication.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
            dispatcher.BeginInvoke(action);
        else
            action();
    }

    private static Icon LoadIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "shield.ico");
        try
        {
            if (File.Exists(iconPath))
                return new Icon(iconPath);
        }
        catch { /* fall through */ }
        return SystemIcons.Shield;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
    }
}
