# MinimalWorker Example

A minimal runnable worker that registers one stage handler and starts `PipelineRunner`.

## Run

```bash
export PIPELOGIQ_API_KEY="<your-api-key>"
export PIPELOGIQ_API_URL="http://localhost:8081"

dotnet run --project examples/MinimalWorker/Pipelogiq.Sdk.Examples.MinimalWorker.csproj
```

Stop with `Ctrl+C`.

## What it shows

- SDK registration via `AddPipelogiq`
- Stage handler registration via `RegisterHandler`
- Minimal `IStageHandler<TInput>` implementation
- Returning `StageResult.Success(...)`
