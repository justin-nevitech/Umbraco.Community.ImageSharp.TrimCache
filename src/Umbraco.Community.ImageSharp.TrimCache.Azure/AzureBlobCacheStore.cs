using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Umbraco.Community.ImageSharp.TrimCache.Core;

namespace Umbraco.Community.ImageSharp.TrimCache.Azure;

/// <summary>
/// Azure Blob storage implementation of <see cref="ICacheStore"/>.
/// This is the ONLY type that references the Azure SDK. It is intentionally
/// thin — it maps blob listings to <see cref="CacheEntry"/> and forwards
/// deletes — so that almost no logic lives here to unit-test. Cover it with
/// an Azurite-backed integration test instead.
/// </summary>
public sealed class AzureBlobCacheStore : ICacheStore
{
    private readonly BlobContainerClient _container;
    private readonly string? _prefix;

    public AzureBlobCacheStore(AzureBlobCacheStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new ArgumentException(
                "ConnectionString is required.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.ContainerName))
        {
            throw new ArgumentException(
                "ContainerName is required.", nameof(options));
        }

        _container = new BlobContainerClient(
            options.ConnectionString, options.ContainerName);
        _prefix = string.IsNullOrWhiteSpace(options.Prefix) ? null : options.Prefix;
    }

    // Constructor for tests / DI where a client is supplied directly
    // (e.g. an Azurite-backed container).
    public AzureBlobCacheStore(BlobContainerClient container, string? prefix = null)
    {
        _container = container ?? throw new ArgumentNullException(nameof(container));
        _prefix = string.IsNullOrWhiteSpace(prefix) ? null : prefix;
    }

    public async IAsyncEnumerable<CacheEntry> ListAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        // If the container doesn't exist yet (e.g. ImageSharp hasn't written its
        // first cached variant), there's nothing to trim. Short-circuit so we return
        // an empty listing instead of letting GetBlobsAsync throw a 404 that would be
        // logged as an error on every run until the container is created.
        var exists = await _container.ExistsAsync(cancellationToken);
        if (!exists.Value)
        {
            yield break;
        }

        // GetBlobsAsync pages transparently and includes LastModified and
        // ContentLength in the listing, so no per-blob properties call is needed.
        await foreach (BlobItem blob in _container
                           .GetBlobsAsync(prefix: _prefix, cancellationToken: cancellationToken))
        {
            var lastModified = blob.Properties.LastModified ?? DateTimeOffset.MinValue;
            var size = blob.Properties.ContentLength ?? 0;
            yield return new CacheEntry(blob.Name, lastModified, size);
        }
    }

    public async Task<bool> DeleteAsync(
        string name, CancellationToken cancellationToken = default)
    {
        var blobClient = _container.GetBlobClient(name);
        var response = await blobClient.DeleteIfExistsAsync(
            DeleteSnapshotsOption.IncludeSnapshots,
            cancellationToken: cancellationToken);
        return response.Value;
    }
}
