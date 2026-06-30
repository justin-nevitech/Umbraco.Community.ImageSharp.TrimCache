Review the source under `src/Umbraco.Community.ImageSharp.TrimCache.Core/`,
`…Azure/`, and `…Web/` for correctness and robustness:

## Resource Management
- `SemaphoreSlim` re-entrancy gate (`_runGate`) disposed correctly; `Dispose` chains to the base (`new` + `base.Dispose()`).
- `PhysicalFileCacheStore` file enumeration/deletion releases handles; no leaks streaming large caches.
- `AzureBlobCacheStore` pages blobs lazily (no full materialisation).

## Cancellation & Shutdown
- `CacheTrimmer.TrimAsync` observes the cancellation token between entries and re-throws `OperationCanceledException` (clean stop, NOT tallied as a failure).
- Hosted service passes `ApplicationStopping`, skips starting when already stopping, and logs cancellation at Information (not Error).

## Trim Correctness
- Age cutoff is inclusive-keep at the boundary (`LastModified >= cutoff` is kept); safety window likewise.
- Delete failures are tallied (`Failed`) and the run continues; failed deletes do NOT add to `DeletedBytes`.
- Vanished entries (delete returns false) count as neither deleted nor failed.
- `PhysicalFileCacheStore` deletes the paired `.meta` best-effort (image delete keeps retry-on-lock semantics; meta lock must not fail the entry); `.meta` is skipped case-insensitively in listing and nested subfolders are enumerated.
- Deletes are idempotent (safe under cross-server double-run).

## Load Balancing & Guards
- `RunsOnEveryServer` resolves correctly: Local → every server (per-server caches), Azure → scheduling/single only (shared cache); `RunOnEveryServer` overrides it.
- Runtime-level guard (`RuntimeLevel.Run`) and server-role skip applied appropriately.

## Options Resolution
- `EffectiveMode` (Auto → Azure if configured else Local), `IsAzureConfigured`, `CanRun`, and `ResolveInterval`/`ResolveStartupDelay` clamping are correct.
- Composer only registers the hosted service when `Enabled && CanRun`.

## Multi-targeting (13 / 17 / 18)
- `UmbracoMajor` selects the dependency range; net10 single assembly serves 17 AND 18.
- Builds: `dotnet build src/Umbraco.Community.ImageSharp.TrimCache.Web/Umbraco.Community.ImageSharp.TrimCache.Web.csproj` (default) and with `-p:TargetFrameworks=net10.0 -p:UmbracoMajor=18`.
- No `#if` needed for Umbraco APIs across the supported set (the obsolete `PerformExecuteAsync(object?)` override still works on 18).
- No open-ended version ranges anywhere.

Report ALL findings with file paths and line numbers. Flag severity as Critical, High, Medium, or Low.
