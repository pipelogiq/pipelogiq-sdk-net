# Release Process

This repository uses semantic versioning and currently publishes preview (`0.x`) releases.

## 1. Bump version

1. Update `<VersionPrefix>` in `Directory.Build.props`. This is the only place the version
   lives — all four packages inherit it, so nothing else needs editing.
2. Add the release entry in `CHANGELOG.md` with the release date, and release notes in
   `docs/releases/`.
3. Note the Pipelogiq server version this release pairs with, in `README.md` and
   `docs/compatibility.md`.
4. Commit the changes.

Example:

```bash
git checkout -b release/v0.4.0-preview.1
# edit Directory.Build.props, CHANGELOG.md, docs/releases/, README.md
git add Directory.Build.props CHANGELOG.md docs README.md
git commit -m "chore(release): prepare v0.4.0-preview.1"
```

## 2. Confirm CI is green

`\.github/workflows/ci.yml` restores, builds, tests and packs on every push and pull
request. Do not tag a commit whose CI run is red.

## 3. Create and push tag

```bash
git checkout main
git pull --ff-only
git tag v0.4.0-preview.1
git push origin v0.4.0-preview.1
```

Tag format is `vX.Y.Z` or `vX.Y.Z-preview.N`.

## 4. Publish package

```bash
NUGET_API_KEY=<your-key> ./scripts/publish-nuget.sh
```

Packages are published to `https://api.nuget.org/v3/index.json`.

## 5. Publish the GitHub release

1. Open GitHub Releases for this repository.
2. Create a release from the pushed tag (`vX.Y.Z` or `vX.Y.Z-preview.N`).
3. Use "Generate release notes" as a base.
4. Keep notes consistent with `CHANGELOG.md`.

## Semantic versioning policy

- `0.x` is preview: minor releases may contain breaking changes.
- Patch releases should remain backwards compatible where practical.
- Once stable (`1.0.0+`), breaking changes are major-version only.
