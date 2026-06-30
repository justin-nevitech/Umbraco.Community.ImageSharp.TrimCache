namespace Umbraco.Community.ImageSharp.TrimCache.Core;

/// <summary>
/// Pure, host-agnostic resolution of the run schedule. Kept in Core (not the
/// Umbraco adapter) so the clamping rules can be unit-tested without pulling in
/// any Umbraco dependency.
/// </summary>
public static class ScheduleResolver
{
    /// <summary>Minimum allowed interval, to stop a misconfiguration hammering storage.</summary>
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Resolves the run interval from a configured minutes value, clamped to the
    /// floor. The interval is measured from startup, not from wall-clock times.
    /// </summary>
    public static TimeSpan ResolveInterval(int intervalMinutes)
    {
        var minutes = Math.Max(1, intervalMinutes);
        return TimeSpan.FromMinutes(minutes);
    }

    /// <summary>Resolves the startup delay from a configured minutes value, clamped to >= 0.</summary>
    public static TimeSpan ResolveStartupDelay(int startupDelayMinutes)
    {
        var minutes = Math.Max(0, startupDelayMinutes);
        return TimeSpan.FromMinutes(minutes);
    }
}
