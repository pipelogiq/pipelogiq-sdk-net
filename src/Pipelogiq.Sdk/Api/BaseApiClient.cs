using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;

namespace PipelogiqSDK.Api;

/// <summary>
/// Base HTTP API client with JSON serialization helpers.
/// </summary>
public abstract class BaseApiClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    /// <summary>
    /// Initializes a new API client instance.
    /// </summary>
    /// <param name="baseUrl">Base API URL.</param>
    /// <param name="apiKey">Optional API key.</param>
    /// <param name="handler">Optional HTTP message handler.</param>
    protected BaseApiClient(
        string baseUrl,
        string? apiKey = null,
        HttpMessageHandler? handler = null,
        bool allowInsecureServerCertificate = false)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _httpClient = new HttpClient(handler ?? CreateDefaultHandler(allowInsecureServerCertificate));

        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("apikey", apiKey);
            _httpClient.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        }
    }

    private static HttpMessageHandler CreateDefaultHandler(bool allowInsecureServerCertificate)
    {
        var handler = new HttpClientHandler();
        if (allowInsecureServerCertificate)
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        return handler;
    }

    /// <summary>
    /// Sets bearer token for outgoing requests.
    /// </summary>
    /// <param name="token">Bearer token value.</param>
    protected void SetBearerToken(string token)
    {
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>
    /// Sends GET request and deserializes JSON response.
    /// </summary>
    /// <typeparam name="T">Target response type.</typeparam>
    /// <param name="requestUri">Relative request URI.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="headers">Optional request headers.</param>
    /// <returns>Deserialized response body.</returns>
    protected async Task<T> GetAsync<T>(
        string requestUri,
        CancellationToken ct = default,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        using var request = BuildRequest(HttpMethod.Get, requestUri, content: null, headers);
        using var response = await _httpClient.SendAsync(request, ct);
        await EnsureSuccess(response);
        var responseData = await response.Content.ReadAsStringAsync(ct);
        return JsonConvert.DeserializeObject<T>(responseData)!;
    }

    /// <summary>
    /// Sends POST request with JSON payload and deserializes JSON response.
    /// </summary>
    /// <typeparam name="T">Target response type.</typeparam>
    /// <param name="requestUri">Relative request URI.</param>
    /// <param name="content">Payload object.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="headers">Optional request headers.</param>
    /// <returns>Deserialized response body.</returns>
    protected async Task<T> PostAsync<T>(
        string requestUri,
        object content,
        CancellationToken ct = default,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        var payload = new StringContent(SdkJsonSerializer.Serialize(content), Encoding.UTF8, "application/json");
        using var request = BuildRequest(HttpMethod.Post, requestUri, payload, headers);
        using var response = await _httpClient.SendAsync(request, ct);
        await EnsureSuccess(response);
        var responseData = await response.Content.ReadAsStringAsync(ct);
        return JsonConvert.DeserializeObject<T>(responseData)!;
    }

    /// <summary>
    /// Sends POST request with JSON payload.
    /// </summary>
    /// <param name="requestUri">Relative request URI.</param>
    /// <param name="content">Payload object.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="headers">Optional request headers.</param>
    protected async Task PostAsync(
        string requestUri,
        object content,
        CancellationToken ct = default,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        var payload = new StringContent(SdkJsonSerializer.Serialize(content), Encoding.UTF8, "application/json");
        using var request = BuildRequest(HttpMethod.Post, requestUri, payload, headers);
        using var response = await _httpClient.SendAsync(request, ct);
        await EnsureSuccess(response);
    }

    /// <summary>
    /// Sends PUT request with JSON payload and deserializes JSON response.
    /// </summary>
    /// <typeparam name="T">Target response type.</typeparam>
    /// <param name="requestUri">Relative request URI.</param>
    /// <param name="content">Payload object.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="headers">Optional request headers.</param>
    /// <returns>Deserialized response body.</returns>
    protected async Task<T> PutAsync<T>(
        string requestUri,
        object content,
        CancellationToken ct = default,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        var payload = new StringContent(SdkJsonSerializer.Serialize(content), Encoding.UTF8, "application/json");
        using var request = BuildRequest(HttpMethod.Put, requestUri, payload, headers);
        using var response = await _httpClient.SendAsync(request, ct);
        await EnsureSuccess(response);
        var responseData = await response.Content.ReadAsStringAsync(ct);
        return JsonConvert.DeserializeObject<T>(responseData)!;
    }

    /// <summary>
    /// Sends DELETE request.
    /// </summary>
    /// <param name="requestUri">Relative request URI.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="headers">Optional request headers.</param>
    protected async Task DeleteAsync(
        string requestUri,
        CancellationToken ct = default,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        using var request = BuildRequest(HttpMethod.Delete, requestUri, content: null, headers);
        using var response = await _httpClient.SendAsync(request, ct);
        await EnsureSuccess(response);
    }

    private string BuildUrl(string requestUri) => $"{_baseUrl}/{requestUri.TrimStart('/')}";

    private HttpRequestMessage BuildRequest(
        HttpMethod method,
        string requestUri,
        HttpContent? content,
        IReadOnlyDictionary<string, string>? headers)
    {
        var request = new HttpRequestMessage(method, BuildUrl(requestUri));
        if (content is not null)
            request.Content = content;

        if (headers is null)
            return request;

        foreach (var (key, value) in headers)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            if (!request.Headers.TryAddWithoutValidation(key, value))
                request.Content?.Headers.TryAddWithoutValidation(key, value);
        }

        return request;
    }

    private static async Task EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        var headers = new StringBuilder();

        foreach (var header in response.Headers)
        {
            headers.AppendLine($"{header.Key}: {string.Join(", ", header.Value)}");
        }

        foreach (var header in response.Content.Headers)
        {
            headers.AppendLine($"{header.Key}: {string.Join(", ", header.Value)}");
        }

        var responseData = await response.Content.ReadAsStringAsync();

        var errorMessage = $"""
Request failed with status code {(int)response.StatusCode} ({response.StatusCode})
Headers:
{headers}
Body:
{responseData}
""";

        throw new HttpRequestException(errorMessage, null, response.StatusCode);
    }
}
