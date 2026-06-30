namespace Umbraco.Community.ImageSharp.TrimCache.Core;

/// <summary>
/// Which cache backend to trim.
/// </summary>
public enum CacheMode
{
    /// <summary>
    /// Choose automatically: use Azure when blob storage is configured
    /// (connection string + container), otherwise fall back to the local
    /// physical file cache. This is the default.
    /// </summary>
    Auto = 0,

    /// <summary>Force the local physical file cache, even if Azure is configured.</summary>
    Local = 1,

    /// <summary>Force the Azure blob cache (requires connection string + container).</summary>
    Azure = 2,
}
