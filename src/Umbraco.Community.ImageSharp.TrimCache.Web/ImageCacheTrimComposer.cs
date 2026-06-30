using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Community.ImageSharp.TrimCache.Core;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace Umbraco.Community.ImageSharp.TrimCache.Web;

/// <summary>Configuration bound from appsettings under "ImageCacheTrim".</summary>
public sealed class ImageCacheTrimOptions
{
    public const string SectionName = "ImageCacheTrim";

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

    // --- Local physical cache settings (used when the effective mode is Local) ---
    /// <summary>
    /// Path to the ImageSharp physical cache folder, relative to the content root
    /// (or absolute). Default matches Umbraco's default cache location. Override
    /// if you've customised the ImageSharp CacheRootPath/CacheFolder.
    /// </summary>
    public string CacheFolderPath { get; set; } = "umbraco/Data/TEMP/MediaCache";

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
    /// Whether the trim should run on every server (not only the scheduling/single
    /// one) in a load-balanced setup. Defaults to true for Local mode (per-server
    /// caches) and false for Azure mode (shared cache), unless
    /// <see cref="RunOnEveryServer"/> overrides it.
    /// </summary>
    public bool RunsOnEveryServer => RunOnEveryServer ?? (EffectiveMode == CacheMode.Local);

    public TimeSpan ResolveInterval() =>
        ScheduleResolver.ResolveInterval(IntervalMinutes);

    public TimeSpan ResolveStartupDelay() =>
        ScheduleResolver.ResolveStartupDelay(StartupDelayMinutes);
}

/// <summary>Registers the hosted service and binds options. Auto-discovered by Umbraco.</summary>
public sealed class ImageCacheTrimComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services
            .AddOptions<ImageCacheTrimOptions>()
            .Bind(builder.Config.GetSection(ImageCacheTrimOptions.SectionName));

        // Bind once at registration so we only start the background service when
        // it's enabled and runnable. Local mode is runnable by default; Azure mode
        // requires credentials. A disabled or (Azure-forced-but-unconfigured) site
        // never starts the service.
        var options = builder.Config
            .GetSection(ImageCacheTrimOptions.SectionName)
            .Get<ImageCacheTrimOptions>() ?? new ImageCacheTrimOptions();

        if (options.Enabled && options.CanRun)
        {
            builder.Services.AddHostedService<ImageCacheTrimHostedService>();
        }
    }
}
