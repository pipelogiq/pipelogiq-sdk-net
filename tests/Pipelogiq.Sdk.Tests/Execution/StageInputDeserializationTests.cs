using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PipelogiqSDK.Abstractions;
using PipelogiqSDK.Agent.Models;
using PipelogiqSDK.Execution;
using PipelogiqSDK.StageHelper;

using Xunit;

namespace PipelogiqSDK.Tests.Execution;

public sealed class StageInputDeserializationTests
{
    [Fact]
    public async Task ExecuteStageHandlerAsync_DeserializesCamelCaseStageInput()
    {
        var executor = CreateExecutor();
        var handler = new CaptureToolInputHandler();

        var result = await executor.ExecuteStageHandlerAsync(new StageExecutionData
        {
            HandlerInstance = handler,
            InputType = typeof(AgentToolCallInput),
            JsonInput = """
                        {"toolName":"getProjectWorkItems","params":{"projectId":"p1","limit":"10"},"resultKey":"getProjectWorkItems"}
                        """,
            StageId = 1,
            PipelineId = 42,
        });

        Assert.True(result.IsSuccess);
        Assert.NotNull(handler.Captured);
        Assert.Equal("getProjectWorkItems", handler.Captured!.ToolName);
        Assert.Equal("getProjectWorkItems", handler.Captured.ResultKey);
        // Free-form params deserialize into JsonElement values; AgentToolHandler coerces them
        // against the tool schema before use, so the contract here is the scalar content.
        Assert.Equal("p1", ScalarOf(handler.Captured.Params["projectId"]));
        Assert.Equal("10", ScalarOf(handler.Captured.Params["limit"]));
    }

    private static StageExecutor CreateExecutor()
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        return new StageExecutor(provider);
    }

    private sealed class CaptureToolInputHandler : IStageHandler<AgentToolCallInput>
    {
        public AgentToolCallInput? Captured { get; private set; }

        public Task<IStageResult> ExecuteAsync(AgentToolCallInput input, IStageContext? context = null)
        {
            Captured = input;
            return Task.FromResult<IStageResult>(StageResult.Success("ok"));
        }
    }

    /// <summary>Reads a free-form parameter value as its scalar text.</summary>
    private static string ScalarOf(object? value) => value switch
    {
        null => string.Empty,
        JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonElement element => element.ToString(),
        _ => value.ToString() ?? string.Empty,
    };

}
