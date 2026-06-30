namespace Umbraco.Community.ImageSharp.TrimCache.Core;

/// <summary>
/// Summary of a completed trim run, returned to the caller for logging.
/// </summary>
public sealed record TrimResult(
    long Examined,
    long Deleted,
    long DeletedBytes,
    long Failed,
    int PrunedDirectories = 0)
{
    public double DeletedMegabytes => DeletedBytes / 1024d / 1024d;

    public static TrimResult Empty { get; } = new(0, 0, 0, 0);
}
