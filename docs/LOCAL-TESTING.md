# Local end-to-end testing guide

Prove the trimmer works against a real local Umbraco instance: generate a resized
image, confirm it's cached, let the scheduled job trim it, and confirm it regenerates
on the next request. It uses the package's normal background service — there are **no
test-only API endpoints**.

Pick one:
- **[Option A — Local disk cache](#option-a--local-disk-cache)** — simplest, no extra tools.
- **[Option B — Azure cache via Azurite](#option-b--azure-cache-via-azurite)** — tests the Azure path, needs Docker/Azurite.

## Test site ports

Each test site runs on its own HTTPS port (substitute for `443xx` below):

| Site | URL |
|---|---|
| v13 | `https://localhost:44313` |
| v17 | `https://localhost:44317` |
| v18 | `https://localhost:44318` |

## Fast-feedback config (already applied)

The trim keeps a **5-minute safety window** — anything written in the last 5 minutes is
never deleted, and this is not configurable. So the soonest a freshly-created variant can
be removed is ~5 minutes. The three test sites are already set up for quick testing in
`appsettings.Development.json`:

```json
"ImageCacheTrim": {
  "Enabled": true,
  "Mode": "Local",
  "MaxAgeDays": 0,
  "StartupDelayMinutes": 0,
  "IntervalMinutes": 1
}
```

- `MaxAgeDays: 0` → "old enough to delete" means "older than the 5-minute safety window"
  (instead of the production default of 30 days).
- `StartupDelayMinutes: 0` → a trim runs shortly after boot.
- `IntervalMinutes: 1` → it re-checks every minute, so an eligible variant is picked up
  promptly (the 5-minute safety window still sets the earliest a variant can be deleted).

Every run logs one line at Information level to the console — this is how you observe it:

```
CacheTrim complete. Examined 3, deleted 1 entr(y/ies) freeing 0.2 MB, pruned 2 empty folder(s), 0 failure(s). Cutoff: older than 00:00:00.
```

---

## Option A — Local disk cache

The default mode. No extra infrastructure. It trims Umbraco's local ImageSharp cache
folder, `~/umbraco/Data/TEMP/MediaCache`.

**Prerequisite:** at least one image uploaded to the site's Media library.

### Steps

1. **Run the site** on the default **`Umbraco.Web.UI`** profile (VS/Rider run dropdown,
   or `dotnet run --launch-profile "Umbraco.Web.UI"`). Keep the console visible.

2. **Generate a cached variant** — request an image at a specific crop/size so ImageSharp
   creates and caches one (use your site's port from the table above):
   ```
   https://localhost:44317/media/<your-image-path>?width=400&height=300&mode=crop
   ```
   You should get the resized image back. ImageSharp has now written the variant
   (`<base>.<ext>`) and a paired `<base>.meta` into the cache folder.

3. **Confirm it's cached** — open `…/umbraco/Data/TEMP/MediaCache` (under the test site
   folder). The variant sits in a nested (sharded) subfolder.

4. **Make it eligible** — pick one:
   - **Wait ~5 minutes** for it to age past the safety window, **or**
   - **Backdate the cache folder** so the next tick picks it up immediately (adjust the
     path to your site):
     ```powershell
     $cache = "src\Umbraco.Community.ImageSharp.TrimCache.TestSite.v17\umbraco\Data\TEMP\MediaCache"
     Get-ChildItem -Path $cache -Recurse -File | ForEach-Object { $_.LastWriteTime = (Get-Date).AddDays(-1) }
     ```

5. **Watch the trim run** — on the next tick (within a minute of the variant becoming
   eligible) the console logs `CacheTrim complete. … deleted 1 …`. Check the cache
   folder: the variant and its `.meta` are gone, and empty shard folders left behind are
   pruned too.

6. **Confirm regeneration** — request the same cropped URL from step 2 again. It returns
   the correct image (regenerated from the original media) and a fresh variant reappears
   in the cache folder.

---

## Option B — Azure cache via Azurite

Tests the Azure path without a real storage account or any cost, using the local
[Azurite](https://learn.microsoft.com/azure/storage/common/storage-use-azurite) emulator.
Each test site ships a ready-made **`Umbraco.Web.UI (Azurite)`** launch profile, so
switching from local to Azure is just picking a profile — no settings to edit by hand.

**What the profile does:** it sets environment variables that route ImageSharp's **image
cache** to an Azurite blob container (`imagesharp-cache-v13`/`-v17`/`-v18`, under the
`cache/` prefix) and point the trimmer at that same container in `Azure` mode. **Your
media library stays on local disk** — only the generated image cache moves — so switching
back to the default profile never strands media.

### Steps

1. **Start Azurite** (Docker):
   ```bash
   docker run -p 10000:10000 mcr.microsoft.com/azure-storage/azurite azurite-blob --blobHost 0.0.0.0 --skipApiVersionCheck
   ```
   `--skipApiVersionCheck` lets Azurite accept the newer storage API version that recent
   Azure SDKs (pulled in by Umbraco 17/18) send — without it you'll get a `400
   InvalidHeaderValue … API version not supported` error. Upgrading Azurite to the latest
   is the alternative.

2. **Run the site** on the **`Umbraco.Web.UI (Azurite)`** profile (VS/Rider run dropdown,
   or `dotnet run --launch-profile "Umbraco.Web.UI (Azurite)"`). Keep the console visible.
   The cache container is created automatically on the first cache write; if your Azurite
   setup doesn't auto-create it, create it once with Azure Storage Explorer or
   `az storage container create --name imagesharp-cache-v17 --connection-string "UseDevelopmentStorage=true"`.

3. **Generate a cached variant** — request an image at a crop/size (site port from the
   table above):
   ```
   https://localhost:44317/media/<your-image-path>?width=400&height=300&mode=crop
   ```
   The variant is now written as a **blob** in the container rather than a file on disk.

4. **Confirm it's cached** — list the blobs (or browse the container in Azure Storage
   Explorer under the Emulator account):
   ```powershell
   az storage blob list --connection-string "UseDevelopmentStorage=true" --container-name imagesharp-cache-v17 --prefix cache/ --output table
   ```

5. **Wait ~5 minutes.** You **can't** backdate a blob's modified time (the storage service
   sets it on write and it can't be moved into the past), so there's no shortcut here —
   let the safety window pass.

6. **Watch the trim run** — the console logs `CacheTrim complete. … deleted 1 …` on the
   next tick, and the blob list from step 4 is now empty.

7. **Confirm regeneration** — request the same cropped URL again; it returns the correct
   image and the blob reappears in the container.

To switch back to local, relaunch with the plain **`Umbraco.Web.UI`** profile.

---

## Code-level tests (no Umbraco, no waiting)

- **Trim logic** — the unit tests write real files to a temp directory, backdate them, and
  assert exactly what's deleted:
  ```bash
  dotnet test src/Umbraco.Community.ImageSharp.TrimCache.Tests/Umbraco.Community.ImageSharp.TrimCache.Tests.csproj
  ```
- **Azure store** — integration tests run directly against Azurite when a connection string
  is set (they skip otherwise):
  ```powershell
  $env:AZURITE_CONNECTION = "UseDevelopmentStorage=true"
  dotnet test src/Umbraco.Community.ImageSharp.TrimCache.Tests/Umbraco.Community.ImageSharp.TrimCache.Tests.csproj
  ```

---

## Notes

- This loop — cached → trimmed → regenerated — is exactly what the scheduled job does in
  production, just with `MaxAgeDays: 0` and a short interval so it happens in minutes
  instead of days.
- **Don't keep re-requesting the image while you wait.** Each request regenerates the
  variant and resets its timestamp, restarting the 5-minute clock — generate it once and
  leave it alone.
- In a load-balanced test, Azure mode trims on one server while Local mode trims on every
  server (see `RunOnEveryServer` in the main README).
