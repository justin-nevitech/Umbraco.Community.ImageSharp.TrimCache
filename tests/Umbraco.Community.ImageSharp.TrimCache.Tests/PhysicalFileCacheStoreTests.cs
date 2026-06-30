using Umbraco.Community.ImageSharp.TrimCache.Core;
using Xunit;

namespace Umbraco.Community.ImageSharp.TrimCache.Tests;

/// <summary>
/// Tests for the local physical cache store. Uses a real temp directory (fast, no
/// Azure), and specifically verifies the two-files-per-variant (.meta) pairing
/// behaviour that distinguishes the physical cache from the blob cache.
/// </summary>
public sealed class PhysicalFileCacheStoreTests : IDisposable
{
    private readonly string _dir;

    public PhysicalFileCacheStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ictrim-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private string WriteFile(string name, string content = "x")
    {
        var path = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task Lists_image_files_but_not_meta_files()
    {
        WriteFile("aaa");
        WriteFile("aaa.meta");
        WriteFile("bbb");
        WriteFile("bbb.meta");

        var store = new PhysicalFileCacheStore(_dir);

        var names = new List<string>();
        await foreach (var entry in store.ListAsync())
        {
            names.Add(Path.GetFileName(entry.Name));
        }

        Assert.Equal(2, names.Count);
        Assert.Contains("aaa", names);
        Assert.Contains("bbb", names);
        Assert.DoesNotContain("aaa.meta", names);
        Assert.DoesNotContain("bbb.meta", names);
    }

    [Fact]
    public async Task Delete_removes_both_the_image_and_its_meta_pair()
    {
        var image = WriteFile("ccc");
        var meta = WriteFile("ccc.meta");

        var store = new PhysicalFileCacheStore(_dir);
        var deleted = await store.DeleteAsync(image);

        Assert.True(deleted);
        Assert.False(File.Exists(image));
        Assert.False(File.Exists(meta));
    }

    [Fact]
    public async Task Delete_of_image_without_meta_still_succeeds()
    {
        var image = WriteFile("ddd");

        var store = new PhysicalFileCacheStore(_dir);
        var deleted = await store.DeleteAsync(image);

        Assert.True(deleted);
        Assert.False(File.Exists(image));
    }

    [Fact]
    public async Task Delete_of_missing_file_returns_false()
    {
        var store = new PhysicalFileCacheStore(_dir);
        var deleted = await store.DeleteAsync(Path.Combine(_dir, "nope"));

        Assert.False(deleted);
    }

    [Fact]
    public async Task Missing_cache_folder_yields_no_entries()
    {
        var store = new PhysicalFileCacheStore(Path.Combine(_dir, "does-not-exist"));

        var any = false;
        await foreach (var _ in store.ListAsync())
        {
            any = true;
        }

        Assert.False(any);
    }

    [Fact]
    public async Task Entries_report_last_write_time_and_size()
    {
        var path = WriteFile("eee", "some-bytes-here");

        var store = new PhysicalFileCacheStore(_dir);

        CacheEntry? found = null;
        await foreach (var entry in store.ListAsync())
        {
            if (entry.Name == path) found = entry;
        }

        Assert.NotNull(found);
        Assert.True(found!.SizeBytes > 0);
        Assert.True(found.LastModified <= DateTimeOffset.UtcNow.AddSeconds(1));
    }

    [Fact]
    public async Task Full_trim_against_local_store_deletes_old_and_keeps_new()
    {
        // Old file: backdate its last-write time well past the cutoff.
        var oldPath = WriteFile("old");
        File.SetLastWriteTimeUtc(oldPath, DateTime.UtcNow.AddDays(-60));
        WriteFile("old.meta");

        // New file: leave at "now".
        var newPath = WriteFile("new");

        var store = new PhysicalFileCacheStore(_dir);
        var trimmer = new CacheTrimmer(store);

        var result = await trimmer.TrimAsync(new TrimSettings
        {
            MaxAge = TimeSpan.FromDays(30),
            SafetyWindow = TimeSpan.FromMinutes(5),
        });

        Assert.Equal(1, result.Deleted);
        Assert.False(File.Exists(oldPath));
        Assert.False(File.Exists(oldPath + ".meta"));
        Assert.True(File.Exists(newPath));
    }

    [Fact]
    public async Task Enumerates_files_in_nested_subdirectories()
    {
        // The ImageSharp physical cache nests variants in hashed subfolders.
        WriteFile("a/b/c/nested");
        WriteFile("a/b/c/nested.meta");
        WriteFile("top");

        var store = new PhysicalFileCacheStore(_dir);

        var names = new List<string>();
        await foreach (var entry in store.ListAsync())
        {
            names.Add(Path.GetFileName(entry.Name));
        }

        Assert.Equal(2, names.Count);
        Assert.Contains("nested", names);
        Assert.Contains("top", names);
        Assert.DoesNotContain("nested.meta", names);
    }

    [Fact]
    public async Task Delete_removes_orphaned_meta_even_when_image_already_gone()
    {
        // Image absent, only the .meta remains: delete reports false (no image) but
        // still cleans up the orphaned .meta so it cannot linger forever.
        var imagePath = Path.Combine(_dir, "lonely");
        var metaPath = WriteFile("lonely.meta");

        var store = new PhysicalFileCacheStore(_dir);
        var deleted = await store.DeleteAsync(imagePath);

        Assert.False(deleted);
        Assert.False(File.Exists(metaPath));
    }

    [SkippableFact]
    public async Task Locked_image_delete_is_reported_as_failure()
    {
        // File locking is only mandatory on Windows; elsewhere an open file can be
        // deleted, so this behaviour is Windows-specific.
        Skip.IfNot(OperatingSystem.IsWindows(),
            "File locking semantics only apply on Windows.");

        var oldPath = WriteFile("locked");
        File.SetLastWriteTimeUtc(oldPath, DateTime.UtcNow.AddDays(-60));

        // Hold an exclusive handle so File.Delete throws IOException.
        using (var _ = new FileStream(oldPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var store = new PhysicalFileCacheStore(_dir);
            var trimmer = new CacheTrimmer(store);

            var result = await trimmer.TrimAsync(new TrimSettings
            {
                MaxAge = TimeSpan.FromDays(30),
                SafetyWindow = TimeSpan.FromMinutes(5),
            });

            Assert.Equal(1, result.Examined);
            Assert.Equal(0, result.Deleted);
            Assert.Equal(1, result.Failed);
        }

        Assert.True(File.Exists(oldPath)); // still there, to be retried next run
    }

    [Fact]
    public async Task Prune_removes_empty_nested_folders_bottom_up_but_keeps_the_root()
    {
        // a/b/c is a chain of empty folders; a/b/c is removed, which empties a/b,
        // which empties a — all three go, the root stays.
        Directory.CreateDirectory(Path.Combine(_dir, "a", "b", "c"));

        var store = new PhysicalFileCacheStore(_dir);
        var removed = await store.PruneEmptyDirectoriesAsync();

        Assert.Equal(3, removed);
        Assert.False(Directory.Exists(Path.Combine(_dir, "a")));
        Assert.True(Directory.Exists(_dir)); // root never removed
    }

    [Fact]
    public async Task Prune_keeps_folders_that_still_contain_files()
    {
        WriteFile("keep/here/file");          // keep + keep/here must survive
        Directory.CreateDirectory(Path.Combine(_dir, "keep", "empty")); // this one goes

        var store = new PhysicalFileCacheStore(_dir);
        var removed = await store.PruneEmptyDirectoriesAsync();

        Assert.Equal(1, removed);
        Assert.True(Directory.Exists(Path.Combine(_dir, "keep", "here")));
        Assert.False(Directory.Exists(Path.Combine(_dir, "keep", "empty")));
    }

    [Fact]
    public async Task Prune_on_missing_cache_folder_returns_zero()
    {
        var store = new PhysicalFileCacheStore(Path.Combine(_dir, "does-not-exist"));

        Assert.Equal(0, await store.PruneEmptyDirectoriesAsync());
    }

    [Fact]
    public async Task Trim_prunes_folders_left_empty_by_deletes_and_reports_the_count()
    {
        // A variant nested as ImageSharp would shard it; backdate it so it's trimmed.
        var oldPath = WriteFile("7/f/2/old");
        File.SetLastWriteTimeUtc(oldPath, DateTime.UtcNow.AddDays(-60));
        WriteFile("7/f/2/old.meta");

        var store = new PhysicalFileCacheStore(_dir);
        var trimmer = new CacheTrimmer(store);

        var result = await trimmer.TrimAsync(new TrimSettings
        {
            MaxAge = TimeSpan.FromDays(30),
            SafetyWindow = TimeSpan.FromMinutes(5),
        });

        Assert.Equal(1, result.Deleted);
        Assert.Equal(3, result.PrunedDirectories);          // 7, 7/f, 7/f/2 all emptied
        Assert.False(Directory.Exists(Path.Combine(_dir, "7")));
        Assert.True(Directory.Exists(_dir));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_requires_a_cache_folder(string? path)
    {
        Assert.Throws<ArgumentException>(() => new PhysicalFileCacheStore(path!));
    }

    [Fact]
    public async Task Meta_files_are_skipped_case_insensitively()
    {
        WriteFile("img");
        WriteFile("img.META");   // upper-case extension must still be treated as meta

        var store = new PhysicalFileCacheStore(_dir);

        var names = new List<string>();
        await foreach (var entry in store.ListAsync())
        {
            names.Add(Path.GetFileName(entry.Name));
        }

        Assert.Single(names);
        Assert.Contains("img", names);
        Assert.DoesNotContain("img.META", names);
    }

    [Fact]
    public async Task ListAsync_honours_cancellation()
    {
        WriteFile("one");
        WriteFile("two");

        var store = new PhysicalFileCacheStore(_dir);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in store.ListAsync(cts.Token))
            {
            }
        });
    }
}
