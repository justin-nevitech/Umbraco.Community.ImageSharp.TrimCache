#if DEBUG
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Community.ImageSharp.TrimCache.Azure;
using Umbraco.Community.ImageSharp.TrimCache.Core;
using Umbraco.Cms.Core.Hosting;

namespace Umbraco.Community.ImageSharp.TrimCache.Web;

/// <summary>
/// DEBUG-ONLY endpoint to run the trim immediately, so you can test the full
/// cycle locally without waiting for the schedule. Compiled out of Release builds
/// entirely (#if DEBUG), so it can never ship to production.
///
/// Uses the same store selection as the hosted service, so it exercises whichever
/// backend (Azure or local) the current config resolves to.
///
/// Usage (local only):
///   GET /imagecachetrim/list
///   GET /imagecachetrim/run?maxAgeMinutes=2&amp;safetyMinutes=0
/// </summary>
[ApiController]
[Route("imagecachetrim")]
public sealed class ImageCacheTrimDebugController : ControllerBase
{
    private readonly ImageCacheTrimOptions _options;
    private readonly ILogger<CacheTrimmer> _trimmerLogger;
    private readonly IHostingEnvironment _hostingEnvironment;

    public ImageCacheTrimDebugController(
        IOptions<ImageCacheTrimOptions> options,
        ILogger<CacheTrimmer> trimmerLogger,
        IHostingEnvironment hostingEnvironment)
    {
        _options = options.Value;
        _trimmerLogger = trimmerLogger;
        _hostingEnvironment = hostingEnvironment;
    }

    private ICacheStore BuildStore()
    {
        if (_options.EffectiveMode == CacheMode.Azure)
        {
            return new AzureBlobCacheStore(new AzureBlobCacheStoreOptions
            {
                ConnectionString = _options.ConnectionString,
                ContainerName = _options.ContainerName,
                Prefix = _options.Prefix,
            });
        }

        var path = _options.CacheFolderPath;
        if (!Path.IsPathRooted(path))
        {
            path = _hostingEnvironment.MapPathContentRoot("~/" + path.TrimStart('~', '/'));
        }
        return new PhysicalFileCacheStore(path);
    }

    [HttpGet("list")]
    public async Task<IActionResult> List()
    {
        var store = BuildStore();
        var now = DateTimeOffset.UtcNow;
        var items = new List<object>();

        await foreach (var entry in store.ListAsync())
        {
            items.Add(new
            {
                entry.Name,
                entry.LastModified,
                AgeMinutes = Math.Round((now - entry.LastModified).TotalMinutes, 1),
                entry.SizeBytes,
            });
        }

        return Ok(new { count = items.Count, items });
    }

    [HttpGet("run")]
    public async Task<IActionResult> Run(
        [FromQuery] int maxAgeMinutes = 2,
        [FromQuery] int safetyMinutes = 0)
    {
        var store = BuildStore();
        var trimmer = new CacheTrimmer(store, TimeProvider.System, _trimmerLogger);

        var result = await trimmer.TrimAsync(new TrimSettings
        {
            MaxAge = TimeSpan.FromMinutes(maxAgeMinutes),
            SafetyWindow = TimeSpan.FromMinutes(safetyMinutes),
        });

        return Ok(new
        {
            result.Examined,
            result.Deleted,
            result.DeletedBytes,
            DeletedMb = Math.Round(result.DeletedMegabytes, 2),
            result.PrunedDirectories,
            result.Failed,
        });
    }
}
#endif
