using Microsoft.Extensions.DependencyInjection;

namespace PipelogiqSDK.Agent.Services;

/// <summary>
/// Collects lifecycle observer factory functions during service registration
/// and builds the final <see cref="IAgentLifecycleObserver"/> at resolution time.
///
/// This avoids the recursive resolution problem with GetServices&lt;IAgentLifecycleObserver&gt;
/// by storing factories separately from the DI container's IAgentLifecycleObserver registrations.
/// </summary>
internal sealed class AgentLifecycleObserverRegistry
{
    private readonly List<Func<IServiceProvider, IAgentLifecycleObserver>> _factories = [];

    /// <summary>
    /// Adds an observer factory to the registry via the service collection.
    /// Called during AddLifecycleObserver() at registration time.
    /// </summary>
    internal static void AddFactory(
        IServiceCollection services,
        Func<IServiceProvider, IAgentLifecycleObserver> factory)
    {
        // Ensure the registry singleton exists before adding the factory.
        // We use a Configure-style pattern: register a post-configure action
        // that appends to the registry once it's resolved.
        services.AddSingleton<Action<AgentLifecycleObserverRegistry>>(registry =>
            registry._factories.Add(factory));
    }

    /// <summary>
    /// Builds the final observer. Called once when <see cref="IAgentLifecycleObserver"/>
    /// is resolved from the container.
    /// </summary>
    internal IAgentLifecycleObserver Build(IServiceProvider sp)
    {
        // Run all registered factory-adders to populate _factories
        foreach (var adder in sp.GetServices<Action<AgentLifecycleObserverRegistry>>())
            adder(this);

        return _factories.Count switch
        {
            0 => new NoOpLifecycleObserver(),
            1 => _factories[0](sp),
            _ => new CompositeLifecycleObserver(_factories.Select(f => f(sp))),
        };
    }
}
