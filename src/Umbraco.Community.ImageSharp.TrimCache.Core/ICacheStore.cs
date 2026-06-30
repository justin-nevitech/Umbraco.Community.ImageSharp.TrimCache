namespace Umbraco.Community.ImageSharp.TrimCache.Core;

/// <summary>
/// The only surface the trimmer needs from a cache backend: enumerate the cached
/// entries and delete one by name. Implemented against Azure Blob storage and
/// against the local physical file cache, plus an in-memory fake for testing.
/// </summary>
public interface ICacheStore
{
    /// <summary>
    /// Streams the cache entries. Implementations should page/enumerate
    /// transparently so the caller sees every entry.
    /// </summary>
    IAsyncEnumerable<CacheEntry> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a single entry by name (including any paired metadata the backend
    /// stores for it). Returns true if an entry was deleted, false if it did not
    /// exist. Implementations should throw only on genuine errors, which the
    /// trimmer will tally and continue past.
    /// </summary>
    Task<bool> DeleteAsync(string name, CancellationToken cancellationToken = default);
}
