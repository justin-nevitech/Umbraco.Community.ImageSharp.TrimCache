# Local end-to-end testing guide

This walks through proving the trimmer works against a real local Umbraco
instance: generate a resized image, confirm it's cached, trim it, and confirm it
regenerates on the next request.

The key trick: don't wait for the 24-hour schedule. The DEBUG-only controller
(`ImageCacheTrimDebugController`) lets you run the trim on demand with an age
threshold measured in **minutes** instead of the production 30 days, so a
just-created variant becomes eligible in a couple of minutes.

> **The debug controller is compiled with `#if DEBUG`.** It exists only in Debug
> builds, so it's present when your test site has a **project reference** to the
> package (built in Debug) or references a Debug-built package — and is physically
> absent from Release builds. It can never ship to production.

The two debug endpoints work against whichever cache the current config resolves
to (local or Azure), so the cycle below is the same for both modes — only the
setup differs.

| Endpoint | Purpose |
|---|---|
| `GET /imagecachetrim/list` | Lists cache entries: `Name`, `LastModified`, `AgeMinutes`, `SizeBytes`. |
| `GET /imagecachetrim/run?maxAgeMinutes=2&safetyMinutes=0` | Runs the trim immediately. Returns `Examined`, `Deleted`, `DeletedBytes`, `DeletedMb`, `Failed`. |

---

## Option A — Local physical cache (default, simplest)

This is the default mode and needs no extra infrastructure (no Azurite, no
connection string). It trims Umbraco's local ImageSharp cache folder
(`~/umbraco/Data/TEMP/MediaCache` by default).

### Prerequisites
- A local Umbraco 13/17/18 site with at least one image in the Media library.
- A project reference to the package, built in **Debug** (so the debug controller
  is available).
- No `ImageCacheTrim` config is required — the defaults run in Local mode. (You can
  add `"ImageCacheTrim": { "Mode": "Local" }` to be explicit.)

### The cycle

1. **Generate a cached variant.** Request an image at a specific crop/size so
   ImageSharp generates and caches a variant:
   ```
   https://localhost:443xx/media/<your-image-path>?width=400&height=300&mode=crop
   ```
   You should get the resized image back. ImageSharp has now written the variant
   (plus a paired `.meta` file) into the cache folder.

2. **Confirm it's in the cache.**
   ```
   GET https://localhost:443xx/imagecachetrim/list
   ```
   Your new variant appears with an `AgeMinutes` close to 0. Note its `Name` (a
   file path in local mode).

3. **Wait a couple of minutes** so the variant ages past the test threshold (or
   pass `maxAgeMinutes=0` in the next step to skip the wait).

4. **Run the trim on demand.**
   ```
   GET https://localhost:443xx/imagecachetrim/run?maxAgeMinutes=2&safetyMinutes=0
   ```
   Your variant (and any others older than 2 minutes) should be counted in
   `Deleted`.

5. **Confirm deletion** — `GET /imagecachetrim/list` no longer shows it, and the
   file (and its `.meta`) are gone from the cache folder.

6. **Confirm regeneration** — request the same cropped URL again; it still returns
   the correct image (regenerated from the original media), and a fresh
   `GET /imagecachetrim/list` shows the variant back with `AgeMinutes` near 0.

---

## Option B — Azure blob cache (via Azurite)

Use this to test the Azure path without a real storage account or any cost.

### Prerequisites
- A local Umbraco 13/17/18 site with at least one image in the Media library.
- The site configured to use `AzureBlobStorageImageCache` pointed at **Azurite**
  (the local emulator) rather than a real storage account.
- Azurite running: `docker run -p 10000:10000 mcr.microsoft.com/azure-storage/azurite`
- A project reference to the package (Debug build), with `appsettings.Development.json`:

  ```json
  "ImageCacheTrim": {
    "Enabled": true,
    "MaxAgeDays": 30,
    "ConnectionString": "UseDevelopmentStorage=true",
    "ContainerName": "<your-imagesharp-cache-container>",
    "Prefix": "cache/"
  }
  ```

  (`ContainerName`/`Prefix` must match whatever your local
  `AzureBlobStorageImageCache` is configured to write to — the **cache** container,
  not the source media container.)

### The cycle

Identical to Option A's steps 1–6. The only differences:
- In step 1 the variant is written as a **blob** in the cache container.
- In step 2 the `Name` values are blob names rather than file paths.

Because Azurite stamps `LastModified` at write time (like real blob storage), the
age logic behaves identically to production — you're testing the real path, not a
simulation.

---

## Notes

- The full loop — cached → trimmed → regenerated — is exactly what the scheduled
  job performs in production, just compressed from days into minutes.
- To test the **scheduled** path itself (not just the on-demand trigger),
  temporarily set a low `IntervalMinutes` (and small `StartupDelayMinutes`) in a
  local build and watch the logs for the `CacheTrim complete` summary line.
- In a load-balanced test, remember that Azure mode trims on one server while
  Local mode trims on every server (see `RunOnEveryServer` in the main README).
