Run the full pre-publish checklist for the Umbraco.Community.ImageSharp.TrimCache package.

## 1. Build Solution
```
dotnet build src/Umbraco.Community.ImageSharp.TrimCache.slnx -c Release
```
- Must be 0 errors.
- Report any code warnings. Ignore NuGet vulnerability advisories (NU1902/NU1903)
  from Umbraco's transitive dependencies, and the CS0618/CS0672 deprecation warnings
  for `PerformExecuteAsync(object?)`/`MapPathContentRoot` (expected on newer Umbraco).

## 2. Run Tests
```
dotnet test src/Umbraco.Community.ImageSharp.TrimCache.Tests/Umbraco.Community.ImageSharp.TrimCache.Tests.csproj -c Release
```
- All tests must pass (Azurite integration tests are SkippableFacts and will skip
  unless `AZURITE_CONNECTION` is set — that's expected).

## 3. Verify the explicit Umbraco 18 build
17 and 18 share net10, so confirm the strict 18 compile in addition to the default (17):
```
dotnet build src/Umbraco.Community.ImageSharp.TrimCache/Umbraco.Community.ImageSharp.TrimCache.csproj -c Release -p:TargetFrameworks=net10.0 -p:UmbracoMajor=18
```
- Must be 0 errors.

## 4. Pack and Inspect
```
dotnet pack src/Umbraco.Community.ImageSharp.TrimCache/Umbraco.Community.ImageSharp.TrimCache.csproj -c Release -p:Version=1.0.0 -o artifacts/nupkg-check
```
Verify the nupkg contains:
- `lib/net8.0/` and `lib/net10.0/` — each with all FOUR bundled assemblies:
  `…TrimCache.dll`, `…TrimCache.Core.dll`, `…TrimCache.Azure.dll`, `…TrimCache.Web.dll`
- `README_nuget.md`
- `icon.png` (once `docs/icon.png` has been added)
- `LICENSE`
- Correct nuspec metadata (ID, title, description, authors, license, tags, repository URL)
- Correct per-TFM dependency groups:
  - net8.0 → `Umbraco.Cms.Web.Common [13.0.0, 14.0.0)` + `Azure.Storage.Blobs [12.22.2, 13.0.0)`
  - net10.0 → `Umbraco.Cms.Web.Common [17.0.0, 19.0.0)` + `Azure.Storage.Blobs [12.22.2, 13.0.0)`
  - No Core/Azure/Web project dependencies leaked (they are bundled, not referenced).

## 5. Verify Documentation
Check these are up to date and consistent (supported versions = 13, 17, 18):
- `.github/README.md` — supported-versions table, config table (incl. `RunOnEveryServer`), behaviour notes, author/website link
- `docs/README_nuget.md` — condensed version, author/website link
- `docs/LOCAL-TESTING.md` — Local + Azure cycles
- `umbraco-marketplace.json` — valid `Category` (enum), `AuthorDetails.Url`, Title, tags, description; NO `IconUrl` (schema is additionalProperties:false; icon comes from `PackageIcon`)

## 6. Verify CI/CD
- `.github/workflows/release.yml` exists and packs the correct main `.csproj`
- Installs net8.0 AND net10.0 SDKs (the package multi-targets)
- Version injected via `/p:Version=${{github.ref_name}}`
- `NUGET_API_KEY` secret is referenced

## 7. Report
Summarize the results as a checklist with pass/fail for each item.
