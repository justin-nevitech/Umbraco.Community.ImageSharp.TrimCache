using Umbraco.Community.ImageSharp.TrimCache.Core;
using Xunit;

namespace Umbraco.Community.ImageSharp.TrimCache.Tests;

public sealed class TrimSettingsTests
{
    [Fact]
    public void Defaults_are_30_day_max_age_and_5_minute_safety_window()
    {
        var settings = new TrimSettings();

        Assert.Equal(TimeSpan.FromDays(30), settings.MaxAge);
        Assert.Equal(TimeSpan.FromMinutes(5), settings.SafetyWindow);
    }
}
