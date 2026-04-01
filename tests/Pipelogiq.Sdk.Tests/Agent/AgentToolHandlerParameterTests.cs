using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PipelogiqSDK.Abstractions;
using PipelogiqSDK.Agent.Configuration;
using PipelogiqSDK.Agent.Extensions;
using PipelogiqSDK.Agent.Handlers;
using PipelogiqSDK.Agent.Models;
using PipelogiqSDK.Agent.Services;
using PipelogiqSDK.Contracts;

using Xunit;

namespace PipelogiqSDK.Tests.Agent;

public sealed class AgentToolHandlerParameterTests
{
    [Fact]
    public async Task ExecuteAsync_WithTypedSchema_CoercesParamsAndBuildsTypedRequestBody()
    {
        var tool = new AgentToolDefinition
        {
            Name = "createOrder",
            Description = "Creates an order.",
            HttpMethod = "POST",
            UrlTemplate = "/api/orders/{orderId}",
            Params = new Dictionary<string, AgentToolParam>(StringComparer.OrdinalIgnoreCase)
            {
                ["orderId"] = new() { In = "path", Type = "integer", Required = true, Description = "Order id." },
                ["dryRun"] = new() { In = "query", Type = "boolean", Required = false, Description = "Dry run flag." },
                ["amount"] = new() { In = "body", Type = "number", Required = true, Description = "Amount." },
                ["count"] = new() { In = "body", Type = "integer", Required = true, Description = "Count." },
                ["startDate"] = new() { In = "body", Type = "string", Format = "date", Required = true, Description = "Date." },
                ["labels"] = new() { In = "body", Type = "array", Required = false, Description = "Labels." },
            }
        };

        var requestRecorder = new RecordingHttpMessageHandler();
        var httpFactory = new StaticHttpClientFactory(new HttpClient(requestRecorder)
        {
            BaseAddress = new Uri("https://api.example.com/")
        });

        var handler = new AgentToolHandler(
            new StaticToolRegistry([tool]),
            new AgentOptions { TargetApiBaseUrl = "https://api.example.com" },
            httpFactory);

        var input = new AgentToolCallInput
        {
            ToolName = "createOrder",
            ResultKey = "createOrder",
            Params = new Dictionary<string, object?>
            {
                ["orderId"] = "42",
                ["dryRun"] = "true",
                ["amount"] = "19.95",
                ["count"] = "3",
                ["startDate"] = "2026-03-09",
                ["labels"] = "[\"vip\",\"eu\"]",
            }
        };

        var result = await handler.ExecuteAsync(input, new StageContext
        {
            PipelineId = 10,
            StageId = 20,
            Payload = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase),
        });

        Assert.True(result.IsSuccess);
        Assert.NotNull(requestRecorder.LastUri);
        Assert.Contains("/api/orders/42", requestRecorder.LastUri, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dryRun=True", requestRecorder.LastUri, StringComparison.OrdinalIgnoreCase);

        Assert.NotNull(requestRecorder.LastBody);
        using var body = JsonDocument.Parse(requestRecorder.LastBody!);
        var root = body.RootElement;

        Assert.Equal(JsonValueKind.Number, root.GetProperty("amount").ValueKind);
        Assert.Equal(19.95d, root.GetProperty("amount").GetDouble(), 3);

        Assert.Equal(JsonValueKind.Number, root.GetProperty("count").ValueKind);
        Assert.Equal(3, root.GetProperty("count").GetInt32());

        Assert.Equal(JsonValueKind.String, root.GetProperty("startDate").ValueKind);
        Assert.Equal("2026-03-09", root.GetProperty("startDate").GetString());

        Assert.Equal(JsonValueKind.Array, root.GetProperty("labels").ValueKind);
        Assert.Equal(2, root.GetProperty("labels").GetArrayLength());
    }

