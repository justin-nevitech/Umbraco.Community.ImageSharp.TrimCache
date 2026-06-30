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

        foreach (var path in Directory.EnumerateFiles(
                     _cacheFolder, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The .meta files are handled with their image; don't surface them
            // as entries in their own right.
            if (path.EndsWith(MetaExtension, StringComparison.OrdinalIgnoreCase))
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

        // The paired .meta is best-effort: once the image is gone the .meta is an
        // unreachable orphan (it is never listed on its own), so a transient lock
        // on it must not fail the whole entry or be reported as a failure.
        DeleteFile(name + MetaExtension, throwOnError: false);

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

        // Recurse into children first so a parent that is emptied only because its
        // children were pruned is itself removed in the same pass.
        foreach (var sub in Directory.EnumerateDirectories(dir))
        {
            cancellationToken.ThrowIfCancellationRequested();
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
