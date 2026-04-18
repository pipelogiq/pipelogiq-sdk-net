using System.Text.Json;
using PipelogiqSDK.Agent.Models;
using PipelogiqSDK.Api;
using PipelogiqSDK.Contracts;

using Xunit;

namespace PipelogiqSDK.Tests.Api;

public sealed class SdkJsonSerializerTests
{
    [Fact]
    public void Serialize_Preserves_JsonElement_Primitives_Inside_ToolStage_Input()
    {
        using var doc = JsonDocument.Parse("""
        {
          "projectId": "164b802d-7b59-498a-9d36-9e19ab4541a5",
          "jobId": "bc737edd-8388-41ac-8d7f-3bacabf05789",
          "currency": "USD",
          "confidenceScore": "62",
          "lineItems": [
            {
              "rowNumber": "1.1",
              "description": "Docking / Undocking"
            }
          ]
        }
        """);

        var paramsPayload = doc.RootElement
            .EnumerateObject()
            .ToDictionary(
                property => property.Name,
                property => (object?)property.Value.Clone(),
                StringComparer.OrdinalIgnoreCase);

        var request = new AppendStagesRequest
        {
            Stages =
            [
                new StageInfo
                {
                    StageName = "agent:tool:saveBudgetResult",
                    StageHandlerName = "AgentToolHandler",
                    Input = new AgentToolCallInput
                    {
                        ToolName = "saveBudgetResult",
                        Params = paramsPayload,
                        ResultKey = "saveBudgetResult",
                    }
                }
            ]
        };

        var json = SdkJsonSerializer.Serialize(request);

        Assert.Contains("\"projectId\":\"164b802d-7b59-498a-9d36-9e19ab4541a5\"", json);
        Assert.Contains("\"jobId\":\"bc737edd-8388-41ac-8d7f-3bacabf05789\"", json);
        Assert.Contains("\"currency\":\"USD\"", json);
        Assert.Contains("\"confidenceScore\":\"62\"", json);
        Assert.Contains("\"lineItems\":[{\"rowNumber\":\"1.1\",\"description\":\"Docking / Undocking\"}]", json);
        Assert.DoesNotContain("ValueKind", json);
    }
}