    [Fact]
    public async Task ExecuteAsync_WithMissingOrUnknownParams_ThrowsValidationError()
    {
        var tool = new AgentToolDefinition
        {
            Name = "createOrder",
            Description = "Creates an order.",
            HttpMethod = "POST",
            UrlTemplate = "/api/orders/{orderId}",
            Params = new Dictionary<string, AgentToolParam>(StringComparer.OrdinalIgnoreCase)
            {
                ["orderId"] = new() { In = "path", Type = "integer", Required = true, Description = "Order id." },
                ["amount"] = new() { In = "body", Type = "number", Required = true, Description = "Amount." },
            }
        };

        var handler = new AgentToolHandler(
            new StaticToolRegistry([tool]),
            new AgentOptions { TargetApiBaseUrl = "https://api.example.com" },
            new StaticHttpClientFactory(new HttpClient(new RecordingHttpMessageHandler())));

        var missingRequiredInput = new AgentToolCallInput
        {
            ToolName = "createOrder",
            ResultKey = "createOrder",
            Params = new Dictionary<string, object?>
            {
                ["orderId"] = "42",
            }
        };

        var unknownParamInput = new AgentToolCallInput
        {
            ToolName = "createOrder",
            ResultKey = "createOrder",
            Params = new Dictionary<string, object?>
            {
                ["orderId"] = "42",
                ["amount"] = "10.50",
                ["unexpected"] = "value",
            }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.ExecuteAsync(missingRequiredInput, new StageContext()));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.ExecuteAsync(unknownParamInput, new StageContext()));
    }

    [Fact]
    public async Task ExecuteAsync_WithNamedTargetApis_UsesPerApiBaseUrlAndHeadersFromContext()
    {
        var lookupCustomer = new AgentToolDefinition
        {
            Name = "lookupCustomer",
            Description = "Gets customer by id.",
            HttpMethod = "GET",
            TargetApiName = "crm",
            UrlTemplate = "/api/customers/{id}",
            Params = new Dictionary<string, AgentToolParam>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = new() { In = "path", Type = "integer", Required = true, Description = "Customer id." },
            }
        };

        var chargeInvoice = new AgentToolDefinition
        {
            Name = "chargeInvoice",
            Description = "Charges invoice by id.",
            HttpMethod = "POST",
            TargetApiName = "billing",
            UrlTemplate = "/api/invoices/{id}/charge",
            Params = new Dictionary<string, AgentToolParam>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = new() { In = "path", Type = "integer", Required = true, Description = "Invoice id." },
            }
        };

        var services = new ServiceCollection();
        var builder = services.AddPipelogiqAgent(_ => { });
        builder.AddTargetApis([
            new AgentTargetApiDefinition
            {
                Name = "crm",
                BaseUrl = "https://crm.example.com",
                HeaderTemplates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["X-Tenant-Id"] = "{{context:tenantId}}",
                },
                Authentication = new AgentAuthHeaderDefinition
                {
                    HeaderName = "Authorization",
                    Scheme = "Bearer",
                    ValueTemplate = "{{context:userAccessToken}}",
                }
            },
            new AgentTargetApiDefinition
            {
                Name = "billing",
                BaseUrl = "https://billing.example.com",
                StaticHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["X-Api-Key"] = "billing-key",
                },
                Authentication = new AgentAuthHeaderDefinition
                {
                    HeaderName = "X-User-Token",
                    Scheme = null,
                    ValueTemplate = "{{context:userAccessToken}}",
                }
            }
        ]);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<AgentOptions>();
        var requestRecorder = new RecordingHttpMessageHandler();

        var handler = new AgentToolHandler(
            new StaticToolRegistry([lookupCustomer, chargeInvoice]),
            options,
            new StaticHttpClientFactory(new HttpClient(requestRecorder)));

        var context = new StageContext
        {
            Payload = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["tenantId"] = "tenant-42",
                ["userAccessToken"] = "user-token-123",
            }
        };

        await handler.ExecuteAsync(new AgentToolCallInput
        {
            ToolName = "lookupCustomer",
            ResultKey = "lookupCustomer",
            Params = new Dictionary<string, object?> { ["id"] = 7 }
        }, context);

        await handler.ExecuteAsync(new AgentToolCallInput
        {
            ToolName = "chargeInvoice",
            ResultKey = "chargeInvoice",
            Params = new Dictionary<string, object?> { ["id"] = 99 }
        }, context);

        Assert.Equal(2, requestRecorder.Requests.Count);

        Assert.Equal("https://crm.example.com/api/customers/7", requestRecorder.Requests[0].Uri);
        Assert.Equal("Bearer user-token-123", requestRecorder.Requests[0].Headers["Authorization"]);
        Assert.Equal("tenant-42", requestRecorder.Requests[0].Headers["X-Tenant-Id"]);

        Assert.Equal("https://billing.example.com/api/invoices/99/charge", requestRecorder.Requests[1].Uri);
        Assert.Equal("billing-key", requestRecorder.Requests[1].Headers["X-Api-Key"]);
        Assert.Equal("user-token-123", requestRecorder.Requests[1].Headers["X-User-Token"]);
    }

    [Fact]
    public async Task ExecuteAsync_WithLegacyAgentTargetApiSettings_RemainsBackwardCompatible()
    {
        var tool = new AgentToolDefinition
        {
            Name = "getOrder",
            Description = "Gets order by id.",
            HttpMethod = "GET",
            UrlTemplate = "/api/orders/{id}",
            Params = new Dictionary<string, AgentToolParam>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = new() { In = "path", Type = "integer", Required = true, Description = "Order id." },
            }
        };

        var requestRecorder = new RecordingHttpMessageHandler();
        var handler = new AgentToolHandler(
            new StaticToolRegistry([tool]),
            new AgentOptions
            {
                TargetApiBaseUrl = "https://legacy.example.com",
                TargetApiBearerToken = "legacy-token",
                TargetApiHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["X-Tenant-Id"] = "legacy-tenant",
                }
            },
            new StaticHttpClientFactory(new HttpClient(requestRecorder)));

        await handler.ExecuteAsync(new AgentToolCallInput
        {
            ToolName = "getOrder",
            ResultKey = "getOrder",
            Params = new Dictionary<string, object?> { ["id"] = 42 }
        }, new StageContext());

        var request = Assert.Single(requestRecorder.Requests);
        Assert.Equal("https://legacy.example.com/api/orders/42", request.Uri);
        Assert.Equal("Bearer legacy-token", request.Headers["Authorization"]);
        Assert.Equal("legacy-tenant", request.Headers["X-Tenant-Id"]);
    }

    [Fact]
    public async Task ExecuteAsync_WithMissingContextHeaderValue_Throws()
    {
        var tool = new AgentToolDefinition
        {
            Name = "lookupCustomer",
            Description = "Gets customer by id.",
            HttpMethod = "GET",
            BaseUrl = "https://crm.example.com",
            UrlTemplate = "/api/customers/{id}",
            HeaderTemplates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["X-Tenant-Id"] = "{{context:tenantId}}",
            },
            Params = new Dictionary<string, AgentToolParam>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = new() { In = "path", Type = "integer", Required = true, Description = "Customer id." },
            }
        };

        var handler = new AgentToolHandler(
            new StaticToolRegistry([tool]),
            new AgentOptions(),
            new StaticHttpClientFactory(new HttpClient(new RecordingHttpMessageHandler())));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.ExecuteAsync(new AgentToolCallInput
            {
                ToolName = "lookupCustomer",
                ResultKey = "lookupCustomer",
                Params = new Dictionary<string, object?> { ["id"] = 5 }
            }, new StageContext()));
    }

    private sealed class StaticToolRegistry(IReadOnlyList<AgentToolDefinition> tools) : IAgentToolRegistry
    {
        public IReadOnlyList<AgentToolDefinition> GetAll() => tools;

        public AgentToolDefinition? Find(string name) =>
            tools.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

        public IAgentToolHandler? FindNativeHandler(string name) => null;
    }

    private sealed class StaticHttpClientFactory(HttpClient httpClient) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => httpClient;
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = new();

        public string? LastUri => Requests.LastOrDefault()?.Uri;

        public string? LastBody => Requests.LastOrDefault()?.Body;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var headers = request.Headers
                .Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
                .ToDictionary(
                    header => header.Key,
                    header => string.Join(", ", header.Value),
                    StringComparer.OrdinalIgnoreCase);

            Requests.Add(new CapturedRequest(
                request.RequestUri?.ToString(),
                body,
                headers));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true}", Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed record CapturedRequest(
        string? Uri,
        string? Body,
        Dictionary<string, string> Headers);
}
