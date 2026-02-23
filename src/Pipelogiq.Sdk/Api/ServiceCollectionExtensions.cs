using Microsoft.Extensions.DependencyInjection;
using PipelogiqSDK.Configuration;
using PipelogiqSDK.Execution;
using PipelogiqSDK.Runner;

namespace PipelogiqSDK.Api;

public static class ServiceCollectionExtensions
{
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

    public static IServiceCollection AddPipelogiqToken(this IServiceCollection services, string token)
    {
        GlobalRunnerContext.Token = token;
        return services;
    }
}
