Perform a security review of the source under `src/Umbraco.Community.ImageSharp.TrimCache.Core/`,
`…Azure/`, and `…Web/`.

## Dependency Review
Run, for each target framework, and report any known CVEs:
```
dotnet list src/Umbraco.Community.ImageSharp.TrimCache.Web/Umbraco.Community.ImageSharp.TrimCache.Web.csproj package --vulnerable --include-transitive
```
Note: NU1902/NU1903 advisories from Umbraco's own transitive dependencies are not
actionable by this package — report them but distinguish them from direct-dependency issues.

## Code Review

### Secrets Handling
- The Azure `ConnectionString` must NEVER be logged or written to error output (check log statements in the hosted service and store).
- Docs must steer users to user secrets / Cloud config, not source control.

### Destructive-Action Safety
- The trimmer must only ever scan/delete within the configured cache container/prefix (Azure) or cache folder (local) — NEVER the source media container/folder.
- `CacheFolderPath` is resolved against the content root; check the `~/`-relative path handling can't be coerced outside the intended cache folder (path traversal) given admin-supplied config.
- Safety window prevents deleting in-flight writes.

### Resource Safety / Denial of Service
- Listing streams entries lazily (`IAsyncEnumerable`) — confirm no unbounded in-memory materialisation of a huge cache.
- Per-instance re-entrancy gate prevents overlapping runs piling up.
- No unbounded loops; deletes are idempotent and failure-tolerant.

### Exception / Information Disclosure
- IO / storage exceptions are caught, logged, and don't crash the hosted service or leak internals.

### Input Validation
- Blob/file names come from storage listings, not user HTTP input — confirm no injection surface.
- No HTTP endpoints are exposed by the package — the trim runs only as a background hosted service.

Report findings with severity (Critical/High/Medium/Low), file path, line number, and recommended fix.
