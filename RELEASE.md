# Release Process

This repository uses semantic versioning and currently publishes preview (`0.x`) releases.

## 1. Bump version

1. Update `<VersionPrefix>` in `Directory.Build.props`.
2. Add release notes entry in `CHANGELOG.md` with the release date.
3. Commit the changes.

Example:

```bash
git checkout -b release/v0.1.1
# edit Directory.Build.props and CHANGELOG.md
git add Directory.Build.props CHANGELOG.md
git commit -m "chore(release): prepare v0.1.1"
```

## 2. Create and push tag

```bash
git checkout main
git pull --ff-only
git tag v0.1.1
git push origin v0.1.1
```

Tag format is `vX.Y.Z`.

## 3. Publish package

```bash
dotnet restore
dotnet pack src/Pipelogiq.Sdk/PipelogiqSDK.csproj -c Release -o ./artifacts/packages

dotnet nuget push ./artifacts/packages/*.nupkg \
  --source "https://nuget.pkg.github.com/pipelogiq/index.json" \
  --api-key <GITHUB_TOKEN>

dotnet nuget push ./artifacts/packages/*.snupkg \
  --source "https://nuget.pkg.github.com/pipelogiq/index.json" \
  --api-key <GITHUB_TOKEN>
```

## 4. Generate release notes

1. Open GitHub Releases for this repository.
2. Create a release from the pushed tag (`vX.Y.Z`).
3. Use "Generate release notes" as a base.
4. Keep notes consistent with `CHANGELOG.md`.

## Semantic versioning policy

- `0.x` is preview: minor releases may contain breaking changes.
- Patch releases should remain backwards compatible where practical.
- Once stable (`1.0.0+`), breaking changes are major-version only.
