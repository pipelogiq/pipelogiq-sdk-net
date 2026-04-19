using Microsoft.Extensions.DependencyInjection;
using PipelogiqSDK.Agent.Configuration;
using PipelogiqSDK.Agent.Extensions;
using PipelogiqSDK.Agent.Models;
using PipelogiqSDK.Agent.Services;

using Xunit;

namespace PipelogiqSDK.Tests.Agent;

public sealed class AgentServiceCollectionExtensionsTests
{
    [Fact]
    public void AddPipelogiqAgent_MultipleCalls_ReusesOptionsAndPreservesTools()
    {
        var services = new ServiceCollection();

        var builder = services.AddPipelogiqAgent(options =>
        {
            options.LlmApiKey = "llm-key";
            options.UseReActMode = true;
        });

        builder.AddTool(new AgentToolDefinition
        {
            Name = "getOrder",
            Description = "Gets order by id",
            HttpMethod = "GET",
            UrlTemplate = "/api/orders/{id}"
        });

        services.AddPipelogiqAgent(options =>
        {
            options.RequireConfirmationForMutations = true;
            options.TargetApiBaseUrl = "https://api.example.com";
        });

        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<IAgentToolRegistry>();
        var options = provider.GetRequiredService<AgentOptions>();

        Assert.Single(registry.GetAll());
        Assert.True(options.UseReActMode);
        Assert.True(options.RequireConfirmationForMutations);
        Assert.Equal("https://api.example.com", options.TargetApiBaseUrl);
        Assert.Equal("llm-key", options.LlmApiKey);
        Assert.NotNull(provider.GetService<ILlmPlanner>());
    }

    [Fact]
    public void AddTelegramAgentChannel_PreservesExistingToolsAndRegistersTelegramServices()
    {
        var services = new ServiceCollection();

        var builder = services.AddPipelogiqAgent(options =>
        {
            options.LlmApiKey = "telegram-llm-key";
            options.UseReActMode = true;
            options.RequireConfirmationForMutations = true;
            options.TargetApiBaseUrl = "https://api.example.com";
            options.MaxThinkSteps = 9;
        });
        builder.AddTool(new AgentToolDefinition
        {
            Name = "lookupCustomer",
            Description = "Gets customer by id",
            HttpMethod = "GET",
            UrlTemplate = "/api/customers/{id}"
        });

        services.AddTelegramAgentChannel(new TelegramAgentChannelOptions
        {
            TelegramBotToken = "123:abc",
        });

        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<IAgentToolRegistry>();
        var options = provider.GetRequiredService<AgentOptions>();
        var telegramOptions = provider.GetRequiredService<TelegramAgentChannelOptions>();

        Assert.Single(registry.GetAll());
        Assert.Equal("telegram-llm-key", options.LlmApiKey);
        Assert.True(options.UseReActMode);
        Assert.True(options.RequireConfirmationForMutations);
        Assert.Equal("https://api.example.com", options.TargetApiBaseUrl);
        Assert.Equal(9, options.MaxThinkSteps);
        Assert.Equal("123:abc", telegramOptions.TelegramBotToken);
        Assert.NotNull(provider.GetService<IAgentNotificationRouter>());
        Assert.NotNull(provider.GetService<IAgentNotificationChannel>());
        Assert.NotNull(provider.GetService<IHttpClientFactory>());
    }

    [Fact]
    public void AddTelegramAgentChannel_WithoutAgentRegistration_Throws()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddTelegramAgentChannel(new TelegramAgentChannelOptions
            {
                TelegramBotToken = "123:abc",
            }));

        Assert.Contains("AddPipelogiqAgent", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddPipelogiqAgent_OllamaProvider_RegistersPlannerWithoutApiKey()
    {
        var services = new ServiceCollection();

        services.AddPipelogiqAgent(options =>
        {
            options.LlmProvider = AgentLlmProvider.Ollama;
            options.LlmModel = "gemma3";
            options.LlmApiBaseUrl = "http://localhost:11434";
        });

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<AgentOptions>();

        Assert.Equal(AgentLlmProvider.Ollama, options.LlmProvider);
        Assert.Equal("gemma3", options.LlmModel);
        Assert.NotNull(provider.GetService<ILlmPlanner>());
    }

    [Fact]
    public void AddPipelogiqAgent_StepRouterWithOpenAiCatalog_RegistersBuiltInPlanner()
    {
        var services = new ServiceCollection();

        services.AddPipelogiqAgent(options =>
        {
            options.LlmProvider = AgentLlmProvider.Anthropic;
            options.LlmModel = "claude-sonnet-4-6";
            options.StepRouter = new AgentLlmStepRouter
            {
                Think = new AgentLlmStepRoute
                {
                    Provider = AgentLlmProvider.OpenAI,
                    Model = "gpt-4.1-mini"
                }
            };
            options.Providers = new AgentProviderCatalog
            {
                OpenAI = new AgentProviderConnection
                {
                    ApiKey = "openai-key",
                    ApiBaseUrl = "https://api.openai.com"
                }
            };
        });

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<ILlmPlanner>());
    }

    [Fact]
    public void AddPipelogiqAgent_ConfiguresLlmHttpClientTimeout()
    {
        var services = new ServiceCollection();

        services.AddPipelogiqAgent(options =>
        {
            options.LlmApiKey = "llm-key";
            options.LlmRequestTimeout = TimeSpan.FromMinutes(7);
        });

        using var provider = services.BuildServiceProvider();

        var factory = provider.GetRequiredService<IHttpClientFactory>();
        var client = factory.CreateClient("pipelogiq-agent-llm");

        Assert.Equal(TimeSpan.FromMinutes(7), client.Timeout);
    }
}
