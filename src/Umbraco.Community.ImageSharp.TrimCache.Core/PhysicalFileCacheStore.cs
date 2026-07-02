namespace Umbraco.Community.ImageSharp.TrimCache.Core;

/// <summary>
/// Local physical-file implementation of <see cref="ICacheStore"/> for the
/// ImageSharp PhysicalFileSystemCache.
///
/// Unlike the Azure blob cache (one blob per variant, metadata held as blob
/// metadata), the physical cache writes TWO files per variant: the image and a
/// paired ".meta" file. This store enumerates the image files only, and on delete
/// removes the paired ".meta" alongside so no orphaned metadata is left behind.
///
/// Uses only System.IO, so it lives in Core with no extra dependency.
/// </summary>
public sealed class PhysicalFileCacheStore : ICacheStore, IPrunableCacheStore
{
    private const string MetaExtension = ".meta";

    // Recurse the whole tree, but never follow directory junctions / symlinks
    // (AttributesToSkip = ReparsePoint) so the walk can't escape the cache folder or
    // loop. AttributesToSkip is set explicitly to ReparsePoint only — the default
    // also skips Hidden/System, which we want to keep enumerating. IgnoreInaccessible
    // lets a locked/denied entry be skipped rather than throwing out of the walk.
    private static readonly EnumerationOptions WalkOptions = new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = true,
    };

    private readonly string _cacheFolder;

    public PhysicalFileCacheStore(string cacheFolder)
    {
        if (string.IsNullOrWhiteSpace(cacheFolder))
        {
            throw new ArgumentException("Cache folder path is required.", nameof(cacheFolder));
        }

        _cacheFolder = cacheFolder;
    }

    public async IAsyncEnumerable<CacheEntry> ListAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_cacheFolder))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(_cacheFolder, "*", WalkOptions))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // ImageSharp writes a variant as two files that share a base name but have
            // different extensions: "<base>.<imageext>" and "<base>.meta". A .meta is
            // represented by — and deleted with — its image, so skip it while that image
            // is present. If the image is gone (an orphaned .meta, e.g. a prior run
            // deleted the image but a transient lock left the .meta behind), surface the
            // .meta itself so an age-based trim can still reclaim it.
            if (path.EndsWith(MetaExtension, StringComparison.OrdinalIgnoreCase)
                && HasSiblingImage(path))
            {
                continue;
            }

            CacheEntry? entry = null;
            try
            {
                var info = new FileInfo(path);
                if (info.Exists)
                {
                    // Name is the full path so DeleteAsync can act on it directly.
                    entry = new CacheEntry(
                        path,
                        new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
                        info.Length);
                }
            }
            catch (IOException)
            {
                // Could not stat (locked / vanished) — skip this one.
            }
            catch (UnauthorizedAccessException)
            {
                // No access — skip.
            }

            if (entry is not null)
            {
                yield return entry;
            }

            await Task.Yield();
        }
    }

    public Task<bool> DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Delete the image first. A lock here (e.g. an in-flight read) is surfaced
        // to the trimmer, which tallies it as a failure; the next run retries it.
        var deletedImage = DeleteFile(name, throwOnError: true);

        // The paired .meta shares the image's base name with a ".meta" extension
        // (ImageSharp writes "<base>.<ext>" + "<base>.meta"), so swap the extension —
        // do NOT append ".meta" to the image path. Best-effort: once the image is gone
        // the .meta is effectively an orphan, so a transient lock on it must not fail
        // the whole entry or be reported as a failure.
        DeleteFile(Path.ChangeExtension(name, MetaExtension), throwOnError: false);

        return Task.FromResult(deletedImage);
    }

    /// <summary>
    /// Removes empty sub-folders beneath the cache root, bottom-up. Runs after the
    /// trimmer has finished enumerating and deleting, so it never removes a folder
    /// out from under an in-flight directory walk.
    /// </summary>
    public Task<int> PruneEmptyDirectoriesAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_cacheFolder))
        {
            return Task.FromResult(0);
        }

        // The root is passed as isRoot so it is never removed, even if empty.
        var removed = PruneEmptyDirectories(_cacheFolder, isRoot: true, cancellationToken);
        return Task.FromResult(removed);
    }

    private static int PruneEmptyDirectories(string dir, bool isRoot, CancellationToken cancellationToken)
    {
        var removed = 0;

        // Snapshot the child directories up front (GetDirectories, not the lazy
        // EnumerateDirectories) so removing a child below doesn't mutate a live
        // enumerator we're still iterating. Guarded so a directory that vanishes
        // mid-walk (e.g. a concurrent trim, or ImageSharp churning the tree) leaves
        // this branch alone instead of throwing out of the whole prune.
        string[] children;
        try
        {
            children = Directory.GetDirectories(dir);
        }
        catch (IOException)
        {
            // DirectoryNotFoundException (vanished) is an IOException — nothing to do.
            return removed;
        }
        catch (UnauthorizedAccessException)
        {
            return removed;
        }

        // Recurse into children first so a parent that is emptied only because its
        // children were pruned is itself removed in the same pass.
        foreach (var sub in children)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Don't descend into (or remove) a junction/symlink — pruning through one
            // could delete entries in its target, outside the cache folder.
            if (IsReparsePoint(sub))
            {
                continue;
            }

            removed += PruneEmptyDirectories(sub, isRoot: false, cancellationToken);
        }

        if (isRoot)
        {
            return removed;
        }

        try
        {
            // Empty means no files AND no remaining subdirectories.
            if (!Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Directory.Delete(dir, recursive: false);
                removed++;
            }
        }
        catch (IOException)
        {
            // Locked / a file was written into it concurrently / vanished — leave it.
        }
        catch (UnauthorizedAccessException)
        {
            // No access — leave it.
        }

        return removed;
    }

    // True when a sibling image file (same base name, any non-".meta" extension) sits
    // next to this .meta — i.e. the .meta is a normal pair member, not an orphan. Uses
    // an OS-filtered single-directory glob, so it returns the ~1 match cheaply even in
    // a populated shard folder.
    private static bool HasSiblingImage(string metaPath)
    {
        var dir = Path.GetDirectoryName(metaPath);
        if (string.IsNullOrEmpty(dir))
        {
            return false;
        }

        var baseName = Path.GetFileNameWithoutExtension(metaPath);
        try
        {
            foreach (var sibling in Directory.EnumerateFiles(dir, baseName + ".*"))
            {
                if (!sibling.EndsWith(MetaExtension, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (IOException)
        {
            // Directory vanished mid-walk — treat as orphan; delete/trim will handle it.
        }
        catch (UnauthorizedAccessException)
        {
        }

        return false;
    }

    private static bool IsReparsePoint(string dir)
    {
        try
        {
            return (File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0;
        }
        catch (IOException)
        {
            // Vanished mid-walk (DirectoryNotFoundException is an IOException) — the
            // recursion's own guards will handle it; treat as not-a-reparse-point.
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool DeleteFile(string path, bool throwOnError)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                return true;
            }
            return false;
        }
        catch (Exception ex) when (!throwOnError && ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort delete (the paired .meta): swallow transient errors.
            return false;
        }
        // For the image (throwOnError: true) IOException/UnauthorizedAccessException
        // propagate, so the trimmer tallies the failure and retries next run.
    }
}
