using Azure.Storage.Blobs;
using Umbraco.Community.ImageSharp.TrimCache.Azure;
using Umbraco.Community.ImageSharp.TrimCache.Core;
using Xunit;

namespace Umbraco.Community.ImageSharp.TrimCache.Tests;

/// <summary>
/// Integration tests for the REAL Azure store, run against Azurite (the local
/// Azure Storage emulator) — no cloud account, no cost. These verify the thin
/// Azure wrapper that the in-memory unit tests cannot cover: the GetBlobsAsync
/// listing/mapping and DeleteIfExistsAsync behaviour.
///
/// Skipped automatically unless an Azurite connection string is present, so the
/// suite stays green on machines without Azurite running.
///
/// To run locally:
///   1. Start Azurite:  docker run -p 10000:10000 mcr.microsoft.com/azure-storage/azurite azurite-blob --blobHost 0.0.0.0 --skipApiVersionCheck
///      (or `azurite-blob` via the npm tool / VS Code extension)
///   2. Set env var AZURITE_CONNECTION to the well-known dev connection string:
///      "UseDevelopmentStorage=true"
///
/// See docs/LOCAL-TESTING.md for the full manual + code-level testing guide.
/// </summary>
public sealed class AzureBlobCacheStoreIntegrationTests
{
    private const string ConnEnvVar = "AZURITE_CONNECTION";

    private static string? Conn =>
        Environment.GetEnvironmentVariable(ConnEnvVar);

    private static bool AzuriteAvailable => !string.IsNullOrWhiteSpace(Conn);

    [SkippableFact]
    public async Task Lists_and_deletes_real_blobs()
    {
        Skip.IfNot(AzuriteAvailable,
            $"Azurite not configured ({ConnEnvVar} unset); skipping integration test.");

        var containerName = "test-cache-" + Guid.NewGuid().ToString("N");
        var container = new BlobContainerClient(Conn, containerName);
        await container.CreateIfNotExistsAsync();

        try
        {
            // Seed two blobs.
            await container.UploadBlobAsync("cache/aaa",
                BinaryData.FromString("image-bytes-1"));
            await container.UploadBlobAsync("cache/bbb",
                BinaryData.FromString("image-bytes-2"));

            var store = new AzureBlobCacheStore(container, prefix: "cache/");

            // Listing should surface both, with real LastModified values.
            var listed = new List<CacheEntry>();
            await foreach (var entry in store.ListAsync())
            {
                listed.Add(entry);
            }

            Assert.Equal(2, listed.Count);
            Assert.All(listed, e => Assert.True(e.LastModified > DateTimeOffset.MinValue));
            Assert.All(listed, e => Assert.True(e.SizeBytes > 0));

            // Deleting one should return true; deleting a missing one should return false.
            Assert.True(await store.DeleteAsync("cache/aaa"));
            Assert.False(await store.DeleteAsync("cache/does-not-exist"));

            // Only one remains now.
            var remaining = new List<CacheEntry>();
            await foreach (var entry in store.ListAsync())
            {
                remaining.Add(entry);
            }
            Assert.Single(remaining);
            Assert.Equal("cache/bbb", remaining[0].Name);
        }
        finally
        {
            await container.DeleteIfExistsAsync();
        }
    }

    [SkippableFact]
    public async Task End_to_end_trim_against_azurite()
    {
        Skip.IfNot(AzuriteAvailable,
            $"Azurite not configured ({ConnEnvVar} unset); skipping integration test.");

        var containerName = "test-cache-" + Guid.NewGuid().ToString("N");
        var container = new BlobContainerClient(Conn, containerName);
        await container.CreateIfNotExistsAsync();

        try
        {
            await container.UploadBlobAsync("cache/keep",
                BinaryData.FromString("fresh"));
            await container.UploadBlobAsync("cache/drop",
                BinaryData.FromString("stale"));

            // Note: Azurite stamps LastModified at upload time, so we can't make a
            // blob genuinely "old" here. Instead we trim with a NEGATIVE-equivalent
            // setup: MaxAge of zero and a zero safety window deletes everything,
            // proving the wired-up path end-to-end. Age-boundary correctness is
            // covered exhaustively by the in-memory unit tests.
            var store = new AzureBlobCacheStore(container, prefix: "cache/");
            var trimmer = new CacheTrimmer(store);

            var result = await trimmer.TrimAsync(new TrimSettings
            {
                MaxAge = TimeSpan.Zero,
                SafetyWindow = TimeSpan.Zero,
            });

            Assert.Equal(2, result.Examined);
            Assert.Equal(2, result.Deleted);
        }
        finally
        {
            await container.DeleteIfExistsAsync();
        }
    }
}
