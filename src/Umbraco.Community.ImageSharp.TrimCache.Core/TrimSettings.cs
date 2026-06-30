namespace Umbraco.Community.ImageSharp.TrimCache.Core;

/// <summary>
/// Settings controlling a single trim run. Provider- and host-agnostic.
/// </summary>
public sealed class TrimSettings
{
    /// <summary>
    /// Entries whose LastModified is older than this are eligible for deletion.
    /// Deleting a still-used variant simply triggers regeneration on next request,
    /// so this can be set fairly aggressively.
    /// </summary>
    public TimeSpan MaxAge { get; init; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Entries modified more recently than this are never deleted, to avoid
    /// racing a variant that may be mid-write.
    /// </summary>
    public TimeSpan SafetyWindow { get; init; } = TimeSpan.FromMinutes(5);
}
