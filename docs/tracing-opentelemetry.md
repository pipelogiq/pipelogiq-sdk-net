# Tracing with OpenTelemetry

The SDK is compatible with OpenTelemetry and W3C trace context.

Current SDK behavior:

- `PipelineBuilder` / `EventBuilder` automatically copy `traceparent` / `tracestate` from `Activity.Current` into pipeline context items (unless you set them manually).
- `PipelineRunner` creates a consumer `Activity` around stage handler execution and sets `Activity.Current` for the handler scope (when an `ActivityListener` or OpenTelemetry pipeline is configured).

Manual propagation is still supported when you need full control.

## How `Activity` works

Use `System.Diagnostics.Activity` / `ActivitySource` in your service and handlers:

```csharp
using System.Diagnostics;

public static class Telemetry
{
    public static readonly ActivitySource Source = new("Pipelogiq.Worker");
}
```

When processing a stage, the SDK now creates a baseline consumer activity for the handler execution.
You can still create nested business spans inside the handler when needed.

## How `traceparent` is propagated

Recommended pattern:

1. Start an `Activity` in your API/background app (for example with ASP.NET Core or your own `ActivitySource`).
2. Build/send a pipeline or event with `PipelineBuilder` / `EventBuilder`.
3. The SDK copies `traceparent` / `tracestate` from `Activity.Current` into pipeline context items unless you already provided explicit values.
4. In the worker, the SDK starts a consumer activity for the handler using the incoming W3C trace context.
5. The SDK writes the updated trace context back into stage context payload so downstream stages can continue the trace.

Optional nested span in a handler (manual business instrumentation):

```csharp
using System.Diagnostics;
using PipelogiqSDK.StageHelper;

// SDK already created a consumer Activity around the handler execution.
// Create nested spans only for business-specific work.
using var activity = Telemetry.Source.StartActivity("fraud-check.rules", ActivityKind.Internal);

if (activity is not null)
{
    activity.SetTag("example.rule_set", "default");
}
```

## Integrate with ASP.NET Core

In ASP.NET Core APIs:

1. Use OpenTelemetry ASP.NET Core instrumentation as usual.
2. On request handling, read `Activity.Current`.
3. Build/send Pipelogiq pipelines/events while `Activity.Current` is active (the SDK captures `traceparent` automatically).

This links API-request spans with downstream worker spans.

## Export traces

Any standard OpenTelemetry exporter can be used (OTLP, Jaeger, Zipkin, etc.).
Typical setup:

- `OpenTelemetry.Extensions.Hosting`
- `OpenTelemetry.Instrumentation.AspNetCore`
- `OpenTelemetry.Instrumentation.Http`
- `OpenTelemetry.Exporter.OpenTelemetryProtocol`

The SDK stays exporter-agnostic and works with your host telemetry pipeline.
