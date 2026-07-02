using Microsoft.Extensions.Time.Testing;
using Umbraco.Community.ImageSharp.TrimCache.Core;
using Xunit;

namespace Umbraco.Community.ImageSharp.TrimCache.Tests;

public sealed class CacheTrimmerTests
{
    // A fixed "now" so age maths is deterministic.
    private static readonly DateTimeOffset Now =
        new(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);

    private static FakeTimeProvider ClockAtNow() => new(Now);

    private static CacheEntry EntryAged(string name, TimeSpan age, long size = 1024) =>
        new(name, Now - age, size);

    private static TrimSettings Settings(
        int maxAgeDays = 30, int safetyMinutes = 5) => new()
    {
        MaxAge = TimeSpan.FromDays(maxAgeDays),
        SafetyWindow = TimeSpan.FromMinutes(safetyMinutes),
    };

    [Fact]
    public async Task Deletes_entries_older_than_max_age()
    {
        var store = new FakeCacheStore(new[]
        {
            EntryAged("old", TimeSpan.FromDays(45)),
        });
        var trimmer = new CacheTrimmer(store, ClockAtNow());

        var result = await trimmer.TrimAsync(Settings());

        Assert.Equal(1, result.Deleted);
        Assert.False(store.Remaining.ContainsKey("old"));
    }

    [Fact]
    public async Task Keeps_entries_newer_than_max_age()
    {
        var store = new FakeCacheStore(new[]
        {
            EntryAged("fresh", TimeSpan.FromDays(10)),
        });
        var trimmer = new CacheTrimmer(store, ClockAtNow());

        var result = await trimmer.TrimAsync(Settings());

        Assert.Equal(0, result.Deleted);
        Assert.True(store.Remaining.ContainsKey("fresh"));
    }

    [Fact]
    public async Task Entry_exactly_at_cutoff_is_kept()
    {
        // LastModified == cutoff should NOT be deleted (>= cutoff is kept).
        var store = new FakeCacheStore(new[]
        {
            EntryAged("boundary", TimeSpan.FromDays(30)),
        });
        var trimmer = new CacheTrimmer(store, ClockAtNow());

        var result = await trimmer.TrimAsync(Settings(maxAgeDays: 30));

        Assert.Equal(0, result.Deleted);
        Assert.True(store.Remaining.ContainsKey("boundary"));
    }

    [Fact]
    public async Task Entry_inside_safety_window_is_never_deleted()
    {
        // Old enough by age, but modified within the safety window — must survive.
        // (Constructed by making it "old" yet within the last 5 minutes is
        // impossible simultaneously, so we test the window directly: an entry
        // aged 2 minutes with a zero max-age is past cutoff but inside the window.)
        var store = new FakeCacheStore(new[]
        {
            EntryAged("inflight", TimeSpan.FromMinutes(2)),
        });
        var trimmer = new CacheTrimmer(store, ClockAtNow());

        var result = await trimmer.TrimAsync(Settings(maxAgeDays: 0, safetyMinutes: 5));

        Assert.Equal(0, result.Deleted);
        Assert.True(store.Remaining.ContainsKey("inflight"));
    }

    [Fact]
    public async Task Mixed_set_deletes_only_the_stale_ones()
    {
        var store = new FakeCacheStore(new[]
        {
            EntryAged("dec",  TimeSpan.FromDays(200), 500),
            EntryAged("jan",  TimeSpan.FromDays(150), 500),
            EntryAged("recent", TimeSpan.FromDays(3), 500),
            EntryAged("today", TimeSpan.FromHours(2), 500),
        });
        var trimmer = new CacheTrimmer(store, ClockAtNow());

        var result = await trimmer.TrimAsync(Settings());

        Assert.Equal(4, result.Examined);
        Assert.Equal(2, result.Deleted);
        Assert.Equal(1000, result.DeletedBytes);
        Assert.True(store.Remaining.ContainsKey("recent"));
        Assert.True(store.Remaining.ContainsKey("today"));
        Assert.False(store.Remaining.ContainsKey("dec"));
        Assert.False(store.Remaining.ContainsKey("jan"));
    }

