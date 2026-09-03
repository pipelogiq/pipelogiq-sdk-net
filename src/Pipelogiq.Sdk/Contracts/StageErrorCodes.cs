namespace PipelogiqSDK.Contracts;

/// <summary>
/// Well-known structured stage error codes.
/// </summary>
public static class StageErrorCodes
{
    public const string Timeout = "TIMEOUT";
    public const string UpstreamError = "UPSTREAM_ERROR";
    public const string RateLimitExceeded = "RATE_LIMIT_EXCEEDED";
    public const string TransportUnavailable = "TRANSPORT_UNAVAILABLE";
    public const string BusinessRejected = "BUSINESS_REJECTED";
    public const string ValidationError = "VALIDATION_ERROR";
    public const string InvalidState = "INVALID_STATE";
    public const string MissingRequiredData = "MISSING_REQUIRED_DATA";
}
