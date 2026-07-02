namespace Umbraco.Community.ImageSharp.TrimCache.Azure;

/// <summary>
/// Connection details for the Azure Blob cache container.
/// Must point at the SAME account/container that ImageSharp's Azure blob image
/// cache writes to — never the source media container.
/// </summary>
public sealed class AzureBlobCacheStoreOptions
{
    /// <summary>
    /// Azure Blob connection string for the account holding the ImageSharp cache. May be
    /// an account connection string or one carrying a SAS
    /// (<c>BlobEndpoint=...;SharedAccessSignature=...</c>) — the SAS form is how Umbraco
    /// Cloud exposes its storage. The credential must allow <b>List</b> and <b>Delete</b>.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Container the ImageSharp Azure blob cache writes to.</summary>
    public string ContainerName { get; set; } = string.Empty;

    /// <summary>
    /// Optional blob name prefix to scope the scan (e.g. "cache/"). REQUIRED when the
    /// cache shares a container with other data (e.g. source media) — everything under
    /// the prefix that is older than the max age is deleted, so an empty prefix on a
    /// shared container would delete media too. Leave empty only for a cache-only container.
    /// </summary>
    public string Prefix { get; set; } = string.Empty;
}
