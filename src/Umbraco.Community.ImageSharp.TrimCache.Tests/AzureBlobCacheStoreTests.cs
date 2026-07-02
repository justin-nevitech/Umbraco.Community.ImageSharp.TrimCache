using Azure.Storage.Blobs;
using Umbraco.Community.ImageSharp.TrimCache.Azure;
using Xunit;

namespace Umbraco.Community.ImageSharp.TrimCache.Tests;

/// <summary>
/// Pure unit tests for <see cref="AzureBlobCacheStore"/> construction and argument
/// validation. These need no Azure/Azurite connection — they only exercise the
/// guard clauses, which run before any network call is made. End-to-end listing and
/// deletion are covered by <see cref="AzureBlobCacheStoreIntegrationTests"/>.
/// </summary>
public sealed class AzureBlobCacheStoreTests
{
    [Fact]
    public void Options_ctor_throws_when_options_null()
    {
        Assert.Throws<ArgumentNullException>(
            () => new AzureBlobCacheStore((AzureBlobCacheStoreOptions)null!));
    }

    [Fact]
    public void Options_ctor_throws_when_connection_string_missing()
    {
        var options = new AzureBlobCacheStoreOptions
        {
            ConnectionString = "",
            ContainerName = "cache",
        };

        Assert.Throws<ArgumentException>(() => new AzureBlobCacheStore(options));
    }

    [Fact]
    public void Options_ctor_throws_when_container_name_missing()
    {
        var options = new AzureBlobCacheStoreOptions
        {
            ConnectionString = "UseDevelopmentStorage=true",
            ContainerName = "   ",
        };

        Assert.Throws<ArgumentException>(() => new AzureBlobCacheStore(options));
    }

    [Fact]
    public void Container_ctor_throws_when_client_null()
    {
        Assert.Throws<ArgumentNullException>(
            () => new AzureBlobCacheStore((BlobContainerClient)null!));
    }

    [Fact]
    public void Options_ctor_succeeds_with_valid_settings()
    {
        // A well-formed dev connection string is parsed but not connected to, so
        // construction succeeds without Azurite running.
        var options = new AzureBlobCacheStoreOptions
        {
            ConnectionString = "UseDevelopmentStorage=true",
            ContainerName = "cache",
            Prefix = "cache/",
        };

        var store = new AzureBlobCacheStore(options);

        Assert.NotNull(store);
    }
}
