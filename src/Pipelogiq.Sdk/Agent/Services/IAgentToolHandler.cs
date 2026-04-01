using PipelogiqSDK.Abstractions;
using PipelogiqSDK.Agent.Models;

namespace PipelogiqSDK.Agent.Services;

/// <summary>
/// Defines a native agent tool handler without typed parameters.
/// Implement this to run arbitrary .NET code as an agent tool instead of making an HTTP call.
/// </summary>
public interface IAgentToolHandler
{
    /// <summary>
    /// Executes the tool with the parameters supplied by the LLM.
    /// </summary>
    /// <param name="parameters">Parameters resolved and validated by the agent runtime.</param>
    /// <param name="context">Pipeline stage context. Use to read/write shared state.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<AgentToolOutput> ExecuteAsync(
        IReadOnlyDictionary<string, object?> parameters,
        IStageContext? context = null,
        CancellationToken ct = default);
}

/// <summary>
/// Defines a native agent tool handler with a strongly-typed parameter object.
/// The SDK deserializes the LLM-supplied parameters into <typeparamref name="TInput"/> automatically.
/// </summary>
/// <typeparam name="TInput">
/// A class whose properties map to the tool's declared parameters.
/// Supports any type serializable via <c>System.Text.Json</c>.
/// </typeparam>
public interface IAgentToolHandler<in TInput> where TInput : class
{
    /// <summary>
    /// Executes the tool with a deserialized typed input.
    /// </summary>
    /// <param name="input">Deserialized parameter object.</param>
    /// <param name="context">Pipeline stage context. Use to read/write shared state.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<AgentToolOutput> ExecuteAsync(
        TInput input,
        IStageContext? context = null,
        CancellationToken ct = default);
}
