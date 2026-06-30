using Umbraco.Community.ImageSharp.TrimCache.Core;
using Xunit;

namespace Umbraco.Community.ImageSharp.TrimCache.Tests;

/// <summary>
/// Tests for the pure schedule-resolution logic. No Umbraco reference required,
/// so these run cleanly on every target framework. Interval is in minutes,
/// measured from startup.
/// </summary>
public sealed class ScheduleResolverTests
{
    [Theory]
    [InlineData(1440, 1440)]   // default: 24h
    [InlineData(1, 1)]
    [InlineData(30, 30)]
    [InlineData(60, 60)]
    public void Interval_is_respected_when_valid(int minutes, int expected)
    {
        Assert.Equal(TimeSpan.FromMinutes(expected), ScheduleResolver.ResolveInterval(minutes));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Interval_is_clamped_to_a_minimum_of_one_minute(int minutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(1), ScheduleResolver.ResolveInterval(minutes));
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(0, 0)]
    [InlineData(30, 30)]
    public void Startup_delay_is_respected_when_valid(int minutes, int expected)
    {
        Assert.Equal(TimeSpan.FromMinutes(expected), ScheduleResolver.ResolveStartupDelay(minutes));
    }

    [Fact]
    public void Startup_delay_is_clamped_to_zero_when_negative()
    {
        Assert.Equal(TimeSpan.Zero, ScheduleResolver.ResolveStartupDelay(-10));
    }

    [Fact]
    public void Minimum_interval_is_one_minute()
    {
        Assert.Equal(TimeSpan.FromMinutes(1), ScheduleResolver.MinimumInterval);
    }
}
