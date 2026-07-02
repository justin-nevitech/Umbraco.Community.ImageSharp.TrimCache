using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Community.ImageSharp.TrimCache.Azure;
using Umbraco.Community.ImageSharp.TrimCache.Core;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Sync;
using Umbraco.Cms.Infrastructure.HostedServices;

namespace Umbraco.Community.ImageSharp.TrimCache.Web;

/// <summary>
/// Thin Umbraco adapter. Owns only the host concerns — scheduling, runtime-level
/// and server-role guards, shutdown handling, re-entrancy protection, config
/// binding, and selecting the cache backend (Azure blob vs local physical) — then
/// delegates the actual work to the provider-agnostic <see cref="CacheTrimmer"/>.
///
/// Shutdown: an in-flight run observes the application's stopping token and bails
/// out promptly so it never blocks or is killed mid-operation.
///
/// Overlap: a per-instance gate ensures a new run is skipped if the previous one
/// is still going. Cross-server overlap is prevented by the server-role guard.
/// </summary>
public sealed class ImageCacheTrimHostedService : RecurringHostedServiceBase, IDisposable
{
    private readonly IRuntimeState _runtimeState;
    private readonly IServerRoleAccessor _serverRoleAccessor;
    private readonly ILogger<ImageCacheTrimHostedService> _logger;
    private readonly ILogger<CacheTrimmer> _trimmerLogger;
    private readonly ImageCacheTrimOptions _options;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ImagingSettings _imagingSettings;

    // Re-entrancy gate: 1 permit, so only one run proceeds at a time on this
    // instance. A second tick that arrives mid-run fails the Wait(0) and is skipped.
    private readonly SemaphoreSlim _runGate = new(1, 1);

    private bool _disposed;

    public ImageCacheTrimHostedService(
        IRuntimeState runtimeState,
        IServerRoleAccessor serverRoleAccessor,
        ILogger<ImageCacheTrimHostedService> logger,
        ILogger<CacheTrimmer> trimmerLogger,
        IOptions<ImageCacheTrimOptions> options,
        IHostApplicationLifetime appLifetime,
        IHostEnvironment hostEnvironment,
        IOptions<ImagingSettings> imagingSettings)
        // Schedule is resolved from configuration. RecurringHostedServiceBase
        // captures the period at construction, so changes take effect on the
        // next app restart rather than live.
        : base(logger, options.Value.ResolveInterval(), options.Value.ResolveStartupDelay())
    {
        _runtimeState = runtimeState;
        _serverRoleAccessor = serverRoleAccessor;
        _logger = logger;
        _trimmerLogger = trimmerLogger;
        _options = options.Value;
        _appLifetime = appLifetime;
        _hostEnvironment = hostEnvironment;
        _imagingSettings = imagingSettings.Value;

        // Azure mode with no prefix scans (and trims) the WHOLE container. That's fine
        // for a dedicated cache container, but dangerous if it also holds media, so
        // surface it once at startup.
        if (_options.EffectiveMode == CacheMode.Azure
            && string.IsNullOrWhiteSpace(_options.Prefix))
        {
            _logger.LogWarning(
                "ImageCacheTrim: Azure mode is configured with no Prefix, so the ENTIRE " +
                "container '{Container}' will be scanned and trimmed by age. Ensure it " +
                "holds only the ImageSharp cache — never source media — or set a Prefix " +
                "(e.g. 'cache/') to scope the scan.",
                _options.ContainerName);
        }
        // Local mode with an explicit CacheFolderPath override: record which custom
        // folder will be trimmed, so a mis-pointed path is visible. The default (empty)
        // follows Umbraco's own ImageSharp cache folder and needs no note.
        else if (_options.EffectiveMode == CacheMode.Local
            && !string.IsNullOrWhiteSpace(_options.CacheFolderPath))
        {
            _logger.LogInformation(
                "ImageCacheTrim: local mode will trim the configured cache folder '{Path}'. " +
                "Files in that folder older than the max age will be deleted — ensure it is " +
                "the ImageSharp cache folder, not source media.",
                ResolveLocalCacheFolder());
        }
    }

