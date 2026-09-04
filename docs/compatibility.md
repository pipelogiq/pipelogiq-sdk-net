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

`0.4.0-preview.1` pairs with Pipelogiq server `v0.4.0-preview.1`.

Install the server before the SDK: the reliability routes below do not exist on
`v0.3.2-preview.6`, and a worker that calls them against an older server fails on the
first lease acquisition.

Baseline routes, available since `v0.3.x`:

- `POST /pipelines`
- `POST /logs`
- `POST /workers/bootstrap`
- `POST /workers/heartbeat`
- `POST /workers/events`
- `POST /workers/shutdown`
- `POST /pipelines/{pipelineId}/stages` (dynamic stage append; used by AI agent orchestration)
- `POST /stages/{stageId}/resume` (approval resume; used by confirmation stages)

Reliability routes, required from `v0.4.0-preview.1`:

- `POST /pipelines/idempotent` (`PostIdempotentPipelineAsync`)
- `POST /pipelines/by-idempotency-key` (`GetPipelineByIdempotencyKeyAsync`)
- `POST /pipelines/{pipelineId}/cancel` (`CancelPipelineAsync`)
- `POST /stages/{stageId}/lease/acquire` (`AcquireStageLeaseAsync`)
- `POST /stages/{stageId}/lease/renew` (`RenewStageLeaseAsync`)

An older SDK keeps working against the newer server, but its stages do not participate in
lease acquisition or result fencing and cannot opt into idempotent creation, error-code
retry filtering or sensitive context.

Because both platform and SDK are in active preview, validate SDK upgrades against your server environment before production rollout.
