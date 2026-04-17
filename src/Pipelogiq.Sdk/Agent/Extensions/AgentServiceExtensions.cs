using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PipelogiqSDK.Agent.Configuration;
using PipelogiqSDK.Agent.Handlers;
using PipelogiqSDK.Agent.Models;
using PipelogiqSDK.Agent.OpenApi;
using PipelogiqSDK.Agent.Services;

namespace PipelogiqSDK.Agent.Extensions;

/// <summary>
/// Dependency injection extensions for the AI agent.
/// </summary>
public static class AgentServiceExtensions
{
    /// <summary>
    /// Registers AI agent services. Call this after AddPipelogiq().
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configure">Action to configure AgentOptions.</param>
    /// <returns>AgentBuilder for chaining AddTool() calls.</returns>
    public static AgentBuilder AddPipelogiqAgent(
        this IServiceCollection services,
        Action<AgentOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = GetOrAddAgentOptions(services);
        configure(options);

        EnsureAgentServicesRegistered(services, options);

        return new AgentBuilder(options);
    }

    internal static AgentOptions GetOrAddAgentOptions(IServiceCollection services)
    {
        var existing = services
            .LastOrDefault(descriptor => descriptor.ServiceType == typeof(AgentOptions))
            ?.ImplementationInstance as AgentOptions;

        if (existing != null)
            return existing;

        var options = new AgentOptions();
        services.AddSingleton(options);
        return options;
    }

    internal static void EnsureAgentServicesRegistered(IServiceCollection services, AgentOptions options)
    {
        services.TryAddSingleton(options);
        services.TryAddSingleton<IAgentToolRegistry>(sp =>
        {
            var registry = new AgentToolRegistry(options);
            foreach (var (name, handler) in options.NativeHandlers)
                registry.RegisterNativeHandler(name, handler);
            return registry;
        });
        services.TryAddSingleton<IAgentNotificationRouter, AgentNotificationRouter>();
        services.TryAddSingleton<IAgentMemoryStore, InMemoryAgentMemoryStore>();
        services.TryAddSingleton<IAgentSessionStore, InMemoryAgentSessionStore>();
        services.TryAddSingleton<IAgentToolPolicy, AllowAllToolPolicy>();

        // Lifecycle observer: resolved via AgentLifecycleObserverRegistry that collects
        // all observers added via AddLifecycleObserver(). The registry builds a
        // CompositeLifecycleObserver (or NoOp if none were registered).
        services.TryAddSingleton<AgentLifecycleObserverRegistry>();
        services.TryAddSingleton<IAgentLifecycleObserver>(sp =>
            sp.GetRequiredService<AgentLifecycleObserverRegistry>().Build(sp));

        services.TryAddTransient<AgentOrchestratorHandler>();
        services.TryAddTransient<AgentToolHandler>();
        services.TryAddTransient<AgentConfirmationHandler>();
        services.TryAddTransient<AgentResponderHandler>();
        services.TryAddTransient<AgentThinkHandler>();
        services.TryAddTransient<AgentCriticHandler>();

        // Critic implementations: both registered so the resolver can pick by provider
        services.TryAddSingleton<OpenAiCritic>();
        services.TryAddSingleton<ClaudeCritic>();
        services.TryAddSingleton<IAgentCriticResolver, DefaultAgentCriticResolver>();

        services.AddHttpClient("pipelogiq-agent-llm");
        services.AddHttpClient("pipelogiq-agent-api", client =>
        {
            if (!string.IsNullOrWhiteSpace(options.TargetApiBaseUrl))
                client.BaseAddress = new Uri(options.TargetApiBaseUrl.TrimEnd('/') + "/");
        });

        if (ShouldRegisterBuiltInPlanner(options))
            services.TryAddSingleton<ILlmPlanner, ClaudeLlmPlanner>();
    }

    private static bool ShouldRegisterBuiltInPlanner(AgentOptions options)
    {
        return options.LlmProvider switch
        {
            AgentLlmProvider.Ollama => !string.IsNullOrWhiteSpace(options.LlmModel),
            _ => !string.IsNullOrWhiteSpace(options.LlmApiKey),
        };
    }
}

/// <summary>
/// Fluent builder for adding tools after calling AddPipelogiqAgent().
/// </summary>
public class AgentBuilder(AgentOptions options)
{
    /// <summary>
    /// Adds a named target API definition that tools can reference.
    /// </summary>
    public AgentBuilder AddTargetApi(AgentTargetApiDefinition targetApi)
    {
        ArgumentNullException.ThrowIfNull(targetApi);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetApi.Name);

