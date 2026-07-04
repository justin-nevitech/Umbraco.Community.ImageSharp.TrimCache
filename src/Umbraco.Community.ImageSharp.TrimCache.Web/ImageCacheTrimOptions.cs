using Umbraco.Community.ImageSharp.TrimCache.Core;

namespace Umbraco.Community.ImageSharp.TrimCache.Web;

/// <summary>Configuration bound from appsettings under "ImageCacheTrim".</summary>
public sealed class ImageCacheTrimOptions
{
    public const string SectionName = "ImageCacheTrim";

    /// <summary>
    /// The cache folder used when neither <see cref="CacheFolderPath"/> nor Umbraco's
    /// configured ImageSharp cache folder supply one. Matches Umbraco's own default.
    /// </summary>
    public const string DefaultCacheFolderPath = "umbraco/Data/TEMP/MediaCache";

    /// <summary>
    /// Upper bound applied to <see cref="MaxAgeDays"/>, kept well under the point at
    /// which <see cref="TimeSpan.FromDays(double)"/> overflows (~10.6M days). ~10,000
    /// years is effectively "never trim".
    /// </summary>
    public const int MaxAgeDaysCeiling = 3_650_000;

    public bool Enabled { get; set; } = true;
    public int MaxAgeDays { get; set; } = 30;

    /// <summary>
    /// Which cache to trim. Default Auto: use Azure when blob storage is
    /// configured, otherwise the local physical file cache.
    /// </summary>
    public CacheMode Mode { get; set; } = CacheMode.Auto;

    // --- Azure blob cache settings (used when the effective mode is Azure) ---
    public string ConnectionString { get; set; } = string.Empty;
    public string ContainerName { get; set; } = string.Empty;
    public string Prefix { get; set; } = string.Empty;

    /// <summary>
    /// Safety opt-in for Azure mode. With an empty <see cref="Prefix"/> the trimmer
    /// would scan and age-trim the ENTIRE container, which is only safe for a
    /// dedicated cache-only container — never one that also holds media, as Umbraco
    /// Cloud's shared container does. To avoid deleting media by accident, Azure mode
    /// with an empty <see cref="Prefix"/> refuses to run unless this is explicitly set
    /// to <c>true</c> to acknowledge the container holds nothing but the cache.
    /// </summary>
    public bool AllowUnprefixedContainer { get; set; }

    // --- Local physical cache settings (used when the effective mode is Local) ---
    /// <summary>
    /// Path to the ImageSharp physical cache folder, relative to the content root
    /// (or absolute). Leave empty (the default) to follow Umbraco's configured
    /// ImageSharp cache folder (<c>Umbraco:CMS:Imaging:Cache:CacheFolder</c>) so the
    /// two never drift apart. Set this only to trim a different folder.
    /// </summary>
    public string CacheFolderPath { get; set; } = string.Empty;

    /// <summary>
    /// How often the trim runs, in minutes, measured from application startup
    /// (after the initial startup delay) — not at specific wall-clock times.
    /// Default 1440 (24 hours). Minimum 1. Read once at startup, so a change
    /// takes effect on the next app restart, not live.
    /// </summary>
    public int IntervalMinutes { get; set; } = 1440;

    /// <summary>
    /// Delay after application start before the first run, in minutes. Default 5.
    /// Minimum 0.
    /// </summary>
    public int StartupDelayMinutes { get; set; } = 5;

    /// <summary>
    /// In a load-balanced setup, which servers run the trim.
    /// <list type="bullet">
    /// <item><c>null</c> (default): auto. Local mode runs on <b>every</b> server,
    /// because each server has its own physical cache that only it can trim; Azure
    /// mode runs only on the scheduling/single server, because the blob cache is
    /// shared, so one server is enough (and deletes are idempotent anyway).</item>
    /// <item><c>true</c>: always run on every server (e.g. each server keeps its own cache).</item>
    /// <item><c>false</c>: only run on the scheduling/single server (e.g. a shared cache).</item>
    /// </list>
    /// On a single (non load-balanced) server this has no effect — it always runs.
    /// </summary>
    public bool? RunOnEveryServer { get; set; }

