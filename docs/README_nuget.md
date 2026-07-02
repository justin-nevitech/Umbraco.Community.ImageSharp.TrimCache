# Umbraco.Community.ImageSharp.TrimCache

Scheduled, age-based trimming of the ImageSharp.Web image cache for Umbraco.
Supports both the **Azure Blob** cache and the **local physical** cache, and runs
in the background as a hosted service.

The ImageSharp cache accumulates resized image variants that are never cleared
automatically — on Umbraco Cloud (or any blob-backed site) it can grow until it
exceeds the plan's media-storage limit. This package trims those variants by age,
on a schedule. Deleting a variant is safe: it is simply regenerated from the
original media on the next request. The trimmer only scans the cache location you
configure, so keep that separate from source media — in Azure mode, point it at a
cache-only container, never the media container.

Supports **Umbraco 13, 17 and 18**.

## Installation

```bash
dotnet add package Umbraco.Community.ImageSharp.TrimCache
```

The hosted service is wired up automatically — no manual registration needed. By
default it trims the local physical cache; if Azure blob storage is configured it
trims the Azure cache instead.

## Configuration

Add an `ImageCacheTrim` section to `appsettings.json` (defaults shown):

```json
"ImageCacheTrim": {
  "Enabled": true,
  "Mode": "Auto",
  "MaxAgeDays": 30,
  "IntervalMinutes": 1440,
  "StartupDelayMinutes": 5,
  "CacheFolderPath": "",
  "ConnectionString": "",
  "ContainerName": "",
  "Prefix": ""
}
```

- `Mode` — `Auto` (default), `Local` or `Azure`.
- `MaxAgeDays` — entries older than this are deleted (regenerated on next request).
- `IntervalMinutes` — run frequency, measured from startup. Default 1440 (24h).
- `CacheFolderPath` — local mode only; leave empty (the default) to follow Umbraco's
  configured ImageSharp cache folder (`Umbraco:CMS:Imaging:Cache:CacheFolder`).
- `ConnectionString` + `ContainerName` — required for Azure mode; must point at the
  same container your ImageSharp Azure blob cache writes to (never the source media
  container). An empty `Prefix` scans the whole container.
- `RunOnEveryServer` — load-balancing control; defaults to running on every server
  for a local (per-server) cache and only the scheduling server for a shared Azure
  cache.

See the [project README](https://github.com/justin-nevitech/Umbraco.Community.ImageSharp.TrimCache)
for full documentation.

## Author

Created and maintained by [Justin Neville](https://www.nevitech.co.uk) at
[Nevitech IT Solutions Ltd](https://www.nevitech.co.uk).

## Credits

Icon: [Cleanup icons created by Icon home - Flaticon](https://www.flaticon.com/free-icons/cleanup)
