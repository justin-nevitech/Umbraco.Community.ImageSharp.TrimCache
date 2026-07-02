using Azure;
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
        // Enumerate manually so a missing container (404) can be treated as "nothing
        // to trim" WITHOUT a separate container-level existence check. That check
        // (ExistsAsync -> Get Container Properties) needs container read/metadata
        // permission, which restricted SAS credentials — e.g. Umbraco Cloud media
        // storage — may not grant, causing a 403. GetBlobsAsync only needs List, which
        // the trim already requires. GetBlobsAsync pages transparently and includes
        // LastModified and ContentLength, so no per-blob properties call is needed.
        await using var enumerator = _container
            .GetBlobsAsync(prefix: _prefix, cancellationToken: cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        while (true)
        {
            bool moved;
            try
            {
                moved = await enumerator.MoveNextAsync();
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // Container doesn't exist yet (e.g. before the first cached variant is
                // written) — treat as end of listing; nothing to trim.
                moved = false;
            }

            if (!moved)
            {
                break;
            }

            var blob = enumerator.Current;
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
