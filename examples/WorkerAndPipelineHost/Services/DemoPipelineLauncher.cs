using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PipelogiqSDK.Api;
using PipelogiqSDK.Builders;
using PipelogiqSDK.Configuration;
using Pipelogiq.Sdk.Examples.WorkerAndPipelineHost.Configuration;
using Pipelogiq.Sdk.Examples.WorkerAndPipelineHost.Handlers;
using Pipelogiq.Sdk.Examples.WorkerAndPipelineHost.Models;

namespace Pipelogiq.Sdk.Examples.WorkerAndPipelineHost.Services;

internal sealed class DemoPipelineLauncher(
    PipelogiqRunnerOptions runnerOptions,
    ExampleSettings settings,
    ILogger<DemoPipelineLauncher> logger)
{
    public async Task SubmitCheckoutPipelineAsync(CancellationToken cancellationToken)
    {
        using var activity = ExampleTelemetry.ActivitySource.StartActivity(
            "examples.checkout.submit",
            ActivityKind.Producer);

        var orderId = $"order-{Guid.NewGuid():N}";
        var customerId = $"cust-{Guid.NewGuid():N}";

        var builder = PipelineBuilder.Create(settings.PipelineName, runnerOptions)
            .WithAction<FraudCheckHandler>(
                stageName: "fraud-check",
                input: new FraudCheckInput(orderId, customerId, Amount: 149.99m, Currency: "USD"))
            .WithAction<ChargeCustomerHandler>(
                stageName: "charge-customer",
                input: new ChargeCustomerInput(orderId, PaymentToken: "tok_demo_visa", Amount: 149.99m, Currency: "USD"))
            .WithAction<ReceiptHandler>(
                stageName: "send-receipt",
                input: null)
            .AddKeyword("example", "worker-and-pipeline-host")
            .AddKeyword("flow", "checkout")
            .AddContextItem("orderId", orderId)
            .AddContextItem("customerId", customerId)
            .AddContextItem("customerEmail", "demo.customer@example.com");

        logger.LogInformation(
            "Submitting demo pipeline '{PipelineName}' for order {OrderId} to {ApiUrl}.",
            settings.PipelineName,
            orderId,
            settings.ApiUrl);

        await PipelineService.StartPipelineAsync(builder, cancellationToken);

        logger.LogInformation("Demo pipeline submitted successfully.");
    }
}
