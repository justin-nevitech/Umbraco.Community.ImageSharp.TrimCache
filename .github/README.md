# Umbraco.Community.ImageSharp.TrimCache

[![Downloads](https://img.shields.io/nuget/dt/Umbraco.Community.ImageSharp.TrimCache?color=cc9900)](https://www.nuget.org/packages/Umbraco.Community.ImageSharp.TrimCache/)
[![NuGet](https://img.shields.io/nuget/vpre/Umbraco.Community.ImageSharp.TrimCache?color=0273B3)](https://www.nuget.org/packages/Umbraco.Community.ImageSharp.TrimCache)
[![GitHub license](https://img.shields.io/github/license/justin-nevitech/Umbraco.Community.ImageSharp.TrimCache?color=8AB803)](../LICENSE)

Scheduled, age-based trimming of the [ImageSharp.Web](https://docs.sixlabors.com/articles/imagesharp.web/)
image cache for Umbraco. Supports both the **Azure Blob** cache and the **local
physical** cache, and runs in the background as a hosted service.

The ImageSharp cache accumulates resized image variants that are never cleared
automatically. On Umbraco Cloud (or any blob-backed site) this can grow until it
exceeds the plan's media-storage limit. This package trims those variants by age,
on a schedule — deleted variants are simply regenerated from the original media on
the next request.

Supports **Umbraco 13, 17 and 18**.

## Why it's safe

The cache is disposable derived output. Deleting a variant that is still in use is
not data loss — it is a cache miss, and the next request regenerates it from the
original media. So an age-based trim does not need to identify "unused" variants
precisely. The trimmer only ever scans the cache location you configure — the local
ImageSharp cache folder, or the Azure cache container/prefix — so keep that separate
from source media. In Azure mode especially, point it at a cache-only container
(never the media container); it deletes whatever it's told to scan.

## Installation

Add the package to an existing Umbraco website from NuGet:

```bash
dotnet add package Umbraco.Community.ImageSharp.TrimCache
```

The hosted service is wired up automatically via a composer — no manual
registration is required. By default it trims the **local** physical cache; if
Azure blob storage is configured (connection string + container) it trims the
**Azure** cache instead. You can also force either mode explicitly.

## Configuration

Add an `ImageCacheTrim` section to `appsettings.json` (all values are optional and
shown here with their defaults):

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
  "Prefix": "",
  "AllowUnprefixedContainer": false,
  "RunOnEveryServer": null
}
```

| Setting | Description |
|---|---|
| `Enabled` | Master on/off switch. |
| `Mode` | `Auto` (default), `Local`, or `Azure`. Auto uses Azure when blob storage is configured, otherwise the local physical cache. |
| `MaxAgeDays` | Entries older than this are deleted (regenerated on next request). Clamped to 0 or more. |
| `IntervalMinutes` | How often the trim runs, measured **from startup** (not at wall-clock times). Default 1440 (24h), minimum 1. Read at startup, so changes apply on the next app restart. |
| `StartupDelayMinutes` | Delay before the first run after app start (default 5). |
| `CacheFolderPath` | **Local mode only.** Path to the ImageSharp physical cache folder, relative to the content root (or absolute). Leave empty (the default) to follow Umbraco's configured ImageSharp cache folder (`Umbraco:CMS:Imaging:Cache:CacheFolder`); set it only to trim a different folder. |
| `ConnectionString` | **Azure mode only** (required for it; supplying it in Auto mode switches to Azure). An account connection string, or one carrying a SAS (`BlobEndpoint=…;SharedAccessSignature=…`) — the SAS form is how Umbraco Cloud exposes storage (see below). The credential must allow **List** and **Delete** on the blobs. |
| `ContainerName` | **Azure mode only.** The container the ImageSharp cache writes to. |
| `Prefix` | **Azure mode only.** Blob-name prefix that scopes the scan (e.g. `cache/`). **Set this whenever the cache shares a container with other data (e.g. media)** — everything under the prefix older than the max age is deleted. Leave empty *only* for a dedicated cache-only container (and see `AllowUnprefixedContainer`). |
| `AllowUnprefixedContainer` | **Azure mode safety gate** (default `false`). With an empty `Prefix` the trimmer would age-delete across the **whole** container, so it **refuses to run** unless you set this to `true` to confirm the container holds nothing but the ImageSharp cache. Leave it `false` (the default) and set `Prefix` whenever media shares the container. |
| `RunOnEveryServer` | Load-balancing control. **Unset/`null` (default) = auto:** Local mode runs on *every* server (each has its own physical cache); Azure mode runs only on the scheduling/single server (shared cache). Set `true` to force every server, `false` to force only the scheduling/single server. No effect on a single server. |

> `ContainerName` / `Prefix` must match the container your ImageSharp Azure blob
> cache writes to. With an empty `Prefix` the trimmer would scan the **entire**
> container, so it **refuses to run** unless you either set a `Prefix` **or** set
> `AllowUnprefixedContainer: true` for a dedicated cache-only container. Keep
> `ConnectionString` out of source control (user secrets / Cloud config).

### Umbraco Cloud (and other SAS-based storage)

Umbraco Cloud accesses its Azure Blob storage with a **SAS** rather than an account key,
and the trimmer uses the same kind of connection string. Put the SAS connection string in
`ConnectionString`, with the container and a prefix:

```json
"ImageCacheTrim": {
  "Mode": "Azure",
  "ConnectionString": "BlobEndpoint=https://<account>.blob.core.windows.net/;SharedAccessSignature=<sas-token>",
  "ContainerName": "<container>",
  "Prefix": "cache/"
}
```

If what you have is a **SAS URL** — `https://<account>.blob.core.windows.net/<container>?sv=…&sig=…` —
convert it to that connection string:
- everything **before** the `?` (minus the container) → `BlobEndpoint` (`https://<account>.blob.core.windows.net/`),
- everything **after** the `?` → `SharedAccessSignature` (`sv=…&sig=…`),
- the `<container>` segment → `ContainerName`.

Notes:
- The SAS must grant **List** and **Delete**. A read-only SAS lets the trim run but every
  delete fails (logged as failures — nothing is removed).
- Keep the SAS out of source control (user secrets / Cloud environment config).

> ⚠️ **Set `Prefix`.** On Umbraco Cloud the ImageSharp cache often lives in the **same
> blob container as your media**. The trimmer deletes every blob under the
> container + prefix that's older than `MaxAgeDays`, so with an **empty** prefix it would
> delete your **media** too. To prevent that, Azure mode with an empty `Prefix`
> **refuses to run** — it logs an error at startup and deletes nothing — unless you set
> `AllowUnprefixedContainer: true` to confirm the container is cache-only. Set `Prefix`
> to the cache's subfolder (e.g. `cache/`) so only cached variants are trimmed — confirm
> it by listing the container and seeing where the cache blobs actually sit.

### Operational behaviour

- **Graceful shutdown.** An in-flight run observes the application's stopping token
  and bails out promptly (between entries), so it never blocks shutdown or is
  killed mid-delete.
- **No overlapping runs.** A per-instance re-entrancy gate skips a scheduled run if
  the previous one is still in progress. Deletes are idempotent regardless.
- **Load balancing.** With a shared cache (Azure blob), the trim runs only on the
  scheduling/single server — one pass cleans the cache for everyone. With per-server
  local caches it runs on every server, so each trims its own disk. This is chosen
  automatically from the mode and can be overridden with `RunOnEveryServer`.
- **Startup notes.** In Azure mode, an empty `Prefix` with no `AllowUnprefixedContainer`
  opt-in makes the service **refuse to run**: it logs an error at startup and deletes
  nothing, because trimming the whole container could delete media. Setting
  `AllowUnprefixedContainer: true` allows it but logs a warning that the whole container
  will be trimmed. In local mode, an explicit `CacheFolderPath` override logs the resolved
  folder that will be trimmed, so a mis-pointed path is visible.
- **Logging.** Each run writes a single summary line at Information level, e.g.
  `CacheTrim complete. Examined 1240, deleted 312 entr(y/ies) freeing 458.7 MB, pruned 40 empty folder(s), 0 failure(s). Cutoff: older than 30.00:00:00.`

## Project layout

The solution is split so the trim logic is fully testable without Azure or Umbraco:

| Project | References | Purpose |
|---|---|---|
| `…TrimCache.Core` | logging abstractions only | The trim algorithm, `ICacheStore`, the physical-file store, DTOs and settings. No Azure, no Umbraco. This is what the unit tests exercise. |
| `…TrimCache.Azure` | `Azure.Storage.Blobs` | `AzureBlobCacheStore` — the only type that touches the Azure SDK. Deliberately thin. |
| `…TrimCache.Web` | Umbraco CMS | The hosted service, composer and options. Host concerns only; no trim logic. |
| `…TrimCache` | the three above | The meta-package consumers install. Bundles the other three assemblies into one NuGet package. |
| `…TrimCache.Tests` | xUnit, FakeTimeProvider, SkippableFact | Unit tests (in-memory fake + real temp directory) and Azurite integration tests. |

The dependency direction is one-way: `Core` knows about nothing; everything else
depends on `Core`. Swapping Azure for another backend (S3, etc.) means writing one
new `ICacheStore` and changing nothing in `Core`.

### Supported versions

Because Umbraco 17 and 18 both run on .NET 10, the dependency can't be chosen by
target framework alone — the `UmbracoMajor` build property selects it:

| Umbraco | TFM | Build |
|---|---|---|
| 13 (LTS) | net8.0  | `dotnet build -f net8.0 -p:UmbracoMajor=13` |
| 17 (LTS) | net10.0 | `dotnet build -f net10.0 -p:UmbracoMajor=17` (default) |
| 18 (STS) | net10.0 | `dotnet build -f net10.0 -p:UmbracoMajor=18` |

The shipped net10.0 assembly references Umbraco `[17.0.0, 19.0.0)` and uses only
stable, shared CMS APIs, so a single build serves both Umbraco 17 and 18.

## Testing

```bash
# Unit tests only (Azurite integration tests skip themselves):
dotnet test

# Include the Azurite integration tests:
docker run -d -p 10000:10000 mcr.microsoft.com/azure-storage/azurite azurite-blob --blobHost 0.0.0.0 --skipApiVersionCheck
export AZURITE_CONNECTION="UseDevelopmentStorage=true"
dotnet test
```

See [docs/LOCAL-TESTING.md](../docs/LOCAL-TESTING.md) for the full
cached → trimmed → regenerated loop against a local Umbraco instance (and against
Azurite), driving the scheduled job with a short interval so it happens in minutes.

## Author

Created and maintained by [Justin Neville](https://www.nevitech.co.uk) at
[Nevitech IT Solutions Ltd](https://www.nevitech.co.uk).

## Contributing

Contributions are most welcome! Please read the [Contributing Guidelines](CONTRIBUTING.md).

## Credits

Icon: [Cleanup icons created by Icon home - Flaticon](https://www.flaticon.com/free-icons/cleanup)
