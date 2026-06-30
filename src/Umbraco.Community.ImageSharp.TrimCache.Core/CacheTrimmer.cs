using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Umbraco.Community.ImageSharp.TrimCache.Core;

/// <summary>
/// The pure trim algorithm. Depends only on <see cref="ICacheStore"/> and
/// <see cref="TimeProvider"/>, so it can be unit-tested with an in-memory store
/// and a pinned clock — no Azure, no Umbraco, no real time.
/// </summary>
public sealed class CacheTrimmer
{
    private readonly ICacheStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CacheTrimmer> _logger;

    public CacheTrimmer(
        ICacheStore store,
        TimeProvider? timeProvider = null,
        ILogger<CacheTrimmer>? logger = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        // Default to the system clock; tests pass a FakeTimeProvider.
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<CacheTrimmer>.Instance;
    }

    public async Task<TrimResult> TrimAsync(
        TrimSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var now = _timeProvider.GetUtcNow();
        var cutoff = now - settings.MaxAge;
        var safetyBoundary = now - settings.SafetyWindow;

        long examined = 0;
        long deleted = 0;
        long deletedBytes = 0;
        long failed = 0;

        await foreach (var entry in _store.ListAsync(cancellationToken)
                           .WithCancellation(cancellationToken))
        {
            // Bail promptly if the host is shutting down. Throws
            // OperationCanceledException, which the caller treats as a clean stop.
            cancellationToken.ThrowIfCancellationRequested();

            examined++;

            // Too new to be eligible by age.
            if (entry.LastModified >= cutoff)
            {
                continue;
            }

            // Inside the safety window — never touch.
            if (entry.LastModified >= safetyBoundary)
            {
                continue;
            }

            try
            {
                var didDelete = await _store.DeleteAsync(entry.Name, cancellationToken);
                if (didDelete)
                {
                    deleted++;
                    deletedBytes += entry.SizeBytes;
                }
            }
            catch (OperationCanceledException)
            {
                // Shutdown mid-delete: stop the run cleanly, don't tally as a
                // failure. Re-thrown so the caller logs a cancellation, not an error.
                throw;
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogWarning(ex,
                    "CacheTrim: failed to delete entry {Name}.", entry.Name);
            }
        }

        // After enumeration has fully completed (so we never remove a folder out
        // from under an in-flight directory walk), prune any now-empty folders left
        // behind by the deletes. Only backends with real nested folders implement
        // this — the Azure blob store does not, so this step is simply skipped.
        var prunedDirectories = 0;
        if (_store is IPrunableCacheStore prunable)
        {
            prunedDirectories = await prunable.PruneEmptyDirectoriesAsync(cancellationToken);
        }

        var result = new TrimResult(examined, deleted, deletedBytes, failed, prunedDirectories);

        _logger.LogInformation(
            "CacheTrim complete. Examined {Examined}, deleted {Deleted} entr(y/ies) " +
            "freeing {Mb:F1} MB, pruned {Pruned} empty folder(s), {Failed} failure(s). " +
            "Cutoff: older than {MaxAge}.",
            result.Examined, result.Deleted, result.DeletedMegabytes,
            result.PrunedDirectories, result.Failed, settings.MaxAge);

        return result;
    }
}