    [Fact]
    public async Task Delete_failure_is_tallied_and_run_continues()
    {
        var store = new FakeCacheStore(new[]
        {
            EntryAged("bad",  TimeSpan.FromDays(60), 100),
            EntryAged("good", TimeSpan.FromDays(60), 100),
        });
        store.ThrowOnDelete.Add("bad");
        var trimmer = new CacheTrimmer(store, ClockAtNow());

        var result = await trimmer.TrimAsync(Settings());

        Assert.Equal(1, result.Failed);
        Assert.Equal(1, result.Deleted);
        // The failure did not stop the run reaching "good".
        Assert.False(store.Remaining.ContainsKey("good"));
        Assert.True(store.Remaining.ContainsKey("bad"));
    }

    [Fact]
    public async Task Empty_store_returns_empty_result()
    {
        var store = new FakeCacheStore(Array.Empty<CacheEntry>());
        var trimmer = new CacheTrimmer(store, ClockAtNow());

        var result = await trimmer.TrimAsync(Settings());

        Assert.Equal(0, result.Examined);
        Assert.Equal(0, result.Deleted);
    }

    [Fact]
    public async Task Only_eligible_entries_are_attempted_for_deletion()
    {
        var store = new FakeCacheStore(new[]
        {
            EntryAged("old1", TimeSpan.FromDays(60)),
            EntryAged("fresh", TimeSpan.FromDays(1)),
            EntryAged("old2", TimeSpan.FromDays(90)),
        });
        var trimmer = new CacheTrimmer(store, ClockAtNow());

        await trimmer.TrimAsync(Settings());

        // The fresh entry must never have a delete attempted against it.
        Assert.DoesNotContain("fresh", store.DeleteAttempts);
        Assert.Contains("old1", store.DeleteAttempts);
        Assert.Contains("old2", store.DeleteAttempts);
    }

    [Fact]
    public async Task Vanished_entry_is_counted_as_neither_deleted_nor_failed()
    {
        // Eligible by age, but the delete reports nothing removed (already gone).
        var store = new FakeCacheStore(new[]
        {
            EntryAged("ghost", TimeSpan.FromDays(60), 1000),
        });
        store.ReturnFalseOnDelete.Add("ghost");
        var trimmer = new CacheTrimmer(store, ClockAtNow());

        var result = await trimmer.TrimAsync(Settings());

        Assert.Equal(1, result.Examined);
        Assert.Equal(0, result.Deleted);
        Assert.Equal(0, result.Failed);
        Assert.Equal(0, result.DeletedBytes);
        Assert.Contains("ghost", store.DeleteAttempts);
    }

    [Fact]
    public async Task Cancellation_during_delete_propagates_and_is_not_tallied()
    {
        var store = new FakeCacheStore(new[]
        {
            EntryAged("a", TimeSpan.FromDays(60)),
        });
        store.CancelOnDelete.Add("a");
        var trimmer = new CacheTrimmer(store, ClockAtNow());

        // An OperationCanceledException from the store (shutdown mid-delete) must
        // surface as a clean cancellation, not be swallowed and tallied as a failure.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => trimmer.TrimAsync(Settings()));
    }

