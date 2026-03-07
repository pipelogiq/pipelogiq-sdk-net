using Microsoft.Extensions.DependencyInjection;
using PipelogiqSDK.Configuration;
using PipelogiqSDK.Execution;
using PipelogiqSDK.Runner;

namespace PipelogiqSDK.Api;

/// <summary>
/// Dependency injection registration helpers for Pipelogiq SDK.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Pipelogiq services in dependency injection container.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="runnerOptions">Runner options.</param>
    /// <returns>Updated service collection.</returns>
    public static IServiceCollection AddPipelogiq(this IServiceCollection services, PipelogiqRunnerOptions runnerOptions)
    {
        services.AddSingleton(runnerOptions);
        services.AddSingleton<PipelogiqApiClient>();
        services.AddTransient<StageExecutor>();
        services.AddTransient<PipelineRunner>();

        if (string.IsNullOrWhiteSpace(runnerOptions.ApiKey))
        {
            throw new ArgumentNullException(nameof(runnerOptions.ApiKey));
        }

        if (string.IsNullOrWhiteSpace(runnerOptions.ApiUrl))
        {
            throw new ArgumentNullException(nameof(runnerOptions.ApiUrl));
        }

        GlobalRunnerContext.Options = runnerOptions;

        return services;
    }

    /// <summary>
    /// Stores global bearer token used by SDK builders and API client.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="token">Bearer token.</param>
    /// <returns>Updated service collection.</returns>
    public static IServiceCollection AddPipelogiqToken(this IServiceCollection services, string token)
    {
        GlobalRunnerContext.Token = token;
        return services;
    }
}
