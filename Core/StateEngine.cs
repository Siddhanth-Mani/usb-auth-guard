using Microsoft.Extensions.Logging;

namespace USBAuthGuard.Core;

/// <summary>Security state of the workstation as tracked by USBAuthGuard.</summary>
public enum SecurityState
{
    /// <summary>No trusted USB is present; workstation is or should be locked.</summary>
    Locked,

    /// <summary>A trusted USB is present; workstation is unlocked/active.</summary>
    Unlocked
}

/// <summary>
/// Tracks the current <see cref="SecurityState"/> and prevents duplicate transitions.
/// Raises <see cref="StateChanged"/> when the state actually changes.
/// </summary>
public sealed class StateEngine
{
    private SecurityState _currentState = SecurityState.Locked;
    private readonly object _lock = new();
    private readonly ILogger<StateEngine> _logger;

    /// <summary>Fired when the security state transitions.</summary>
    public event EventHandler<SecurityState>? StateChanged;

    public StateEngine(ILogger<StateEngine> logger)
    {
        _logger = logger;
    }

    /// <summary>Current security state (thread-safe read).</summary>
    public SecurityState CurrentState
    {
        get { lock (_lock) return _currentState; }
    }

    /// <summary>
    /// Attempts to transition to <paramref name="newState"/>.
    /// Returns <c>true</c> if the transition occurred; <c>false</c> if already in that state.
    /// </summary>
    public bool TryTransition(SecurityState newState)
    {
        lock (_lock)
        {
            if (_currentState == newState)
            {
                _logger.LogDebug("StateEngine: already in {State}, no transition.", newState);
                return false;
            }

            var previous = _currentState;
            _currentState = newState;
            _logger.LogInformation("StateEngine: {From} → {To}", previous, newState);
        }

        // Fire event outside lock to avoid deadlocks
        StateChanged?.Invoke(this, newState);
        return true;
    }

    /// <summary>Force-sets state without raising the event. Use on startup only.</summary>
    public void InitializeState(SecurityState state)
    {
        lock (_lock)
        {
            _currentState = state;
            _logger.LogInformation("StateEngine initialized to {State}.", state);
        }
    }
}
