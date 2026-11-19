using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using USBAuthGuard.Core;
using USBAuthGuard.Logging;

namespace USBAuthGuard.UI;

/// <summary>
/// MVVM ViewModel for the main dashboard.
/// Exposes live-updating properties bound to the WPF dashboard UI.
/// </summary>
public sealed class DashboardViewModel : INotifyPropertyChanged
{
    private readonly StateEngine _state;
    private readonly AuthManager _auth;
    private readonly PolicyEngine _policy;
    private readonly USBMonitor _monitor;
    private readonly AppLogger _appLogger;

    // ── Observable Properties ──────────────────────────────────────────────

    private string _securityStatus = "Initializing…";
    public string SecurityStatus
    {
        get => _securityStatus;
        private set => SetField(ref _securityStatus, value);
    }

    private bool _isLocked = true;
    public bool IsLocked
    {
        get => _isLocked;
        private set
        {
            if (SetField(ref _isLocked, value))
            {
                OnPropertyChanged(nameof(IsUnlocked));
                OnPropertyChanged(nameof(StatusColor));
                OnPropertyChanged(nameof(StatusIcon));
            }
        }
    }

    public bool IsUnlocked => !_isLocked;

    public string StatusColor => IsLocked ? "#EF4444" : "#22C55E";
    public string StatusIcon  => IsLocked ? "🔒" : "🔓";

    private string _connectedDevices = "None";
    public string ConnectedDevices
    {
        get => _connectedDevices;
        private set => SetField(ref _connectedDevices, value);
    }

    private int _trustedDeviceCount;
    public int TrustedDeviceCount
    {
        get => _trustedDeviceCount;
        private set => SetField(ref _trustedDeviceCount, value);
    }

    public ObservableCollection<string> RecentLogs { get; } = [];
    public ObservableCollection<string> TrustedKeys { get; } = [];

    // ── Commands ───────────────────────────────────────────────────────────

    public ICommand LockNowCommand { get; }
    public ICommand RefreshCommand { get; }

    // ── Constructor ────────────────────────────────────────────────────────

    public DashboardViewModel(
        StateEngine state,
        AuthManager auth,
        PolicyEngine policy,
        USBMonitor monitor,
        AppLogger appLogger)
    {
        _state     = state;
        _auth      = auth;
        _policy    = policy;
        _monitor   = monitor;
        _appLogger = appLogger;

        LockNowCommand  = new RelayCommand(_ => _policy.ForceLockNow());
        RefreshCommand  = new RelayCommand(_ => Refresh());

        // Subscribe to events
        _state.StateChanged   += OnStateChanged;
        _appLogger.EntryLogged += OnLogEntryLogged;

        Refresh();
    }

    // ── Public API ─────────────────────────────────────────────────────────

    public void Refresh()
    {
        // Security state
        var s = _state.CurrentState;
        IsLocked       = s == SecurityState.Locked;
        SecurityStatus = IsLocked ? "🔒 LOCKED" : "🔓 UNLOCKED";

        // Connected USB devices
        var devices = _monitor.GetConnectedDevices();
        ConnectedDevices = devices.Count == 0
            ? "None"
            : string.Join(", ", devices.Select(d => d.DisplayName));

        // Trusted key list
        var keys = _auth.GetTrustedKeys();
        TrustedDeviceCount = keys.Count;

        TrustedKeys.Clear();
        foreach (var k in keys)
            TrustedKeys.Add(k);

        // Recent logs
        var logs = _appLogger.GetRecentEntries().TakeLast(100).Reverse();
        RecentLogs.Clear();
        foreach (var e in logs)
            RecentLogs.Add($"[{e.Timestamp:HH:mm:ss}] [{e.Level}] {e.Module}: {e.Message}");
    }

    // ── Event Handlers ─────────────────────────────────────────────────────

    private void OnStateChanged(object? sender, SecurityState state)
    {
        RunOnUiThread(Refresh);
    }

    private void OnLogEntryLogged(object? sender, LogEntry entry)
    {
        RunOnUiThread(() =>
        {
            RecentLogs.Insert(0,
                $"[{entry.Timestamp:HH:mm:ss}] [{entry.Level}] {entry.Module}: {entry.Message}");

            while (RecentLogs.Count > 100)
                RecentLogs.RemoveAt(RecentLogs.Count - 1);
        });
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }

    // ── INotifyPropertyChanged ─────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}

/// <summary>Simple ICommand relay implementation.</summary>
public sealed class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    : ICommand
{
    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => execute(parameter);
    public event EventHandler? CanExecuteChanged
    {
        add    => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}
