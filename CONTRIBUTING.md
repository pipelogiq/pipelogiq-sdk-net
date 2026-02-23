# Contributing

Thanks for helping improve the Pipelogiq .NET SDK.

## Prerequisites

- .NET SDK 8.0+

## Build

```bash
dotnet restore
dotnet build PipelogiqSdk.sln
```

## Tests

```bash
dotnet test PipelogiqSdk.sln
```

If no test projects are present, `dotnet test` only validates build/test discovery.

## Coding style

- Follow `.editorconfig` settings.
- Keep nullable reference types enabled.
- Prefer explicit, small changes.
- Do not mix behavioral refactors with formatting-only edits.

## Branch naming

Use one of:

- `feature/<short-description>`
- `fix/<short-description>`
- `chore/<short-description>`
- `docs/<short-description>`

## Pull request guidelines

- Keep PRs focused and reviewable.
- Include a clear problem statement and change summary.
- Link related issues (if any).
- Update docs/examples when API usage changes.
- Ensure `dotnet build` and `dotnet pack` pass before requesting review.
