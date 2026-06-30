using Umbraco.Community.ImageSharp.TrimCache.Core;
using Umbraco.Community.ImageSharp.TrimCache.Web;
using Xunit;

namespace Umbraco.Community.ImageSharp.TrimCache.Tests;

/// <summary>
/// Tests for the host-facing options logic in <see cref="ImageCacheTrimOptions"/>:
/// Azure-configured detection, Auto-mode resolution, the can-run gate, and the
/// schedule resolution helpers. These are pure property evaluations, so they run
/// on every target framework without booting Umbraco.
/// </summary>
public sealed class ImageCacheTrimOptionsTests
{
    private static ImageCacheTrimOptions WithAzure() => new()
    {
        ConnectionString = "UseDevelopmentStorage=true",
        ContainerName = "cache",
    };

    [Fact]
    public void Section_name_is_stable()
    {
        Assert.Equal("ImageCacheTrim", ImageCacheTrimOptions.SectionName);
    }

    [Fact]
    public void Defaults_are_sensible()
    {
        var options = new ImageCacheTrimOptions();

        Assert.True(options.Enabled);
        Assert.Equal(CacheMode.Auto, options.Mode);
        Assert.Equal(30, options.MaxAgeDays);
        Assert.Equal(1440, options.IntervalMinutes);
        Assert.Equal(5, options.StartupDelayMinutes);
        Assert.Equal("umbraco/Data/TEMP/MediaCache", options.CacheFolderPath);
    }

    [Fact]
    public void IsAzureConfigured_is_false_without_both_connection_and_container()
    {
        Assert.False(new ImageCacheTrimOptions().IsAzureConfigured);
        Assert.False(new ImageCacheTrimOptions { ConnectionString = "x" }.IsAzureConfigured);
        Assert.False(new ImageCacheTrimOptions { ContainerName = "x" }.IsAzureConfigured);
    }

    [Fact]
    public void IsAzureConfigured_is_true_with_both()
    {
        Assert.True(WithAzure().IsAzureConfigured);
    }

    [Fact]
    public void Auto_mode_resolves_to_local_when_azure_not_configured()
    {
        var options = new ImageCacheTrimOptions { Mode = CacheMode.Auto };
        Assert.Equal(CacheMode.Local, options.EffectiveMode);
    }

    [Fact]
    public void Auto_mode_resolves_to_azure_when_azure_configured()
    {
        var options = WithAzure();
        options.Mode = CacheMode.Auto;
        Assert.Equal(CacheMode.Azure, options.EffectiveMode);
    }

    [Fact]
    public void Forced_local_mode_stays_local_even_when_azure_configured()
    {
        var options = WithAzure();
        options.Mode = CacheMode.Local;
        Assert.Equal(CacheMode.Local, options.EffectiveMode);
    }

    [Fact]
    public void Forced_azure_mode_stays_azure_even_when_unconfigured()
    {
        var options = new ImageCacheTrimOptions { Mode = CacheMode.Azure };
        Assert.Equal(CacheMode.Azure, options.EffectiveMode);
    }

    [Fact]
    public void CanRun_is_true_for_local_mode_without_any_azure_config()
    {
        var options = new ImageCacheTrimOptions { Mode = CacheMode.Local };
        Assert.True(options.CanRun);
    }

    [Fact]
    public void CanRun_is_true_in_auto_mode_on_a_plain_self_hosted_site()
    {
        // No Azure config -> Auto resolves to Local -> always runnable.
        Assert.True(new ImageCacheTrimOptions().CanRun);
    }

    [Fact]
    public void CanRun_is_false_when_azure_forced_but_not_configured()
    {
        var options = new ImageCacheTrimOptions { Mode = CacheMode.Azure };
        Assert.False(options.CanRun);
    }

    [Fact]
    public void CanRun_is_true_when_azure_forced_and_configured()
    {
        var options = WithAzure();
        options.Mode = CacheMode.Azure;
        Assert.True(options.CanRun);
    }

    [Theory]
    [InlineData(1440, 1440)]
    [InlineData(0, 1)]      // clamped up to the 1-minute floor
    [InlineData(-99, 1)]
    public void ResolveInterval_clamps_to_minimum(int configured, int expectedMinutes)
    {
        var options = new ImageCacheTrimOptions { IntervalMinutes = configured };
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), options.ResolveInterval());
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(0, 0)]
    [InlineData(-10, 0)]    // clamped up to zero
    public void ResolveStartupDelay_clamps_to_zero(int configured, int expectedMinutes)
    {
        var options = new ImageCacheTrimOptions { StartupDelayMinutes = configured };
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), options.ResolveStartupDelay());
    }

    [Fact]
    public void RunsOnEveryServer_defaults_true_for_local_mode()
    {
        // Local caches are per-server, so every server must trim its own.
        Assert.True(new ImageCacheTrimOptions { Mode = CacheMode.Local }.RunsOnEveryServer);
        // Auto with no Azure config resolves to Local -> every server.
        Assert.True(new ImageCacheTrimOptions().RunsOnEveryServer);
    }

    [Fact]
    public void RunsOnEveryServer_defaults_false_for_azure_mode()
    {
        // Azure blob cache is shared, so only the scheduling/single server trims.
        var auto = WithAzure();
        auto.Mode = CacheMode.Auto; // resolves to Azure
        Assert.False(auto.RunsOnEveryServer);

        var forced = WithAzure();
        forced.Mode = CacheMode.Azure;
        Assert.False(forced.RunsOnEveryServer);
    }

    [Fact]
    public void RunsOnEveryServer_explicit_setting_overrides_the_mode_default()
    {
        // Azure (shared) but forced to run on every server.
        var azure = WithAzure();
        azure.RunOnEveryServer = true;
        Assert.True(azure.RunsOnEveryServer);

        // Local (per-server) but forced onto a single server (e.g. a shared path).
        var local = new ImageCacheTrimOptions
        {
            Mode = CacheMode.Local,
            RunOnEveryServer = false,
        };
        Assert.False(local.RunsOnEveryServer);
    }
}