    /// <summary>True when Azure blob storage is fully configured.</summary>
    public bool IsAzureConfigured =>
        !string.IsNullOrWhiteSpace(ConnectionString) &&
        !string.IsNullOrWhiteSpace(ContainerName);

    /// <summary>
    /// The mode that will actually be used, resolving Auto to Azure (if configured)
    /// or Local otherwise.
    /// </summary>
    public CacheMode EffectiveMode => Mode switch
    {
        CacheMode.Auto => IsAzureConfigured ? CacheMode.Azure : CacheMode.Local,
        _ => Mode,
    };

    /// <summary>
    /// Whether the service can run with the current configuration. Local mode is
    /// always runnable (it just needs a folder path, which has a default); Azure
    /// mode requires connection string + container.
    /// </summary>
    public bool CanRun => EffectiveMode == CacheMode.Local || IsAzureConfigured;

    /// <summary>
    /// Whether Azure mode is scoped safely: either a <see cref="Prefix"/> confines the
    /// trim to the cache subfolder, or <see cref="AllowUnprefixedContainer"/> explicitly
    /// acknowledges a dedicated cache-only container. Always true outside Azure mode.
    /// Guards against the worst footgun — an unprefixed Azure trim silently deleting
    /// media from a shared container (as on Umbraco Cloud). When this is false the
    /// hosted service refuses to run and deletes nothing.
    /// </summary>
    public bool IsAzurePrefixSafe =>
        EffectiveMode != CacheMode.Azure ||
        !string.IsNullOrWhiteSpace(Prefix) ||
        AllowUnprefixedContainer;

    /// <summary>
    /// Whether the trim should run on every server (not only the scheduling/single
    /// one) in a load-balanced setup. Defaults to true for Local mode (per-server
    /// caches) and false for Azure mode (shared cache), unless
    /// <see cref="RunOnEveryServer"/> overrides it.
    /// </summary>
    public bool RunsOnEveryServer => RunOnEveryServer ?? (EffectiveMode == CacheMode.Local);

    /// <summary>
    /// Resolves the local cache folder to trim. An explicit <see cref="CacheFolderPath"/>
    /// wins; otherwise Umbraco's configured ImageSharp cache folder
    /// (<paramref name="umbracoImagingCacheFolder"/>, from
    /// <c>Umbraco:CMS:Imaging:Cache:CacheFolder</c>); falling back to
    /// <see cref="DefaultCacheFolderPath"/> only if neither is set. Following
    /// Umbraco's own setting keeps the trimmer pointed at the same folder ImageSharp
    /// actually writes to, even when that's been customised.
    /// </summary>
    public string ResolveCacheFolderPath(string? umbracoImagingCacheFolder)
    {
        if (!string.IsNullOrWhiteSpace(CacheFolderPath))
        {
            return CacheFolderPath;
        }

        if (!string.IsNullOrWhiteSpace(umbracoImagingCacheFolder))
        {
            return umbracoImagingCacheFolder;
        }

        return DefaultCacheFolderPath;
    }

    /// <summary>
    /// Resolves <see cref="MaxAgeDays"/> to a TimeSpan, clamped to a safe range.
    /// A negative value is treated as 0 — otherwise the cutoff (now − MaxAge) would
    /// move into the future and make every entry eligible, wiping the whole cache.
    /// Values above <see cref="MaxAgeDaysCeiling"/> are capped to avoid a
    /// <see cref="TimeSpan"/> overflow.
    /// </summary>
    public TimeSpan ResolveMaxAge() =>
        TimeSpan.FromDays(Math.Clamp(MaxAgeDays, 0, MaxAgeDaysCeiling));

    public TimeSpan ResolveInterval() =>
        ScheduleResolver.ResolveInterval(IntervalMinutes);

    public TimeSpan ResolveStartupDelay() =>
        ScheduleResolver.ResolveStartupDelay(StartupDelayMinutes);
}
