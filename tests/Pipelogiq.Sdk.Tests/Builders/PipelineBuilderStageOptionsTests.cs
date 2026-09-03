using PipelogiqSDK.Builders;
using PipelogiqSDK.Configuration;
using PipelogiqSDK.Contracts;

using Xunit;

namespace PipelogiqSDK.Tests.Builders;

public sealed class PipelineBuilderStageOptionsTests
{
    [Fact]
    public void WithAction_ExplicitHandler_StageOptionsArgument_IsStoredAsOptions()
    {
        var pipeline = PipelineBuilder
            .Create("budget", CreateOptions())
            .SetApiKey("test-api-key")
            .WithAction(
                "evaluate-budget-strategy",
                "VesselOps.EvaluateBudgetStrategy",
                new StageOptions { TimeOut = 900 })
            .Build();

        var stage = Assert.Single(pipeline.Stages!);
        Assert.Null(stage.Input);
        Assert.Equal(900, stage.Options?.TimeOut);
    }

    [Fact]
    public void WithAction_GenericStageName_StageOptionsArgument_IsStoredAsOptions()
    {
        var pipeline = PipelineBuilder
            .Create("agent", CreateOptions())
            .SetApiKey("test-api-key")
            .WithAction<ThinkHandler>("agent:think", new StageOptions
            {
                RetryInterval = 120,
                MaxRetries = 30,
            })
            .Build();

        var stage = Assert.Single(pipeline.Stages!);
        Assert.Null(stage.Input);
        Assert.Equal("ThinkHandler", stage.StageHandlerName);
        Assert.Equal(120, stage.Options?.RetryInterval);
        Assert.Equal(30, stage.Options?.MaxRetries);
    }

    [Fact]
    public void WithAction_GenericTypeName_StageOptionsArgument_IsStoredAsOptions()
    {
        var pipeline = PipelineBuilder
            .Create("agent", CreateOptions())
            .SetApiKey("test-api-key")
            .WithAction<ResponderHandler>(new StageOptions { RunNextIfFailed = true })
            .Build();

        var stage = Assert.Single(pipeline.Stages!);
        Assert.Null(stage.Input);
        Assert.Equal("ResponderHandler", stage.StageName);
        Assert.Equal("ResponderHandler", stage.StageHandlerName);
        Assert.True(stage.Options?.RunNextIfFailed);
    }

    private static PipelogiqRunnerOptions CreateOptions()
    {
        return new PipelogiqRunnerOptions
        {
            ApiUrl = "http://localhost",
            ApiKey = "test-api-key",
        };
    }

    private sealed class ThinkHandler;

    private sealed class ResponderHandler;
}
