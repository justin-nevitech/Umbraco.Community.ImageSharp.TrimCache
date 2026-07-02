using Azure.Storage.Blobs;
using Umbraco.Cms.Core.DependencyInjection;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

IUmbracoBuilder umbraco = builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    .AddComposers();

// Local testing against Azurite (see docs/LOCAL-TESTING.md, Option B): when an Azure
// blob cache file system is configured — the "Umbraco.Web.UI (Azurite)" launch profile
// sets it via environment variables — route ImageSharp's generated-image cache to Azure
// blob storage so the package's Azure trim path can be exercised end-to-end. Media stays
// on the local disk. With no such config (the default profile) this is skipped and
// ImageSharp uses Umbraco's normal local physical cache.
var azureCacheConnection = builder.Configuration["Umbraco:Storage:AzureBlob:Cache:ConnectionString"];
if (!string.IsNullOrWhiteSpace(azureCacheConnection))
{
    // The named blob file system does NOT auto-create its container (only Umbraco's media
    // file system does that), so create it up front. Otherwise ImageSharp's cache writes
    // fail silently and nothing is cached — neither in Azure nor locally, because
    // AddAzureBlobImageSharpCache replaces the default local physical cache.
    var azureCacheContainer = builder.Configuration["Umbraco:Storage:AzureBlob:Cache:ContainerName"];
    new BlobContainerClient(azureCacheConnection, azureCacheContainer).CreateIfNotExists();

    umbraco
        .AddAzureBlobFileSystem("Cache")
        .AddAzureBlobImageSharpCache("Cache", "cache");
}

umbraco.Build();

WebApplication app = builder.Build();

await app.BootUmbracoAsync();


app.UseUmbraco()
    .WithMiddleware(u =>
    {
        u.UseBackOffice();
        u.UseWebsite();
    })
    .WithEndpoints(u =>
    {
        u.UseBackOfficeEndpoints();
        u.UseWebsiteEndpoints();
    });

await app.RunAsync();
