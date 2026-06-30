namespace Umbraco.Community.ImageSharp.TrimCache;

/// <summary>
/// Marker type for the Umbraco.Community.ImageSharp.TrimCache package.
///
/// This is the main package consumers install. It brings in the Core, Azure and
/// Web assemblies. The actual wiring happens automatically via the composer in
/// the Web assembly (<c>Umbraco.Community.ImageSharp.TrimCache.Web</c>), which
/// Umbraco discovers on startup — no manual registration is required.
///
/// Configure via the "ImageCacheTrim" section in appsettings. See the README.
/// </summary>
public static class TrimCachePackage
{
    /// <summary>The package identifier.</summary>
    public const string PackageId = "Umbraco.Community.ImageSharp.TrimCache";
}
