using PipelogiqSDK.Builders;

namespace PipelogiqSDK.Api;

public static class PipelineService
{
    public static Task StartPipelineAsync(PipelineBuilder builder, CancellationToken ct = default)
    {
        return builder.SendAsync(ct);
    }

    public static Task StartEventAsync(EventBuilder builder, CancellationToken ct = default)
    {
        return builder.SendAsync(ct);
    }

    public static Task SendLogAsync(LogBuilder builder, CancellationToken ct = default)
    {
        return builder.SendAsync(ct);
    }
}
