using Umbraco.Community.ImageSharp.TrimCache.Core;

namespace Umbraco.Community.ImageSharp.TrimCache.Tests;

/// <summary>
/// In-memory <see cref="ICacheStore"/> for unit tests. No Azure, no I/O.
/// Lets tests seed entries at precise ages and assert exactly what was deleted.
/// Can optionally be told to throw on a specific entry to exercise the
/// failure-tally path.
/// </summary>
public sealed class FakeCacheStore : ICacheStore
{
    private readonly Dictionary<string, CacheEntry> _entries;

    /// <summary>Names that should throw on delete, to simulate storage errors.</summary>
    public HashSet<string> ThrowOnDelete { get; } = new();

    /// <summary>
    /// Names that should throw <see cref="OperationCanceledException"/> on delete,
    /// to simulate shutdown mid-delete (which the trimmer must re-throw, not tally).
    /// </summary>
    public HashSet<string> CancelOnDelete { get; } = new();

    /// <summary>
    /// Names whose delete should report "nothing deleted" (return false), to
    /// simulate an entry that vanished between listing and deletion.
    /// </summary>
    public HashSet<string> ReturnFalseOnDelete { get; } = new();

    /// <summary>Records the order in which deletes were attempted.</summary>
    public List<string> DeleteAttempts { get; } = new();

    public FakeCacheStore(IEnumerable<CacheEntry> entries)
    {
        _entries = entries.ToDictionary(e => e.Name, e => e);
    }

    public IReadOnlyDictionary<string, CacheEntry> Remaining => _entries;

    public async IAsyncEnumerable<CacheEntry> ListAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        // Snapshot so deletes during enumeration don't mutate the iterator.
        foreach (var entry in _entries.Values.ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return entry;
            await Task.Yield();
        }
    }

    public Task<bool> DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        DeleteAttempts.Add(name);

        if (CancelOnDelete.Contains(name))
        {
            throw new OperationCanceledException($"Simulated cancellation deleting '{name}'.");
        }

        if (ThrowOnDelete.Contains(name))
        {
            throw new InvalidOperationException($"Simulated storage failure for '{name}'.");
        }

        if (ReturnFalseOnDelete.Contains(name))
        {
            // Pretend it was already gone: do not remove, report nothing deleted.
            return Task.FromResult(false);
        }

        var removed = _entries.Remove(name);
        return Task.FromResult(removed);
    }
}