        options.TargetApis[targetApi.Name] = targetApi;
        return this;
    }

    /// <summary>
    /// Adds multiple named target API definitions.
    /// </summary>
    public AgentBuilder AddTargetApis(IEnumerable<AgentTargetApiDefinition> targetApis)
    {
        ArgumentNullException.ThrowIfNull(targetApis);

        foreach (var targetApi in targetApis)
            AddTargetApi(targetApi);

        return this;
    }

    /// <summary>
    /// Adds a tool definition that the AI agent can call.
    /// </summary>
    public AgentBuilder AddTool(AgentToolDefinition tool)
    {
        options.Tools.Add(tool);
        return this;
    }

    /// <summary>
    /// Adds multiple tool definitions at once.
    /// </summary>
    public AgentBuilder AddTools(IEnumerable<AgentToolDefinition> tools)
    {
        options.Tools.AddRange(tools);
        return this;
    }

    /// <summary>
    /// Registers a native tool handler that executes .NET code instead of making an HTTP call.
    /// </summary>
    /// <param name="definition">Tool metadata shown to the LLM (name, description, parameters).</param>
    /// <param name="handler">The handler instance that contains the tool logic.</param>
    public AgentBuilder AddNativeTool(AgentToolDefinition definition, IAgentToolHandler handler)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Name);

        options.Tools.Add(definition);
        options.NativeHandlers[definition.Name] = handler;
        return this;
    }

    /// <summary>
    /// Registers a lifecycle observer that receives tool, approval, session, and budget events.
    /// Multiple observers can be registered — they all receive every event via a composite.
    /// </summary>
    /// <typeparam name="TObserver">Observer implementation registered as a singleton.</typeparam>
    public AgentBuilder AddLifecycleObserver<TObserver>(IServiceCollection services)
        where TObserver : class, IAgentLifecycleObserver
    {
        services.AddSingleton<TObserver>();
        AgentLifecycleObserverRegistry.AddFactory(services,
            sp => sp.GetRequiredService<TObserver>());
        return this;
    }

    /// <summary>
    /// Registers a lifecycle observer instance.
    /// Multiple observers can be registered — they all receive every event via a composite.
    /// </summary>
    public AgentBuilder AddLifecycleObserver(IServiceCollection services, IAgentLifecycleObserver observer)
    {
        AgentLifecycleObserverRegistry.AddFactory(services, _ => observer);
        return this;
    }

    /// <summary>
    /// Registers a custom tool policy that controls which tools can be executed.
    /// Replaces the default <see cref="AllowAllToolPolicy"/>.
    /// </summary>
    /// <typeparam name="TPolicy">Policy implementation type registered as a singleton.</typeparam>
    /// <param name="services">The same service collection passed to AddPipelogiqAgent().</param>
    public AgentBuilder UseToolPolicy<TPolicy>(IServiceCollection services)
        where TPolicy : class, IAgentToolPolicy
    {
        services.AddSingleton<IAgentToolPolicy, TPolicy>();
        return this;
    }

    /// <summary>
    /// Registers a custom tool policy instance.
    /// </summary>
    public AgentBuilder UseToolPolicy(IServiceCollection services, IAgentToolPolicy policy)
    {
        services.AddSingleton(policy);
        return this;
    }

    /// <summary>
    /// Loads tool definitions from an OpenAPI / Swagger spec and adds them to the agent.
    /// Supports OpenAPI 3.x and Swagger 2.x JSON specs from a URL or local file path.
    /// </summary>
    /// <param name="specUrlOrPath">
    /// URL (https://api.example.com/swagger/v1/swagger.json) or local file path to the spec.
    /// </param>
    /// <param name="configure">Optional filter and behavior configuration.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>This builder for chaining.</returns>
    /// <example>
    /// await builder.UseOpenApiSpecAsync(
    ///     "https://api.clinic.com/swagger/v1/swagger.json",
    ///     config =>
    ///     {
    ///         config.IncludeOperations = ["getPersons", "getPlanTimes", "updatePlanTime"];
    ///         config.ExcludePathPrefixes = ["/api/admin"];
    ///     });
    /// </example>
    public async Task<AgentBuilder> UseOpenApiSpecAsync(
        string specUrlOrPath,
        Action<OpenApiLoadOptions>? configure = null,
        CancellationToken ct = default)
    {
        var loadOptions = new OpenApiLoadOptions();
        configure?.Invoke(loadOptions);

        var tools = await OpenApiToolLoader.LoadAsync(specUrlOrPath, loadOptions, ct: ct);
        options.Tools.AddRange(tools);

        return this;
    }
}
