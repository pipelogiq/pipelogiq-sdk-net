# Worker And Pipeline Host Example

A structured example project that shows both:

- running a Pipelogiq worker (`worker` mode)
- submitting a demo pipeline (`pipeline` mode)

It is organized into separate folders for handlers, services, hosted services, models, and configuration.

## Project layout

- `Configuration/` - mode + environment settings parsing
- `Handlers/` - sample stage handlers
- `HostedServices/` - host entry points for worker/pipeline modes
- `Models/` - stage input DTOs
- `Services/` - pipeline launcher, handler registry, trace propagation helpers

## Environment variables

- `PIPELOGIQ_API_KEY` (required)
- `PIPELOGIQ_API_URL` (optional, default `http://localhost:8081`)
- `PIPELOGIQ_WORKER_NAME` (optional, default `checkout-worker-example`)
- `PIPELOGIQ_PIPELINE_NAME` (optional, default `checkout-demo`)

## Run worker mode

```bash
export PIPELOGIQ_API_KEY="<your-api-key>"
export PIPELOGIQ_API_URL="http://localhost:8081"

dotnet run --project examples/WorkerAndPipelineHost/Pipelogiq.Sdk.Examples.WorkerAndPipelineHost.csproj -- worker
```

## Run pipeline submission mode

```bash
export PIPELOGIQ_API_KEY="<your-api-key>"
export PIPELOGIQ_API_URL="http://localhost:8081"

dotnet run --project examples/WorkerAndPipelineHost/Pipelogiq.Sdk.Examples.WorkerAndPipelineHost.csproj -- pipeline
```

## Handlers included

- `FraudCheckHandler` (`IStageHandler<FraudCheckInput>`)
- `ChargeCustomerHandler` (`IStageHandler<ChargeCustomerInput>`)
- `ReceiptHandler` (`IStageHandler` without typed input)

The pipeline submission example builds a simple checkout flow using these handlers.