    public override async Task PerformExecuteAsync(object? state)
    {
        if (_runtimeState.Level != RuntimeLevel.Run)
        {
            return;
        }

        // In a load-balanced setup, a shared (Azure) cache only needs to be trimmed
        // by the scheduling/single server, whereas per-server (local) caches must be
        // trimmed on every server — otherwise subscriber servers' caches grow
        // unbounded. RunsOnEveryServer encodes that (auto by mode, overridable).
        if (!_options.RunsOnEveryServer)
        {
            switch (_serverRoleAccessor.CurrentServerRole)
            {
                case ServerRole.Subscriber:
                case ServerRole.Unknown:
                    _logger.LogDebug(
                        "ImageCacheTrim skipped: server role {Role} and the cache is shared, " +
                        "so only the scheduling/single server trims it.",
                        _serverRoleAccessor.CurrentServerRole);
                    return;
            }
        }

        if (!_options.Enabled)
        {
            _logger.LogDebug("ImageCacheTrim skipped: disabled in configuration.");
            return;
        }

        if (!_options.CanRun)
        {
            _logger.LogWarning(
                "ImageCacheTrim skipped: Azure mode selected but ConnectionString/" +
                "ContainerName not configured.");
            return;
        }

        // Don't start a run if shutdown is already underway.
        if (_appLifetime.ApplicationStopping.IsCancellationRequested)
        {
            _logger.LogDebug("ImageCacheTrim skipped: application is stopping.");
            return;
        }

        // Re-entrancy guard: if a previous run is still in progress on this
        // instance, skip this tick rather than overlap.
        if (!await _runGate.WaitAsync(0))
        {
            _logger.LogInformation(
                "ImageCacheTrim skipped: a previous run is still in progress.");
            return;
        }

        try
        {
            ICacheStore store = BuildStore();

            var trimmer = new CacheTrimmer(store, TimeProvider.System, _trimmerLogger);

            // Pass the application's stopping token so an in-flight run cancels
            // promptly when Umbraco shuts down.
            await trimmer.TrimAsync(
                new TrimSettings { MaxAge = _options.ResolveMaxAge() },
                _appLifetime.ApplicationStopping);
        }
        catch (OperationCanceledException)
        {
            // Expected when the app is shutting down mid-run; not an error.
            _logger.LogInformation("ImageCacheTrim cancelled: application is stopping.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ImageCacheTrim run failed.");
        }
        finally
        {
            _runGate.Release();
        }
    }

    private ICacheStore BuildStore()
    {
        if (_options.EffectiveMode == CacheMode.Azure)
        {
            _logger.LogDebug("ImageCacheTrim: using Azure blob cache store.");
            return new AzureBlobCacheStore(new AzureBlobCacheStoreOptions
            {
                ConnectionString = _options.ConnectionString,
                ContainerName = _options.ContainerName,
                Prefix = _options.Prefix,
            });
        }

        // Local physical cache.
        var path = ResolveLocalCacheFolder();
        _logger.LogDebug("ImageCacheTrim: using local physical cache store at {Path}.", path);
        return new PhysicalFileCacheStore(path);
    }

    // Resolves the local cache folder: the configured path, otherwise Umbraco's own
    // ImageSharp cache folder so we trim exactly what it writes — mapped against the
    // content root when relative.
    private string ResolveLocalCacheFolder()
    {
        var path = _options.ResolveCacheFolderPath(_imagingSettings.Cache.CacheFolder);
        if (!Path.IsPathRooted(path))
        {
            path = Path.Combine(_hostEnvironment.ContentRootPath, path.TrimStart('~', '/', '\\'));
        }

        return path;
    }

    // The base RecurringHostedServiceBase.Dispose() is non-virtual, so this hides it
    // (new) and re-declares IDisposable, so disposal via the IDisposable interface
    // (how the host disposes hosted services) runs this and chains to the base —
    // disposing both the base timer and our re-entrancy gate. Disposal happens only
    // after the host has stopped the service, so the gate is never disposed out from
    // under an in-flight run. Guarded so a double-dispose is a no-op.
    public new void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _runGate.Dispose();
        base.Dispose();
    }
}
