# Compatibility

## Target framework

- SDK target framework: `net8.0`

## Breaking change policy (v0.x)

This SDK is currently in preview (`0.x`).

- Minor versions may include breaking API or behavior changes.
- Patch versions are intended for backwards-compatible fixes.
- Breaking changes are documented in `CHANGELOG.md`.

If you need strict stability, pin an exact package version.

## Supported Pipelogiq server versions

The SDK is intended for Pipelogiq server deployments that expose the worker and pipeline APIs used by this SDK, including:

- `POST /pipelines`
- `POST /logs`
- `POST /workers/bootstrap`
- `POST /workers/heartbeat`
- `POST /workers/events`
- `POST /workers/shutdown`
- `POST /pipelines/{pipelineId}/stages` (dynamic stage append; used by AI agent orchestration)
- `POST /stages/{stageId}/resume` (approval resume; used by confirmation stages)

Because both platform and SDK are in active preview, validate SDK upgrades against your server environment before production rollout.
