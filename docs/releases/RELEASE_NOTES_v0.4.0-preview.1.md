# Pipelogiq .NET SDK 0.4.0-preview.1

**Released:** 2026-09-04
**Requires:** Pipelogiq server `v0.4.0-preview.1`
**Packages:** `PipelogiqSDK`, `PipelogiqSDK.Redis`, `PipelogiqSDK.Postgres`, `PipelogiqSDK.Testing`

```bash
dotnet add package PipelogiqSDK --version 0.4.0-preview.1
```

This release gives handlers the vocabulary and the runtime guarantees needed for workflows
with real side effects: idempotent creation, classified failures, execution leases and
cooperative cancellation.

The minor version moves because the public API grows substantially and one behaviour
changes — see [Behaviour changes](#behaviour-changes).

---

## Added

### Idempotent pipeline creation

```csharp
await PipelineBuilder.Create("claim-intake", options)
    .WithIdempotencyKey($"claim-{claimId}")
    .WithAction("verify", "VerifyClaimHandler")
    .SendAsync();
```

Sending the same key twice returns the existing pipeline instead of creating a second one.
`GetPipelineByIdempotencyKeyAsync` reconciles an outcome you did not observe — after a
timeout, a crash or a restart — without putting the key in a URL.

### Classified stage results

Handlers now say whether a failure is worth retrying, and why:

```csharp
return StageResult.RetryableError("upstream timed out", StageErrorCodes.Timeout);
return StageResult.BusinessRejected("policy is not active");   // terminal, never retried
return StageResult.ValidationError("claimId is missing");      // terminal
```

Full set: `RetryableError`, `TerminalError`, `Timeout`, `UpstreamError`,
`RateLimitExceeded`, `TransportUnavailable`, `BusinessRejected`, `ValidationError`,
`InvalidState`, `MissingRequiredData`.

`StageOptions` gained `RetryOnErrorCodes`, `Backoff` (`fixed` / `linear` / `exponential`),
`MaxRetryInterval` and `Jitter`, so a stage retries only the codes you allow, with a
bounded and jittered delay.

### Execution leases

`PipelineRunner` acquires a lease before running a handler and renews it in the background
for as long as the handler runs. A worker that loses its lease is told through
`IStageExecutionContext`, and a result submitted under a superseded execution id is
rejected by the server rather than overwriting fresher state.

### Cancellation and sensitive context

`CancelPipelineAsync` requests cooperative cancellation. Context items can be marked
sensitive so the server redacts them from status responses, dashboard logs and WebSocket
projections.

### Status vocabulary

`PipelineStatuses` and `StageStatuses` expose terminal checks, attempts, next retry time,
last error code and failure disposition, so a consumer can reconcile an unknown outcome
instead of guessing.

### Example

`examples/InsuranceClaimWorkflow` is an end-to-end claim submission and cancellation that
keeps business state in the consumer's own database — the pattern this release is designed
for.

---

## Behaviour changes

- **Agent stages report terminal failures.** `AgentResponderHandler` now returns a
  classified terminal result when the think handler marked the run unrecoverable (tool
  loop, exceeded budget). It previously returned success, so a retry policy would repeat a
  run that could not succeed. The user still receives the prepared apology. If you relied
  on agent stages always reporting success, adjust the pipeline's retry configuration.
- **Tool validation failures are recorded, not thrown.** A missing or unknown tool
  parameter is recorded as a tool error the planner can recover from, instead of raising
  out of the stage.

---

## Fixed

- **`PackageLicenseExpression` is `MIT`,** matching `LICENSE`, `NOTICE` and the README.
  Packages up to `0.3.2-preview.5` were published stamped `Apache-2.0`.
- **The version has one source.** Per-project `<Version>` elements were removed; all four
  packages inherit `<VersionPrefix>` from `Directory.Build.props`, which had already
  drifted from the per-project values.
- `scripts/publish-nuget.sh` reads the version from `Directory.Build.props`.
- Tracing tests encode and decode pipeline context values the way the builder and stage
  executor do on the wire.

## Removed

- The unused `Hashids.net` dependency, which every consumer had to resolve.
- An internal implementation prompt from the published documentation set.

## Infrastructure

- **CI added.** `.github/workflows/ci.yml` restores, builds, tests and packs on every push
  and pull request. The repository previously had no automated verification, which is why
  twelve failing tests went unnoticed for months. The suite is green: 87 of 87.

---

## Upgrading

1. **Upgrade the server first** to `v0.4.0-preview.1`. The lease, cancel and idempotency
   routes do not exist on `v0.3.2-preview.6`; a worker calling them against an older
   server fails on the first lease acquisition.
2. Bump the package:
   ```bash
   dotnet add package PipelogiqSDK --version 0.4.0-preview.1
   ```
3. No source changes are required. Existing handlers, builders and results compile and run
   unchanged — the new result factories and `StageOptions` fields are opt-in.
4. For workflows with side effects, opt in deliberately: add an idempotency key at
   creation, return classified results from handlers, and set `RetryOnErrorCodes` on the
   stages that may retry.

## Known limitations

The SDK does not make external side effects exactly-once. A handler must keep its own
stable external idempotency key and reconcile an unknown outcome before issuing another
command. A lease excludes competing workers only while it is valid.
