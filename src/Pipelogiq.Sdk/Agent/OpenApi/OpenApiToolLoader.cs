using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PipelogiqSDK.Agent.Models;

namespace PipelogiqSDK.Agent.OpenApi;

/// <summary>
/// Parses an OpenAPI 3.x or Swagger 2.x specification and converts
/// each endpoint operation into an <see cref="AgentToolDefinition"/>.
/// </summary>
public static class OpenApiToolLoader
{
    private static readonly JsonSerializerOptions SerializeOptions = new() { WriteIndented = true };

    /// <summary>
    /// Loads tool definitions from an OpenAPI spec URL or file path.
    /// </summary>
    /// <param name="specUrlOrPath">HTTPS URL, HTTP URL, or local file path to the spec JSON.</param>
    /// <param name="options">Filtering and behavior options.</param>
    /// <param name="httpClient">Optional pre-configured HttpClient. A new one is created if null.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task<List<AgentToolDefinition>> LoadAsync(
        string specUrlOrPath,
        OpenApiLoadOptions? options = null,
        HttpClient? httpClient = null,
        CancellationToken ct = default)
    {
        options ??= new OpenApiLoadOptions();

        var json = await FetchSpecAsync(specUrlOrPath, options, httpClient, ct);
        using var doc = JsonDocument.Parse(json);

        return ParseSpec(doc, options);
    }

    // ── Fetching ─────────────────────────────────────────────────────────────

    private static async Task<string> FetchSpecAsync(
        string specUrlOrPath,
        OpenApiLoadOptions options,
        HttpClient? httpClient,
        CancellationToken ct)
    {
        if (specUrlOrPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            specUrlOrPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            using var ownClient = httpClient == null ? new HttpClient() : null;
            var client = httpClient ?? ownClient!;

            if (options.SpecFetchHeaders != null)
                foreach (var (k, v) in options.SpecFetchHeaders)
                    client.DefaultRequestHeaders.TryAddWithoutValidation(k, v);

            return await client.GetStringAsync(specUrlOrPath, ct);
        }

        // Local file
        var path = specUrlOrPath.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
            ? specUrlOrPath[7..]
            : specUrlOrPath;

        return await File.ReadAllTextAsync(path, ct);
    }

    // ── Top-level parsing ─────────────────────────────────────────────────────

    private static List<AgentToolDefinition> ParseSpec(JsonDocument doc, OpenApiLoadOptions options)
    {
        var root = doc.RootElement;
        var tools = new List<AgentToolDefinition>();

        if (!root.TryGetProperty("paths", out var pathsEl))
            return tools;

        foreach (var pathProp in pathsEl.EnumerateObject())
        {
            var urlPath = pathProp.Name;

            // Apply path prefix exclusion
            if (options.ExcludePathPrefixes != null &&
                options.ExcludePathPrefixes.Any(prefix =>
                    urlPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                continue;

            foreach (var methodProp in pathProp.Value.EnumerateObject())
            {
                var method = methodProp.Name.ToUpperInvariant();
                if (!IsHttpMethod(method)) continue;

                var tool = ParseOperation(urlPath, method, methodProp.Value, root, options);
                if (tool != null)
                    tools.Add(tool);
            }
        }

        return tools;
    }

    private static AgentToolDefinition? ParseOperation(
        string urlPath,
        string method,
        JsonElement operationEl,
        JsonElement root,
        OpenApiLoadOptions options)
    {
        // operationId → tool name
        var operationId = operationEl.TryGetProperty("operationId", out var opIdEl)
            ? opIdEl.GetString()
            : GenerateOperationId(method, urlPath);

        if (string.IsNullOrWhiteSpace(operationId))
            return null;

        // Filtering
        if (options.IncludeOperations?.Count > 0 &&
            !options.IncludeOperations.Contains(operationId, StringComparer.OrdinalIgnoreCase))
            return null;

        if (options.ExcludeOperations?.Contains(operationId, StringComparer.OrdinalIgnoreCase) == true)
            return null;

        if (options.IncludeTags?.Count > 0)
        {
            var tags = GetTags(operationEl);
            if (!tags.Any(t => options.IncludeTags.Contains(t, StringComparer.OrdinalIgnoreCase)))
                return null;
        }

        // Description: summary → description → operationId
        var description = GetString(operationEl, "summary")
                          ?? GetString(operationEl, "description")
                          ?? operationId;

        var tool = new AgentToolDefinition
        {
            Name = operationId,
            Description = description,
            HttpMethod = method,
            TargetApiName = options.TargetApiName,
            UrlTemplate = urlPath,
            IsMutating = options.MutatingMethods.Contains(method),
            Params = new Dictionary<string, AgentToolParam>(),
        };

        // Parse path + query parameters
        if (operationEl.TryGetProperty("parameters", out var paramsEl))
            ParseParameters(paramsEl, root, tool.Params);

        // Parse request body (OpenAPI 3.x)
        if (operationEl.TryGetProperty("requestBody", out var bodyEl))
            ParseRequestBody(bodyEl, root, tool.Params, options);

        // Parse request body (Swagger 2.x: parameters with in=body)
        ParseSwagger2Body(tool.Params, root, options);

        // Generate response example from first 2xx response
        tool.ResponseExample = ExtractResponseExample(operationEl, root, options);

        // Generate body example from body params
        tool.BodyExample = BuildBodyExample(tool.Params);

        return tool;
    }

    // ── Parameters ───────────────────────────────────────────────────────────

    private static void ParseParameters(
        JsonElement paramsEl,
        JsonElement root,
        Dictionary<string, AgentToolParam> target)
    {
        foreach (var paramEl in paramsEl.EnumerateArray())
        {
            var resolved = ResolveRef(paramEl, root);
            if (resolved == null) continue;

            var name = GetString(resolved.Value, "name");
            if (string.IsNullOrWhiteSpace(name)) continue;

            var inValue = GetString(resolved.Value, "in") ?? "query";
            if (inValue == "header" || inValue == "cookie") continue; // skip non-relevant params

            var schema = resolved.Value.TryGetProperty("schema", out var sEl) ? (JsonElement?)sEl : null;
            var resolvedSchema = schema.HasValue ? ResolveRef(schema.Value, root) : null;

            var param = new AgentToolParam
            {
                In = inValue,
                Description = GetString(resolved.Value, "description")
                              ?? GetString(resolvedSchema ?? default, "description")
                              ?? name,
                Required = GetBool(resolved.Value, "required") ?? (inValue == "path"),
                Type = GetString(resolvedSchema ?? default, "type") ?? "string",
                Format = GetString(resolvedSchema ?? default, "format"),
                Example = ExtractExample(resolvedSchema ?? default)
                          ?? ExtractExample(resolved.Value),
                EnumValues = ExtractEnum(resolvedSchema ?? default),
            };

            target[name!] = param;
        }
    }

    private static void ParseRequestBody(
        JsonElement bodyEl,
        JsonElement root,
        Dictionary<string, AgentToolParam> target,
        OpenApiLoadOptions options)
    {
        var resolved = ResolveRef(bodyEl, root);
        if (resolved == null) return;

        var schema = ExtractJsonSchema(resolved.Value, root);
        if (schema == null) return;

        var schemaEl = schema.Value;
        var schemaResolved = ResolveRef(schemaEl, root);
        if (schemaResolved == null) return;

        var requiredNames = GetRequiredNames(schemaResolved.Value);
        ExtractObjectProperties(schemaResolved.Value, root, target, requiredNames, options.MaxExampleDepth);
    }

    private static void ParseSwagger2Body(
        Dictionary<string, AgentToolParam> target,
        JsonElement root,
        OpenApiLoadOptions options)
    {
        // Swagger 2.x in=body parameters were already iterated in ParseParameters,
        // but we need to drill into their schema. This is handled there via "schema" property.
        // Nothing extra needed here for the common case.
    }

    private static void ExtractObjectProperties(
        JsonElement schemaEl,
        JsonElement root,
        Dictionary<string, AgentToolParam> target,
        HashSet<string> required,
        int maxDepth)
    {
        if (maxDepth <= 0) return;

        // Handle allOf: merge all sub-schemas
        if (schemaEl.TryGetProperty("allOf", out var allOfEl))
        {
            foreach (var subEl in allOfEl.EnumerateArray())
            {
                var resolved = ResolveRef(subEl, root);
                if (resolved != null)
                    ExtractObjectProperties(resolved.Value, root, target, required, maxDepth);
            }
            return;
        }

        if (!schemaEl.TryGetProperty("properties", out var propsEl)) return;

        foreach (var prop in propsEl.EnumerateObject())
        {
            var resolved = ResolveRef(prop.Value, root);
            var propSchema = resolved ?? prop.Value;

            var param = new AgentToolParam
            {
                In = "body",
                Description = GetString(propSchema, "description") ?? prop.Name,
                Required = required.Contains(prop.Name),
                Type = GetString(propSchema, "type") ?? "string",
                Format = GetString(propSchema, "format"),
                Example = ExtractExample(propSchema),
                EnumValues = ExtractEnum(propSchema),
            };

            target[prop.Name] = param;
        }
    }

    // ── Response example ─────────────────────────────────────────────────────

    private static object? ExtractResponseExample(
        JsonElement operationEl,
        JsonElement root,
        OpenApiLoadOptions options)
    {
        if (!operationEl.TryGetProperty("responses", out var responsesEl)) return null;

        // Find first 2xx response
        foreach (var resp in responsesEl.EnumerateObject())
        {
            if (!resp.Name.StartsWith('2')) continue;

            var resolvedResp = ResolveRef(resp.Value, root);
            var schema = resolvedResp.HasValue
                ? ExtractJsonSchema(resolvedResp.Value, root)
                : null;

            if (schema == null) continue;

            var resolvedSchema = ResolveRef(schema.Value, root);
            if (resolvedSchema == null) continue;

            return GenerateExampleObject(resolvedSchema.Value, root, options.MaxExampleDepth);
        }

        return null;
    }

    private static object? GenerateExampleObject(JsonElement schema, JsonElement root, int depth)
    {
        if (depth <= 0) return null;

        // Resolve $ref
        var resolved = ResolveRef(schema, root);
        var el = resolved ?? schema;

        var type = GetString(el, "type");

        // allOf → merge
        if (el.TryGetProperty("allOf", out var allOfEl))
        {
            var merged = new Dictionary<string, object?>();
            foreach (var sub in allOfEl.EnumerateArray())
            {
                var r = ResolveRef(sub, root);
                var part = GenerateExampleObject(r ?? sub, root, depth);
                if (part is Dictionary<string, object?> dict)
                    foreach (var kv in dict) merged[kv.Key] = kv.Value;
            }
            return merged.Count > 0 ? merged : null;
        }

        // Object
        if (type == "object" || el.TryGetProperty("properties", out _))
        {
            if (!el.TryGetProperty("properties", out var propsEl)) return new { };
            var obj = new Dictionary<string, object?>();
            foreach (var prop in propsEl.EnumerateObject())
            {
                var r = ResolveRef(prop.Value, root);
                var propEl = r ?? prop.Value;
                // Use inline example if present
                var ex = ExtractExample(propEl);
                obj[prop.Name] = ex != null ? (object?)ex : GenerateExampleObject(propEl, root, depth - 1);
            }
            return obj;
        }

        // Array
        if (type == "array" && el.TryGetProperty("items", out var itemsEl))
        {
            var r = ResolveRef(itemsEl, root);
            var itemExample = GenerateExampleObject(r ?? itemsEl, root, depth - 1);
            return itemExample != null ? new[] { itemExample } : Array.Empty<object>();
        }

        // Primitive: use example, default, or type-based placeholder
        return ExtractExample(el) ?? TypePlaceholder(el);
    }

    private static object? BuildBodyExample(Dictionary<string, AgentToolParam> allParams)
    {
        var body = allParams.Where(kv => kv.Value.In == "body").ToList();
        if (body.Count == 0) return null;

        var dict = new Dictionary<string, object?>();
        foreach (var (name, param) in body)
            dict[name] = param.Example ?? TypePlaceholder(param.Type, param.Format);

        return dict;
    }

    // ── $ref resolution ───────────────────────────────────────────────────────

    private static JsonElement? ResolveRef(JsonElement el, JsonElement root)
    {
        if (!el.TryGetProperty("$ref", out var refEl))
            return el;

        var refPath = refEl.GetString();
        if (string.IsNullOrWhiteSpace(refPath) || !refPath.StartsWith('#'))
            return null;

        // "#/components/schemas/Foo" or "#/definitions/Foo"
        var parts = refPath.TrimStart('#', '/').Split('/');
        var current = root;
        foreach (var part in parts)
        {
            if (!current.TryGetProperty(part, out var next)) return null;
            current = next;
        }

        // Recurse in case the target also has a $ref
        return ResolveRef(current, root);
    }

    private static JsonElement? ExtractJsonSchema(JsonElement el, JsonElement root)
    {
        // OpenAPI 3.x: content -> application/json -> schema
        if (el.TryGetProperty("content", out var contentEl))
        {
            foreach (var media in contentEl.EnumerateObject())
            {
                if (media.Value.TryGetProperty("schema", out var s))
                    return ResolveRef(s, root) ?? s;
            }
        }

        // Swagger 2.x: schema directly on body parameter
        if (el.TryGetProperty("schema", out var schemaEl))
            return ResolveRef(schemaEl, root) ?? schemaEl;

        return null;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string? ExtractExample(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Undefined) return null;

        if (el.TryGetProperty("example", out var exEl) && exEl.ValueKind != JsonValueKind.Null)
            return exEl.ValueKind == JsonValueKind.String ? exEl.GetString() : exEl.GetRawText();

        if (el.TryGetProperty("default", out var defEl) && defEl.ValueKind != JsonValueKind.Null)
            return defEl.ValueKind == JsonValueKind.String ? defEl.GetString() : defEl.GetRawText();

        return null;
    }

    private static string[]? ExtractEnum(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Undefined) return null;
        if (!el.TryGetProperty("enum", out var enumEl)) return null;

        var values = enumEl.EnumerateArray()
            .Select(v => v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .ToArray();

        return values.Length > 0 ? values : null;
    }

    private static HashSet<string> GetRequiredNames(JsonElement schemaEl)
    {
        if (!schemaEl.TryGetProperty("required", out var reqEl))
            return new HashSet<string>();

        return reqEl.EnumerateArray()
            .Where(v => v.ValueKind == JsonValueKind.String)
            .Select(v => v.GetString()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string? GetString(JsonElement el, string key)
    {
        if (el.ValueKind == JsonValueKind.Undefined) return null;
        return el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
    }

    private static bool? GetBool(JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static List<string> GetTags(JsonElement operationEl)
    {
        if (!operationEl.TryGetProperty("tags", out var tagsEl)) return new();
        return tagsEl.EnumerateArray()
            .Where(t => t.ValueKind == JsonValueKind.String)
            .Select(t => t.GetString()!)
            .ToList();
    }

    private static bool IsHttpMethod(string method) =>
        method is "GET" or "POST" or "PUT" or "PATCH" or "DELETE" or "HEAD" or "OPTIONS";

    private static string GenerateOperationId(string method, string path)
    {
        // "patch /api/plantimes/{id}" → "patchPlantimesById"
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => Regex.IsMatch(p, @"^\{.+\}$")
                ? "By" + char.ToUpperInvariant(p[1]) + p[2..^1]
                : char.ToUpperInvariant(p[0]) + p[1..])
            .ToList();

        return method.ToLowerInvariant() + string.Concat(parts);
    }

    private static object? TypePlaceholder(JsonElement el)
    {
        var type = GetString(el, "type") ?? "string";
        var format = GetString(el, "format");
        return TypePlaceholder(type, format);
    }

    private static object? TypePlaceholder(string type, string? format) => type switch
    {
        "integer" or "int32" or "int64" => (object?)0,
        "number" or "float" or "double" => 0.0,
        "boolean" => false,
        "array" => Array.Empty<object>(),
        "string" => format switch
        {
            "date" => DateTime.Today.ToString("yyyy-MM-dd"),
            "date-time" => DateTime.UtcNow.ToString("o"),
            "time" or "HH:mm" => "09:00",
            "uuid" or "guid" => "00000000-0000-0000-0000-000000000000",
            "email" => "user@example.com",
            "uri" or "url" => "https://example.com",
            _ => "string"
        },
        _ => null
    };
}
