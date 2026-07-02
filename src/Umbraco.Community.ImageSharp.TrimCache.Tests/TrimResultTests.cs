using Umbraco.Community.ImageSharp.TrimCache.Core;
using Xunit;

namespace Umbraco.Community.ImageSharp.TrimCache.Tests;

public sealed class TrimResultTests
{
    [Fact]
    public void DeletedMegabytes_converts_bytes_to_mib()
    {
        var result = new TrimResult(Examined: 10, Deleted: 1, DeletedBytes: 5 * 1024 * 1024, Failed: 0);
        Assert.Equal(5d, result.DeletedMegabytes, precision: 6);
    }

    [Fact]
    public void DeletedMegabytes_is_zero_for_no_bytes()
    {
        Assert.Equal(0d, new TrimResult(0, 0, 0, 0).DeletedMegabytes);
    }

    [Fact]
    public void Empty_is_all_zero()
    {
        Assert.Equal(0, TrimResult.Empty.Examined);
        Assert.Equal(0, TrimResult.Empty.Deleted);
        Assert.Equal(0, TrimResult.Empty.DeletedBytes);
        Assert.Equal(0, TrimResult.Empty.Failed);
        Assert.Equal(0, TrimResult.Empty.PrunedDirectories);
    }

    [Fact]
    public void PrunedDirectories_defaults_to_zero_when_not_specified()
    {
        Assert.Equal(0, new TrimResult(1, 1, 100, 0).PrunedDirectories);
    }
}
