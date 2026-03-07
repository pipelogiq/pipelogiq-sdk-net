using PipelogiqSDK.Builders;
using PipelogiqSDK.Contracts;

namespace PipelogiqSDK.Api;

/// <summary>
/// Convenience facade for sending builders through API.
/// </summary>
public static class PipelineService
{
    /// <summary>
    /// Sends a pipeline built with <see cref="PipelineBuilder"/> and returns the created pipeline.
    /// </summary>
    /// <param name="builder">Pipeline builder instance.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Created pipeline response with id, status and stages.</returns>
    public static Task<PipelineResponse> StartPipelineAsync(PipelineBuilder builder, CancellationToken ct = default)
    {
        return builder.SendAsync(ct);
    }

    /// <summary>
    /// Sends an event built with <see cref="EventBuilder"/> and returns the created pipeline.
    /// </summary>
    /// <param name="builder">Event builder instance.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Created pipeline response with id, status and stages.</returns>
    public static Task<PipelineResponse> StartEventAsync(EventBuilder builder, CancellationToken ct = default)
    {
        return builder.SendAsync(ct);
    }

    /// <summary>
    /// Sends a log built with <see cref="LogBuilder"/>.
    /// </summary>
    /// <param name="builder">Log builder instance.</param>
    /// <param name="ct">Cancellation token.</param>
    public static Task SendLogAsync(LogBuilder builder, CancellationToken ct = default)
    {
        return builder.SendAsync(ct);
    }
}
