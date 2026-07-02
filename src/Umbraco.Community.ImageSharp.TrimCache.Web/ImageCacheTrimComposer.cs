using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace Umbraco.Community.ImageSharp.TrimCache.Web;

/// <summary>Registers the hosted service and binds options. Auto-discovered by Umbraco.</summary>
public sealed class ImageCacheTrimComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services
            .AddOptions<ImageCacheTrimOptions>()
            .Bind(builder.Config.GetSection(ImageCacheTrimOptions.SectionName));

        // Bind once at registration so we only start the background service when
        // it's enabled and runnable. Local mode is runnable by default; Azure mode
        // requires credentials. A disabled or (Azure-forced-but-unconfigured) site
        // never starts the service.
        var options = builder.Config
            .GetSection(ImageCacheTrimOptions.SectionName)
            .Get<ImageCacheTrimOptions>() ?? new ImageCacheTrimOptions();

        if (options.Enabled && options.CanRun)
        {
            builder.Services.AddHostedService<ImageCacheTrimHostedService>();
        }
    }
}
