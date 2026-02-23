using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;

namespace PipelogiqSDK.Api;

public abstract class BaseApiClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    protected BaseApiClient(string baseUrl, string? apiKey = null, HttpMessageHandler? handler = null)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _httpClient = new HttpClient(handler ?? new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        });

        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("apikey", apiKey);
            _httpClient.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        }
    }

    protected void SetBearerToken(string token)
    {
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

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

    protected async Task<T> PostAsync<T>(
        string requestUri,
        object content,
        CancellationToken ct = default,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        var payload = new StringContent(JsonConvert.SerializeObject(content), Encoding.UTF8, "application/json");
        using var request = BuildRequest(HttpMethod.Post, requestUri, payload, headers);
        using var response = await _httpClient.SendAsync(request, ct);
        await EnsureSuccess(response);
        var responseData = await response.Content.ReadAsStringAsync(ct);
        return JsonConvert.DeserializeObject<T>(responseData)!;
    }

    protected async Task PostAsync(
        string requestUri,
        object content,
        CancellationToken ct = default,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        var payload = new StringContent(JsonConvert.SerializeObject(content), Encoding.UTF8, "application/json");
        using var request = BuildRequest(HttpMethod.Post, requestUri, payload, headers);
        using var response = await _httpClient.SendAsync(request, ct);
        await EnsureSuccess(response);
    }

    protected async Task<T> PutAsync<T>(
        string requestUri,
        object content,
        CancellationToken ct = default,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        var payload = new StringContent(JsonConvert.SerializeObject(content), Encoding.UTF8, "application/json");
        using var request = BuildRequest(HttpMethod.Put, requestUri, payload, headers);
        using var response = await _httpClient.SendAsync(request, ct);
        await EnsureSuccess(response);
        var responseData = await response.Content.ReadAsStringAsync(ct);
        return JsonConvert.DeserializeObject<T>(responseData)!;
    }

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
