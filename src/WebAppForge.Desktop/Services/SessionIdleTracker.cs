using System;

namespace WebAppForge.Desktop.Services;

/// <summary>Tracks the time since the last user input. Every input calls <see cref="Touch"/>, which is the keep-alive.</summary>
public sealed class SessionIdleTracker
{
    private DateTime _lastActivity;

    public SessionIdleTracker(TimeSpan timeout, DateTime startedAt)
    {
        Timeout = timeout;
        _lastActivity = startedAt;
    }

    public TimeSpan Timeout { get; set; }

    /// <summary>Records user input at <paramref name="now"/>.</summary>
    public void Touch(DateTime now) => _lastActivity = now;

    /// <summary>True once <see cref="Timeout"/> has passed since the last input.</summary>
    public bool IsIdle(DateTime now) => now - _lastActivity >= Timeout;
}
