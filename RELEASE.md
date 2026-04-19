# Release Process

This repository uses semantic versioning and currently publishes preview (`0.x`) releases.

## 1. Bump version

1. Update `<VersionPrefix>` in `Directory.Build.props` and package `<Version>` values in the publishable `.csproj` files.
2. Add release notes entry in `CHANGELOG.md` with the release date.
3. Commit the changes.

Example:

```bash
git checkout -b release/v0.3.2-preview.4
# edit Directory.Build.props, package .csproj files, and CHANGELOG.md
git add Directory.Build.props src/Pipelogiq.Sdk/*.csproj src/Pipelogiq.Sdk.Redis/*.csproj src/Pipelogiq.Sdk.Postgres/*.csproj src/Pipelogiq.Sdk.Testing/*.csproj CHANGELOG.md
git commit -m "chore(release): prepare v0.3.2-preview.4"
```

## 2. Create and push tag

```bash
git checkout main
git pull --ff-only
git tag v0.3.2-preview.4
git push origin v0.3.2-preview.4
```

Tag format is `vX.Y.Z` or `vX.Y.Z-preview.N`.

## 3. Publish package

```bash
NUGET_API_KEY=<your-key> ./scripts/publish-nuget.sh
```

Packages are published to `https://api.nuget.org/v3/index.json`.

## 4. Generate release notes

1. Open GitHub Releases for this repository.
2. Create a release from the pushed tag (`vX.Y.Z` or `vX.Y.Z-preview.N`).
3. Use "Generate release notes" as a base.
4. Keep notes consistent with `CHANGELOG.md`.

## Semantic versioning policy

- `0.x` is preview: minor releases may contain breaking changes.
- Patch releases should remain backwards compatible where practical.
- Once stable (`1.0.0+`), breaking changes are major-version only.
