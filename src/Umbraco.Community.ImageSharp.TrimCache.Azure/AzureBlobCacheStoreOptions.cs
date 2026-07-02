namespace Umbraco.Community.ImageSharp.TrimCache.Azure;

/// <summary>
/// Connection details for the Azure Blob cache container.
/// Must point at the SAME account/container that ImageSharp's Azure blob image
/// cache writes to — never the source media container.
/// </summary>
public sealed class AzureBlobCacheStoreOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Container name configured for the ImageSharp Azure blob image cache.</summary>
    public string ContainerName { get; set; } = string.Empty;

    /// <summary>
    /// Optional blob name prefix to scope the scan (e.g. "cache/") when the
    /// cache shares a container with other data. Leave empty to scan all.
    /// </summary>
    public string Prefix { get; set; } = string.Empty;
}
