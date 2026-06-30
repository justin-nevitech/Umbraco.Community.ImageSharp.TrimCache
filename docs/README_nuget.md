# Umbraco.Community.ImageSharp.TrimCache

Scheduled, age-based trimming of the ImageSharp.Web image cache for Umbraco.
Supports both the **Azure Blob** cache and the **local physical** cache, and runs
in the background as a hosted service.

The ImageSharp cache accumulates resized image variants that are never cleared
automatically — on Umbraco Cloud (or any blob-backed site) it can grow until it
exceeds the plan's media-storage limit. This package trims those variants by age,
on a schedule. Deleting a variant is safe: it is simply regenerated from the
original media on the next request. Source media is never touched.

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
  "CacheFolderPath": "umbraco/Data/TEMP/MediaCache",
  "ConnectionString": "",
  "ContainerName": "",
  "Prefix": "cache/"
}
```

- `Mode` — `Auto` (default), `Local` or `Azure`.
- `MaxAgeDays` — entries older than this are deleted (regenerated on next request).
- `IntervalMinutes` — run frequency, measured from startup. Default 1440 (24h).
- `ConnectionString` + `ContainerName` — required for Azure mode; must point at the
  same container ImageSharp's `AzureBlobStorageImageCache` writes to.
- `RunOnEveryServer` — load-balancing control; defaults to running on every server
  for a local (per-server) cache and only the scheduling server for a shared Azure
  cache.

See the [project README](https://github.com/justin-nevitech/Umbraco.Community.ImageSharp.TrimCache)
for full documentation.

## Author

Created and maintained by [Justin Neville](https://www.nevitech.co.uk) at
[Nevitech IT Solutions Ltd](https://www.nevitech.co.uk).
