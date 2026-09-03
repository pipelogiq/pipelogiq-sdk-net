# Private-insurance reliability example

This example uses only generic Pipelogiq primitives. It deliberately creates a
normal pipeline after the payment event; it does not keep a pipeline waiting for
payment.

`EnsureClaimHandler` keeps claim state and the stable external idempotency key
in the consumer database. After `OutcomeUnknown`, every later execution calls
`GET status` for the original claim. A second blind `POST` is forbidden by the
state machine. The consumer store's atomic `NotSubmitted -> Submitting`
transition grants only one concurrent handler execution the right to issue the
first POST; all other executions reconcile by status.
If the status API can authoritatively prove that the request key was never
received (for example, after a crash between the local transition and POST), a
second compare-and-set may permit one handler to POST again with the same key.
It never uses a new key and never does so before the status query.

Run against a local control plane:

```bash
PIPELOGIQ_API_URL=http://localhost:8081 \
PIPELOGIQ_API_KEY=... \
dotnet run --project examples/InsuranceClaimWorkflow/InsuranceClaimWorkflow.csproj
```

The creation key is application-scoped and retained for the lifetime of the
pipeline. It is an opaque technical value persisted before the first request,
not a token, claim payload, PII, or raw business identifier. Save the returned
`PipelineResponse.Id` in the consumer database.
`BUSINESS_REJECTED` is terminal; only the four configured transient error codes
are retried with capped exponential backoff and jitter.

The separate cancellation pipeline makes cancellation of an external insurance
reservation repeatable. `CancelPipelineAsync` cancels a Pipelogiq pipeline
itself; it cannot undo an external side effect that has already started.
