namespace Umbraco.Community.ImageSharp.TrimCache.Core;

/// <summary>
/// Optional capability for backends that have a real nested-folder structure: after
/// the trim has deleted entries, remove any sub-folders left behind empty.
///
/// Only the local physical cache implements this — ImageSharp's
/// <c>PhysicalFileSystemCache</c> shards variants into one-character subfolders
/// (e.g. <c>.../7/f/2/f/d/8/1/6/</c>), and deleting the variant files leaves those
/// folders behind empty. The Azure blob backend has no real directories (its
/// "folders" are virtual key prefixes that disappear when the last blob is removed),
/// so it does not implement this and the trimmer simply skips the prune step.
/// </summary>
public interface IPrunableCacheStore
{
    /// <summary>
    /// Removes empty sub-folders beneath the cache root, bottom-up, so a folder that
    /// becomes empty only because its children were removed is also pruned. The cache
    /// root itself is never removed, even when empty. Returns the number of folders
    /// removed. Best-effort: a folder that can't be removed (locked, in use, no
    /// access) is left in place and does not fail the run.
    /// </summary>
    Task<int> PruneEmptyDirectoriesAsync(CancellationToken cancellationToken = default);
}