    [Fact]
    public async Task Cancellation_stops_the_run()
    {
        var store = new FakeCacheStore(new[]
        {
            EntryAged("a", TimeSpan.FromDays(60)),
            EntryAged("b", TimeSpan.FromDays(60)),
        });
        var trimmer = new CacheTrimmer(store, ClockAtNow());

        // Already-cancelled token: the run should throw rather than proceed.
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => trimmer.TrimAsync(Settings(), cts.Token));
    }

    [Fact]
    public void Constructor_throws_when_store_is_null()
    {
        Assert.Throws<ArgumentNullException>(() => new CacheTrimmer(null!));
    }

    [Fact]
    public async Task TrimAsync_throws_when_settings_is_null()
    {
        var trimmer = new CacheTrimmer(new FakeCacheStore(Array.Empty<CacheEntry>()), ClockAtNow());

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => trimmer.TrimAsync(null!));
    }

    [Fact]
    public async Task Entry_exactly_at_safety_boundary_is_kept()
    {
        // SafetyWindow boundary is inclusive-keep: LastModified == (now - window) stays.
        var store = new FakeCacheStore(new[]
        {
            EntryAged("edge", TimeSpan.FromMinutes(5)),
        });
        var trimmer = new CacheTrimmer(store, ClockAtNow());

        // MaxAge 0 makes it eligible by age; the 5-minute safety window must still save it.
        var result = await trimmer.TrimAsync(Settings(maxAgeDays: 0, safetyMinutes: 5));

        Assert.Equal(0, result.Deleted);
        Assert.True(store.Remaining.ContainsKey("edge"));
    }

    [Fact]
    public async Task Failed_delete_does_not_add_to_deleted_bytes()
    {
        var store = new FakeCacheStore(new[]
        {
            EntryAged("bad",  TimeSpan.FromDays(60), 5000),
            EntryAged("good", TimeSpan.FromDays(60), 100),
        });
        store.ThrowOnDelete.Add("bad");
        var trimmer = new CacheTrimmer(store, ClockAtNow());

        var result = await trimmer.TrimAsync(Settings());

        // Only the successfully deleted entry's bytes are counted.
        Assert.Equal(1, result.Deleted);
        Assert.Equal(1, result.Failed);
        Assert.Equal(100, result.DeletedBytes);
    }

    [Fact]
    public async Task Prune_is_invoked_and_its_count_surfaced_when_entries_were_deleted()
    {
        var store = new PrunableStore(
            new[] { EntryAged("old", TimeSpan.FromDays(60)) },
            pruneReturns: 3);
        var trimmer = new CacheTrimmer(store, ClockAtNow());

        var result = await trimmer.TrimAsync(Settings());

        Assert.Equal(1, result.Deleted);
        Assert.Equal(1, store.PruneCallCount);
        Assert.Equal(3, result.PrunedDirectories);
    }

    [Fact]
    public async Task Prune_is_not_invoked_when_nothing_was_deleted()
    {
        // Only a fresh entry -> nothing deleted -> no point walking the tree to prune.
        var store = new PrunableStore(
            new[] { EntryAged("fresh", TimeSpan.FromDays(1)) },
            pruneReturns: 5);
        var trimmer = new CacheTrimmer(store, ClockAtNow());

        var result = await trimmer.TrimAsync(Settings());

        Assert.Equal(0, result.Deleted);
        Assert.Equal(0, store.PruneCallCount);
        Assert.Equal(0, result.PrunedDirectories);
    }

    [Fact]
    public async Task Non_prunable_store_reports_zero_pruned_directories()
    {
        // A store without IPrunableCacheStore (e.g. the Azure blob store) skips pruning.
        var store = new FakeCacheStore(new[] { EntryAged("old", TimeSpan.FromDays(60)) });
        var trimmer = new CacheTrimmer(store, ClockAtNow());

        var result = await trimmer.TrimAsync(Settings());

        Assert.Equal(1, result.Deleted);
        Assert.Equal(0, result.PrunedDirectories);
    }

    /// <summary>
    /// A store that also supports directory pruning, so the trimmer's prune step can
    /// be asserted (was it called, with what result) without touching the filesystem.
    /// </summary>
    private sealed class PrunableStore : ICacheStore, IPrunableCacheStore
    {
        private readonly FakeCacheStore _inner;
        private readonly int _pruneReturns;

        public PrunableStore(IEnumerable<CacheEntry> entries, int pruneReturns)
        {
            _inner = new FakeCacheStore(entries);
            _pruneReturns = pruneReturns;
        }

        public int PruneCallCount { get; private set; }

        public IAsyncEnumerable<CacheEntry> ListAsync(CancellationToken cancellationToken = default)
            => _inner.ListAsync(cancellationToken);

        public Task<bool> DeleteAsync(string name, CancellationToken cancellationToken = default)
            => _inner.DeleteAsync(name, cancellationToken);

        public Task<int> PruneEmptyDirectoriesAsync(CancellationToken cancellationToken = default)
        {
            PruneCallCount++;
            return Task.FromResult(_pruneReturns);
        }
    }
}
