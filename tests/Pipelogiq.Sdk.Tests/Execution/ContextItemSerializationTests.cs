using Microsoft.Extensions.DependencyInjection;
using PipelogiqSDK.Abstractions;
using PipelogiqSDK.Agent.Models;
using PipelogiqSDK.Contracts;
using PipelogiqSDK.Execution;
using PipelogiqSDK.StageHelper;
using System.Text.Json;

using Xunit;

namespace PipelogiqSDK.Tests.Execution;

public sealed class ContextItemSerializationTests
{
    [Fact]
    public async Task ExecuteStageHandlerAsync_RoundTripsComplexContextAcrossStages()
    {
        var executor = CreateExecutor();

        var writeResult = await executor.ExecuteStageHandlerAsync(new StageExecutionData
        {
            HandlerInstance = new ComplexContextWriterHandler(),
            StageId = 1,
            PipelineId = 100,
            ContextItems = new List<ContextItem>(),
        });

        var readResult = await executor.ExecuteStageHandlerAsync(new StageExecutionData
        {
            HandlerInstance = new ComplexContextReaderHandler(),
            StageId = 2,
            PipelineId = 100,
            ContextItems = writeResult.ContextItems,
        });

        Assert.True(readResult.IsSuccess);
    }

    private static StageExecutor CreateExecutor()
    {
        var services = new ServiceCollection();
        var serviceProvider = services.BuildServiceProvider();
        return new StageExecutor(serviceProvider);
    }

    private sealed class ComplexContextWriterHandler : IStageHandler
    {
        public Task<IStageResult> ExecuteAsync(IStageContext? context = null)
        {
            context!.Payload ??= new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            context.Payload["agent:toolResults"] = new List<AgentToolResult>
            {
                new()
                {
                    ToolName = "lookupCustomer",
                    ResultKey = "customer",
                    StatusCode = 200,
                    IsSuccess = true,
                    ResponseBody = "{\"id\":42,\"name\":\"Ada\"}",
                }
            };
            context.Payload["agent:metadata"] = new Dictionary<string, object?>
            {
                ["count"] = 2,
                ["active"] = true,
            };

            return Task.FromResult<IStageResult>(StageResult.Success("written"));
        }
    }

    private sealed class ComplexContextReaderHandler : IStageHandler
    {
        public Task<IStageResult> ExecuteAsync(IStageContext? context = null)
        {
            var toolResults = context.TryGetValue<List<AgentToolResult>>("agent:toolResults");
            Assert.NotNull(toolResults);
            Assert.Single(toolResults!);
            Assert.Equal("lookupCustomer", toolResults[0].ToolName);
            Assert.Equal(200, toolResults[0].StatusCode);

            var metadata = context.TryGetValue<Dictionary<string, object?>>("agent:metadata");
            Assert.NotNull(metadata);

            var count = AsInt(metadata!, "count");
            var active = AsBool(metadata, "active");
            Assert.Equal(2, count);
            Assert.True(active);

            return Task.FromResult<IStageResult>(StageResult.Success("read"));
        }

        private static int AsInt(IReadOnlyDictionary<string, object?> metadata, string key)
        {
            var value = metadata[key];
            return value switch
            {
                int i => i,
                long l => (int)l,
                JsonElement element when element.ValueKind == JsonValueKind.Number => element.GetInt32(),
                _ => Convert.ToInt32(value),
            };
        }

        private static bool AsBool(IReadOnlyDictionary<string, object?> metadata, string key)
        {
            var value = metadata[key];
            return value switch
            {
                bool b => b,
                JsonElement element when element.ValueKind is JsonValueKind.True or JsonValueKind.False => element.GetBoolean(),
                _ => Convert.ToBoolean(value),
            };
        }
    }
}
