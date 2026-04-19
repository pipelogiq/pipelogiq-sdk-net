using System.Net;

namespace PipelogiqSDK.Agent.Services;

internal static class AgentLlmHttpErrorClassifier
{
    internal const string InvalidRequestErrorCode = "LLM_INVALID_REQUEST";

    internal static bool IsRateLimit(HttpRequestException ex)
        => ex.StatusCode == HttpStatusCode.TooManyRequests;

    internal static bool IsInvalidRequest(HttpRequestException ex)
        => ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity;
}
