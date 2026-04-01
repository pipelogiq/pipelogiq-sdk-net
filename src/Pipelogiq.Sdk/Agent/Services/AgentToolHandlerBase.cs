using System.Text.Json;
using System.Text.Json.Serialization;
using PipelogiqSDK.Abstractions;
using PipelogiqSDK.Agent.Models;

namespace PipelogiqSDK.Agent.Services;

/// <summary>
/// Convenience base class for typed native tool handlers.
/// Deserializes the LLM-supplied parameter dictionary into <typeparamref name="TInput"/>
/// and delegates to <see cref="ExecuteAsync(TInput, IStageContext?, CancellationToken)"/>.
/// </summary>
/// <typeparam name="TInput">
/// A class whose properties match the tool's declared parameters.
/// Property names are matched case-insensitively.
/// </typeparam>
/// <example>
/// <code>
/// public record DiscountInput(decimal Price, decimal Percent);
///
/// public class CalculateDiscountHandler : AgentToolHandlerBase&lt;DiscountInput&gt;
/// {
///     protected override Task&lt;AgentToolOutput&gt; ExecuteAsync(
///         DiscountInput input, IStageContext? context, CancellationToken ct)
///     {
///         var amount = Math.Round(input.Price * input.Percent / 100, 2);
///         return Task.FromResult(AgentToolOutput.Success(amount.ToString("F2")));
///     }
/// }
/// </code>
/// </example>
public abstract class AgentToolHandlerBase<TInput> : IAgentToolHandler where TInput : class
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // LLMs sometimes send numbers as JSON strings (e.g. "280" instead of 280).
        // Allow this so numeric tool parameters are accepted regardless of quoting.
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <inheritdoc />
    public async Task<AgentToolOutput> ExecuteAsync(
        IReadOnlyDictionary<string, object?> parameters,
        IStageContext? context = null,
        CancellationToken ct = default)
    {
        TInput input;
        try
        {
            // Serialize to JSON then deserialize to TInput so that nested types,
            // records, and init-only properties are all handled correctly.
            var json = JsonSerializer.Serialize(parameters, JsonOptions);
            input = JsonSerializer.Deserialize<TInput>(json, JsonOptions)
                ?? throw new InvalidOperationException($"Failed to deserialize parameters into {typeof(TInput).Name}.");
        }
        catch (Exception ex)
        {
            return AgentToolOutput.Failure($"Parameter deserialization failed: {ex.Message}");
        }

        return await ExecuteAsync(input, context, ct);
    }

    /// <summary>
    /// Executes the tool logic with a strongly-typed input object.
    /// </summary>
    protected abstract Task<AgentToolOutput> ExecuteAsync(
        TInput input,
        IStageContext? context = null,
        CancellationToken ct = default);
}
