using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Net.Mail;
using PipelogiqSDK.Abstractions;
using PipelogiqSDK.Agent.Configuration;
using PipelogiqSDK.Agent.Models;
using PipelogiqSDK.Agent.Services;
using PipelogiqSDK.Contracts;
using PipelogiqSDK.StageHelper;

namespace PipelogiqSDK.Agent.Handlers;

/// <summary>
/// Built-in handler that executes a single API tool call.
/// Resolves {{ref:key.path}} references from previous tool results in context.
/// </summary>
public class AgentToolHandler(
    IAgentToolRegistry toolRegistry,
    AgentOptions agentOptions,
    IHttpClientFactory httpClientFactory) : IStageHandler<AgentToolCallInput>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Regex HeaderTemplateRegex =
        new(@"\{\{(?<type>context|payload|ref):(?<path>.+?)\}\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <inheritdoc />
    public async Task<IStageResult> ExecuteAsync(AgentToolCallInput input, IStageContext? context = null)
    {
        var toolDef = toolRegistry.Find(input.ToolName)
            ?? throw new InvalidOperationException($"Tool '{input.ToolName}' is not registered.");

        // Resolve {{ref:...}} references in params
        var resolvedParams = ResolveParams(input.Params, context);
        var validatedParams = ValidateAndCoerceParams(toolDef, resolvedParams);

        // Execute the HTTP call
        var result = await ExecuteToolCallAsync(toolDef, validatedParams, input.ResultKey, context, default);

        // Store result in context
        context ??= new StageContext();
        context.Payload ??= new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        context.Payload[$"agent:result:{input.ResultKey}"] = result.ResponseBody ?? string.Empty;

        // Append to overall tool results list
        var toolResults = GetOrCreateToolResults(context);
        toolResults.Add(result);
        context.Payload[AgentConstants.ToolResults] = toolResults;

        // Record tool result in ReAct conversation history (no-op in plan-and-execute mode)
        var history = GetOrCreateHistory(context);
        history.Add(new AgentConversationTurn
        {
            Type = "tool_result",
            ToolName = input.ToolName,
            Content = result.ResponseBody,
        });
        context.Payload[AgentConstants.ConversationHistory] = history;

        if (!result.IsSuccess)
            return StageResult.Error($"Tool '{input.ToolName}' failed with HTTP {result.StatusCode}: {result.ResponseBody}");

        return StageResult.Success($"Tool '{input.ToolName}' executed. HTTP {result.StatusCode}.");
    }

    private async Task<AgentToolResult> ExecuteToolCallAsync(
        AgentToolDefinition tool,
        Dictionary<string, object?> validatedParams,
        string resultKey,
        IStageContext? context,
        CancellationToken ct)
    {
        var http = httpClientFactory.CreateClient("pipelogiq-agent-api");
        var requestConfig = ResolveRequestConfiguration(tool);
        var url = BuildUrl(tool, validatedParams, requestConfig.BaseUrl, requestConfig.AllowRelativeUrlFallback);
        var pathParams = GetPathParamNames(tool);
        var queryParams = GetQueryParamNames(tool);
        var bodyParams = GetBodyParamNames(tool, pathParams, queryParams);
        var hasSchema = tool.Params?.Count > 0;

        HttpResponseMessage response;

        if (tool.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) ||
            tool.HttpMethod.Equals("DELETE", StringComparison.OrdinalIgnoreCase))
        {
            using var req = new HttpRequestMessage(new HttpMethod(tool.HttpMethod.ToUpperInvariant()), url);
            AddHeaders(req, tool, requestConfig, context);
            response = await http.SendAsync(req, ct);
        }
        else
        {
            // POST/PATCH/PUT — build body from params mapped to body.
            // If the schema has no explicit body params, fall back to legacy non-path/non-query behavior.
            var payloadParams = validatedParams
                .Where(kv =>
                    bodyParams.Contains(kv.Key, StringComparer.OrdinalIgnoreCase) ||
                    (!hasSchema &&
                     !pathParams.Contains(kv.Key, StringComparer.OrdinalIgnoreCase) &&
                     !queryParams.Contains(kv.Key, StringComparer.OrdinalIgnoreCase)))
                .ToDictionary(kv => kv.Key, kv => kv.Value);

            using var req = new HttpRequestMessage(new HttpMethod(tool.HttpMethod.ToUpperInvariant()), url);
            req.Content = new StringContent(
                JsonSerializer.Serialize(payloadParams),
                Encoding.UTF8, "application/json");
            AddHeaders(req, tool, requestConfig, context);
            response = await http.SendAsync(req, ct);
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        return new AgentToolResult
        {
            ToolName = tool.Name,
            ResultKey = resultKey,
            ResponseBody = body,
            StatusCode = (int)response.StatusCode,
            IsSuccess = response.IsSuccessStatusCode,
            IsMutating = tool.IsEffectivelyMutating,
        };
    }

    private static string BuildUrl(
        AgentToolDefinition tool,
        Dictionary<string, object?> validatedParams,
        string? baseUrl,
        bool allowRelativeUrlFallback)
    {
        var url = tool.UrlTemplate;
        var pathParams = GetPathParamNames(tool);
        var queryParams = GetQueryParamNames(tool);
        var hasSchema = tool.Params?.Count > 0;

        // Replace path parameters
        foreach (var kv in validatedParams.Where(kv => pathParams.Contains(kv.Key, StringComparer.OrdinalIgnoreCase)))
        {
            var placeholder = $"{{{kv.Key}}}";
            if (url.Contains(placeholder, StringComparison.OrdinalIgnoreCase))
                url = url.Replace(placeholder, Uri.EscapeDataString(kv.Value?.ToString() ?? ""), StringComparison.OrdinalIgnoreCase);
        }

        // Query params can exist for any HTTP method; include only mapped query params.
        var queryPairs = validatedParams
            .Where(kv =>
                (queryParams.Contains(kv.Key, StringComparer.OrdinalIgnoreCase) ||
                 (!hasSchema &&
                  (tool.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) ||
                   tool.HttpMethod.Equals("DELETE", StringComparison.OrdinalIgnoreCase)) &&
                  !pathParams.Contains(kv.Key, StringComparer.OrdinalIgnoreCase))) &&
                         kv.Value != null &&
                         !string.IsNullOrWhiteSpace(Convert.ToString(kv.Value, CultureInfo.InvariantCulture)))
            .Select(kv =>
                $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(Convert.ToString(kv.Value, CultureInfo.InvariantCulture) ?? string.Empty)}")
            .ToList();

        if (queryPairs.Count > 0)
            url += "?" + string.Join("&", queryPairs);

        if (Uri.TryCreate(url, UriKind.Absolute, out var absoluteUri) &&
            !string.Equals(absoluteUri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase))
            return absoluteUri.ToString();

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            if (!allowRelativeUrlFallback)
            {
                throw new InvalidOperationException(
                    $"Tool '{tool.Name}' must define an absolute UrlTemplate or a BaseUrl/TargetApiName base URL.");
            }

            return url;
        }

        if (!Uri.TryCreate(baseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri))
            throw new InvalidOperationException($"Tool '{tool.Name}' has invalid base URL '{baseUrl}'.");

        return new Uri(baseUri, url.TrimStart('/')).ToString();
    }

    private ResolvedRequestConfiguration ResolveRequestConfiguration(AgentToolDefinition tool)
    {
        AgentTargetApiDefinition? targetApi = null;
        if (!string.IsNullOrWhiteSpace(tool.TargetApiName))
        {
            if (!agentOptions.TargetApis.TryGetValue(tool.TargetApiName, out targetApi))
            {
                throw new InvalidOperationException(
                    $"Tool '{tool.Name}' references unknown target API '{tool.TargetApiName}'.");
            }
        }

        var useLegacyDefaults = targetApi == null &&
            string.IsNullOrWhiteSpace(tool.BaseUrl) &&
            tool.Authentication == null &&
            tool.HeaderTemplates == null;

        var staticHeaders = MergeHeaders(targetApi?.StaticHeaders, tool.StaticHeaders);
        var headerTemplates = MergeHeaders(targetApi?.HeaderTemplates, tool.HeaderTemplates);
        var authentication = tool.Authentication ?? targetApi?.Authentication;
        var baseUrl = tool.BaseUrl ?? targetApi?.BaseUrl;

        if (useLegacyDefaults)
        {
            baseUrl ??= agentOptions.TargetApiBaseUrl;
            authentication ??= BuildLegacyAuthentication(agentOptions);
            staticHeaders = MergeHeaders(agentOptions.TargetApiHeaders, staticHeaders);
        }

        return new ResolvedRequestConfiguration
        {
            AllowRelativeUrlFallback = useLegacyDefaults,
            BaseUrl = baseUrl,
            StaticHeaders = staticHeaders,
            HeaderTemplates = headerTemplates,
            Authentication = authentication,
        };
    }

    private static AgentAuthHeaderDefinition? BuildLegacyAuthentication(AgentOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.TargetApiBearerToken))
            return null;

        return new AgentAuthHeaderDefinition
        {
            HeaderName = "Authorization",
            Scheme = "Bearer",
            Value = options.TargetApiBearerToken,
        };
    }

    private static Dictionary<string, string>? MergeHeaders(
        IReadOnlyDictionary<string, string>? primary,
        IReadOnlyDictionary<string, string>? secondary)
    {
        if ((primary == null || primary.Count == 0) && (secondary == null || secondary.Count == 0))
            return null;

        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (primary != null)
        {
            foreach (var (key, value) in primary)
                merged[key] = value;
        }

        if (secondary != null)
        {
            foreach (var (key, value) in secondary)
                merged[key] = value;
        }

        return merged;
    }

    private static void AddHeaders(
        HttpRequestMessage req,
        AgentToolDefinition tool,
        ResolvedRequestConfiguration requestConfig,
        IStageContext? context)
    {
        if (requestConfig.StaticHeaders != null)
        {
            foreach (var (key, value) in requestConfig.StaticHeaders)
                SetHeader(req, key, value);
        }

        AddAuthenticationHeader(req, tool, requestConfig.Authentication, context);

        if (requestConfig.HeaderTemplates == null)
            return;

        foreach (var (key, template) in requestConfig.HeaderTemplates)
        {
            var resolvedValue = ResolveHeaderTemplate(tool.Name, template, context);
            if (!string.IsNullOrWhiteSpace(resolvedValue))
                SetHeader(req, key, resolvedValue);
        }
    }

    private static void AddAuthenticationHeader(
        HttpRequestMessage req,
        AgentToolDefinition tool,
        AgentAuthHeaderDefinition? authentication,
        IStageContext? context)
    {
        if (authentication == null || string.IsNullOrWhiteSpace(authentication.HeaderName))
            return;

        var rawValue = !string.IsNullOrWhiteSpace(authentication.ValueTemplate)
            ? ResolveHeaderTemplate(tool.Name, authentication.ValueTemplate!, context)
            : authentication.Value;

        if (string.IsNullOrWhiteSpace(rawValue))
            return;

        var headerValue = string.IsNullOrWhiteSpace(authentication.Scheme)
            ? rawValue
            : $"{authentication.Scheme} {rawValue}";

        SetHeader(req, authentication.HeaderName, headerValue);
    }

    private static string ResolveHeaderTemplate(string toolName, string template, IStageContext? context)
    {
        if (string.IsNullOrWhiteSpace(template))
            return string.Empty;

        return HeaderTemplateRegex.Replace(template, match =>
        {
            var kind = match.Groups["type"].Value;
            var path = match.Groups["path"].Value.Trim();

            var resolvedValue = kind.ToLowerInvariant() switch
            {
                "context" or "payload" => ResolvePayloadValue(path, context),
                "ref" => ResolveReference(path, context),
                _ => null
            };

            if (resolvedValue == null)
            {
                throw new InvalidOperationException(
                    $"Tool '{toolName}' could not resolve header template value '{match.Value}'.");
            }

            return ConvertResolvedValueToString(resolvedValue);
        });
    }

    private static object? ResolvePayloadValue(string payloadPath, IStageContext? context)
    {
        if (context?.Payload == null || string.IsNullOrWhiteSpace(payloadPath))
            return null;

        var dotIndex = payloadPath.IndexOf('.');
        var bracketIndex = payloadPath.IndexOf('[');
        var splitIndex = dotIndex < 0 ? bracketIndex : bracketIndex < 0 ? dotIndex : Math.Min(dotIndex, bracketIndex);

        var key = splitIndex < 0 ? payloadPath : payloadPath[..splitIndex];
        var jsonPath = splitIndex < 0 ? null : payloadPath[splitIndex..].TrimStart('.');

        if (!context.Payload.TryGetValue(key, out var rawValue))
            return null;

        if (string.IsNullOrWhiteSpace(jsonPath))
            return rawValue;

        try
        {
            var json = rawValue is string s ? s : JsonSerializer.Serialize(rawValue);
            var doc = JsonDocument.Parse(json);
            return NavigateJsonPath(doc.RootElement, jsonPath);
        }
        catch
        {
            return rawValue;
        }
    }

    private static string ConvertResolvedValueToString(object value)
    {
        if (value is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString() ?? string.Empty,
                JsonValueKind.Number => element.GetRawText(),
                JsonValueKind.True => bool.TrueString,
                JsonValueKind.False => bool.FalseString,
                JsonValueKind.Null => string.Empty,
                _ => element.GetRawText()
            };
        }

        return value switch
        {
            string text => text,
            bool boolValue => boolValue ? bool.TrueString : bool.FalseString,
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    private static void SetHeader(HttpRequestMessage req, string key, string value)
    {
        req.Headers.Remove(key);
        req.Content?.Headers.Remove(key);

        if (!req.Headers.TryAddWithoutValidation(key, value))
            req.Content?.Headers.TryAddWithoutValidation(key, value);
    }

    private sealed class ResolvedRequestConfiguration
    {
        public bool AllowRelativeUrlFallback { get; init; }
        public string? BaseUrl { get; init; }
        public Dictionary<string, string>? StaticHeaders { get; init; }
        public Dictionary<string, string>? HeaderTemplates { get; init; }
        public AgentAuthHeaderDefinition? Authentication { get; init; }
    }

    private static HashSet<string> ExtractPathParamNames(string urlTemplate)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(urlTemplate, @"\{(\w+)\}"))
            result.Add(match.Groups[1].Value);
        return result;
    }

    private Dictionary<string, object?> ResolveParams(Dictionary<string, object?> rawParams, IStageContext? context)
    {
        var resolved = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var kv in rawParams)
        {
            if (kv.Value is string value &&
                value.StartsWith("{{ref:", StringComparison.OrdinalIgnoreCase) &&
                value.EndsWith("}}", StringComparison.Ordinal))
            {
                var refPath = value[6..^2]; // strip {{ref: and }}
                resolved[kv.Key] = ResolveReference(refPath, context);
            }
            else
            {
                resolved[kv.Key] = kv.Value;
            }
        }

        return resolved;
    }

    private static Dictionary<string, object?> ValidateAndCoerceParams(
        AgentToolDefinition tool,
        Dictionary<string, object?> resolvedParams)
    {
        var hasSchema = tool.Params?.Count > 0;
        if (!hasSchema)
            return resolvedParams;

        var schema = new Dictionary<string, AgentToolParam>(tool.Params!, StringComparer.OrdinalIgnoreCase);
        var coerced = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, value) in resolvedParams)
        {
            if (!schema.TryGetValue(name, out var paramDef))
                throw new InvalidOperationException($"Tool '{tool.Name}' does not define parameter '{name}'.");

            coerced[name] = CoerceParameterValue(tool, name, value, paramDef);
        }

        foreach (var (name, paramDef) in schema.Where(kv => kv.Value.Required))
        {
            if (!coerced.ContainsKey(name) || coerced[name] == null)
                throw new InvalidOperationException($"Tool '{tool.Name}' requires parameter '{name}'.");
        }

        var missingPathParams = ExtractPathParamNames(tool.UrlTemplate)
            .Where(pathParam =>
                !coerced.ContainsKey(pathParam) || string.IsNullOrWhiteSpace(Convert.ToString(coerced[pathParam], CultureInfo.InvariantCulture)))
            .ToList();

        if (missingPathParams.Count > 0)
            throw new InvalidOperationException(
                $"Tool '{tool.Name}' is missing required path parameter(s): {string.Join(", ", missingPathParams)}.");

        return coerced;
    }

    private static object? CoerceParameterValue(
        AgentToolDefinition tool,
        string paramName,
        object? value,
        AgentToolParam paramDef)
    {
        if (value == null)
            return null;

        object? coerced = (paramDef.Type ?? "string").ToLowerInvariant() switch
        {
            "integer" or "int32" or "int64" => CoerceInteger(tool.Name, paramName, value),
            "number" or "float" or "double" or "decimal" => CoerceNumber(tool.Name, paramName, value),
            "boolean" or "bool" => CoerceBoolean(tool.Name, paramName, value),
            "array" => CoerceArray(tool.Name, paramName, value),
            "object" => CoerceObject(tool.Name, paramName, value),
            _ => CoerceString(tool.Name, paramName, value)
        };

        ValidateFormat(tool.Name, paramName, coerced, paramDef.Format);
        ValidateEnum(tool.Name, paramName, coerced, paramDef.EnumValues);

        return coerced;
    }

    private static string CoerceString(string toolName, string paramName, object value)
    {
        if (value is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.String)
                return element.GetString() ?? string.Empty;

            throw new InvalidOperationException(
                $"Tool '{toolName}' parameter '{paramName}' must be a string.");
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static long CoerceInteger(string toolName, string paramName, object value)
    {
        if (value is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var jsonLong))
                return jsonLong;

            if (element.ValueKind == JsonValueKind.String &&
                long.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedFromString))
                return parsedFromString;
        }

        if (value is long l) return l;
        if (value is int i) return i;
        if (value is short s) return s;
        if (value is byte b) return b;

        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            return parsed;

        throw new InvalidOperationException(
            $"Tool '{toolName}' parameter '{paramName}' must be an integer.");
    }

    private static double CoerceNumber(string toolName, string paramName, object value)
    {
        if (value is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var jsonDouble))
                return jsonDouble;

            if (element.ValueKind == JsonValueKind.String &&
                double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedFromString))
                return parsedFromString;
        }

        if (value is double d) return d;
        if (value is float f) return f;
        if (value is decimal m) return (double)m;
        if (value is int i) return i;
        if (value is long l) return l;

        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            return parsed;

        throw new InvalidOperationException(
            $"Tool '{toolName}' parameter '{paramName}' must be a number.");
    }

    private static bool CoerceBoolean(string toolName, string paramName, object value)
    {
        if (value is JsonElement element)
        {
            if (element.ValueKind is JsonValueKind.True or JsonValueKind.False)
                return element.GetBoolean();

            if (element.ValueKind == JsonValueKind.String &&
                TryParseBoolean(element.GetString(), out var parsedJsonBool))
                return parsedJsonBool;
        }

        if (value is bool b) return b;

        if (TryParseBoolean(Convert.ToString(value, CultureInfo.InvariantCulture), out var parsed))
            return parsed;

        throw new InvalidOperationException(
            $"Tool '{toolName}' parameter '{paramName}' must be a boolean.");
    }

    private static object[] CoerceArray(string toolName, string paramName, object value)
    {
        if (value is JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException(
                    $"Tool '{toolName}' parameter '{paramName}' must be an array.");

            return element.EnumerateArray().Select(GetPrimitive).ToArray()!;
        }

        if (value is IEnumerable<object?> enumerable && value is not string)
            return enumerable.ToArray()!;

        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<object>();

        try
        {
            var parsed = JsonSerializer.Deserialize<object[]>(text);
            if (parsed != null)
                return parsed;
        }
        catch
        {
            // ignored, explicit error below
        }

        throw new InvalidOperationException(
            $"Tool '{toolName}' parameter '{paramName}' must be a JSON array.");
    }

    private static Dictionary<string, object?> CoerceObject(string toolName, string paramName, object value)
    {
        if (value is JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException(
                    $"Tool '{toolName}' parameter '{paramName}' must be an object.");

            var json = element.GetRawText();
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(json, JsonOptions)
                   ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }

        if (value is Dictionary<string, object?> typedDictionary)
            return typedDictionary;

        if (value is IReadOnlyDictionary<string, object?> readOnlyDictionary)
            return readOnlyDictionary.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(text))
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, object?>>(text, JsonOptions);
            if (parsed != null)
                return parsed;
        }
        catch
        {
            // ignored, explicit error below
        }

        throw new InvalidOperationException(
            $"Tool '{toolName}' parameter '{paramName}' must be a JSON object.");
    }

    private static bool TryParseBoolean(string? text, out bool parsed)
    {
        if (bool.TryParse(text, out parsed))
            return true;

        if (string.Equals(text, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase))
        {
            parsed = true;
            return true;
        }

        if (string.Equals(text, "0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "no", StringComparison.OrdinalIgnoreCase))
        {
            parsed = false;
            return true;
        }

        return false;
    }

    private static void ValidateEnum(
        string toolName,
        string paramName,
        object? value,
        string[]? allowedValues)
    {
        if (allowedValues == null || allowedValues.Length == 0 || value == null)
            return;

        var actual = Convert.ToString(value, CultureInfo.InvariantCulture);
        if (actual == null)
            return;

        var isAllowed = allowedValues.Any(allowed =>
            string.Equals(allowed, actual, StringComparison.OrdinalIgnoreCase));

        if (!isAllowed)
        {
            throw new InvalidOperationException(
                $"Tool '{toolName}' parameter '{paramName}' must be one of [{string.Join(", ", allowedValues)}].");
        }
    }

    private static void ValidateFormat(string toolName, string paramName, object? value, string? format)
    {
        if (value == null || string.IsNullOrWhiteSpace(format))
            return;

        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        var normalizedFormat = format.Trim().ToLowerInvariant();

        var isValid = normalizedFormat switch
        {
            "uuid" or "guid" => Guid.TryParse(text, out _),
            "date" => DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            "date-time" => DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _),
            "time" or "hh:mm" => TimeOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            "email" => IsValidEmail(text),
            "uri" or "url" => Uri.TryCreate(text, UriKind.Absolute, out _),
            _ => true
        };

        if (!isValid)
        {
            throw new InvalidOperationException(
                $"Tool '{toolName}' parameter '{paramName}' has invalid format '{format}'.");
        }
    }

    private static bool IsValidEmail(string value)
    {
        try
        {
            _ = new MailAddress(value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static HashSet<string> GetPathParamNames(AgentToolDefinition tool)
    {
        if (tool.Params?.Count > 0)
        {
            return tool.Params
                .Where(kv => string.Equals(kv.Value.In, "path", StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        return ExtractPathParamNames(tool.UrlTemplate);
    }

    private static HashSet<string> GetQueryParamNames(AgentToolDefinition tool)
    {
        if (tool.Params?.Count > 0)
        {
            return tool.Params
                .Where(kv => string.Equals(kv.Value.In, "query", StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        if (tool.QueryParams is { Length: > 0 })
            return tool.QueryParams.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> GetBodyParamNames(
        AgentToolDefinition tool,
        HashSet<string> pathParams,
        HashSet<string> queryParams)
    {
        if (tool.Params?.Count > 0)
        {
            var bodyParams = tool.Params
                .Where(kv => string.Equals(kv.Value.In, "body", StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (bodyParams.Count > 0)
                return bodyParams;

            // Schema present but without explicit body params: keep backward-compatible fallback.
            return tool.Params.Keys
                .Where(name => !pathParams.Contains(name) && !queryParams.Contains(name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        if (tool.BodyParams?.Count > 0)
            return tool.BodyParams.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private static object? ResolveReference(string refPath, IStageContext? context)
    {
        if (context?.Payload == null)
            return null;

        // refPath format: "resultKey.json.path" or "resultKey[0].field"
        var dotIndex = refPath.IndexOf('.');
        var bracketIndex = refPath.IndexOf('[');
        var splitIndex = dotIndex < 0 ? bracketIndex : bracketIndex < 0 ? dotIndex : Math.Min(dotIndex, bracketIndex);

        var resultKey = splitIndex < 0 ? refPath : refPath[..splitIndex];
        var jsonPath = splitIndex < 0 ? null : refPath[splitIndex..].TrimStart('.');

        var contextKey = $"agent:result:{resultKey}";
        if (!context.Payload.TryGetValue(contextKey, out var rawValue))
            return null;

        if (string.IsNullOrWhiteSpace(jsonPath))
            return rawValue;

        try
        {
            var json = rawValue is string s ? s : JsonSerializer.Serialize(rawValue);
            var doc = JsonDocument.Parse(json);
            return NavigateJsonPath(doc.RootElement, jsonPath);
        }
        catch
        {
            return rawValue;
        }
    }

    private static object? NavigateJsonPath(JsonElement element, string path)
    {
        // Handle array index like [0]
        if (path.StartsWith('['))
        {
            var end = path.IndexOf(']');
            if (end < 0) return null;
            var idx = int.Parse(path[1..end]);
            if (element.ValueKind != JsonValueKind.Array) return null;
            var arr = element.EnumerateArray().ToList();
            if (idx >= arr.Count) return null;
            var rest = path[(end + 1)..].TrimStart('.');
            return string.IsNullOrEmpty(rest) ? GetPrimitive(arr[idx]) : NavigateJsonPath(arr[idx], rest);
        }

        // Handle property access
        var dotIdx = path.IndexOf('.');
        var bracketIdx = path.IndexOf('[');
        int splitAt;
        if (dotIdx < 0 && bracketIdx < 0) splitAt = path.Length;
        else if (dotIdx < 0) splitAt = bracketIdx;
        else if (bracketIdx < 0) splitAt = dotIdx;
        else splitAt = Math.Min(dotIdx, bracketIdx);

        var prop = path[..splitAt];
        var remaining = path[splitAt..].TrimStart('.');

        if (!element.TryGetProperty(prop, out var child)) return null;
        return string.IsNullOrEmpty(remaining) ? GetPrimitive(child) : NavigateJsonPath(child, remaining);
    }

    private static object? GetPrimitive(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => el.GetRawText()
    };

    private static List<AgentToolResult> GetOrCreateToolResults(IStageContext context)
    {
        context.Payload ??= new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (context.Payload.TryGetValue(AgentConstants.ToolResults, out var existing) &&
            existing is List<AgentToolResult> list)
            return list;

        var newList = new List<AgentToolResult>();
        context.Payload[AgentConstants.ToolResults] = newList;
        return newList;
    }

    private static List<AgentConversationTurn> GetOrCreateHistory(IStageContext context)
    {
        context.Payload ??= new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var existing = context.TryGetValue<List<AgentConversationTurn>>(AgentConstants.ConversationHistory);
        return existing ?? new List<AgentConversationTurn>();
    }
}
