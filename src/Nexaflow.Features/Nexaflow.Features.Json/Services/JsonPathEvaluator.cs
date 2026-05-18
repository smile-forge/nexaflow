using Json.Path;
using Nexaflow.Features.Json.Models;
using System.Text.Json.Nodes;

namespace Nexaflow.Features.Json.Services;

internal sealed class JsonPathEvaluator
{
    public IReadOnlyList<JsonNodeModel> Evaluate(string jsonPath, JsonNodeModel root)
    {
        try
        {
            var nodeMap  = new Dictionary<JsonNode, JsonNodeModel>(ReferenceEqualityComparer.Instance);
            var jsonNode = ModelToJsonNode(root, nodeMap);
            if (jsonNode is null) return [];

            var path   = JsonPath.Parse(jsonPath);
            var result = path.Evaluate(jsonNode);
            if (result.Matches is null) return [];

            var matches = new List<JsonNodeModel>();
            foreach (var match in result.Matches)
            {
                if (match.Value is not null && nodeMap.TryGetValue(match.Value, out var model))
                    matches.Add(model);
            }
            return matches;
        }
        catch
        {
            return [];
        }
    }

    private static JsonNode? ModelToJsonNode(JsonNodeModel model, Dictionary<JsonNode, JsonNodeModel> map)
    {
        JsonNode? node = model switch
        {
            JsonObjectNodeModel obj => BuildJsonObject(obj, map),
            JsonArrayNodeModel  arr => BuildJsonArray(arr, map),
            JsonValueNodeModel  val => BuildJsonValue(val),
            VirtualJsonNodeModel    => null,
            _                       => null,
        };

        if (node is not null) map[node] = model;
        return node;
    }

    private static JsonObject BuildJsonObject(JsonObjectNodeModel obj, Dictionary<JsonNode, JsonNodeModel> map)
    {
        var result = new JsonObject();
        foreach (var child in obj.Children)
        {
            if (child.Key is null) continue;
            var childNode = ModelToJsonNode(child, map);
            result[child.Key] = childNode;
        }
        return result;
    }

    private static JsonArray BuildJsonArray(JsonArrayNodeModel arr, Dictionary<JsonNode, JsonNodeModel> map)
    {
        var result = new JsonArray();
        foreach (var child in arr.Children)
        {
            var childNode = ModelToJsonNode(child, map);
            result.Add(childNode);
        }
        return result;
    }

    private static JsonNode? BuildJsonValue(JsonValueNodeModel val)
    {
        return val.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String => JsonValue.Create(val.Value?.ToString()),
            System.Text.Json.JsonValueKind.True   => JsonValue.Create(true),
            System.Text.Json.JsonValueKind.False  => JsonValue.Create(false),
            System.Text.Json.JsonValueKind.Null   => null,
            System.Text.Json.JsonValueKind.Number when val.Value is long l   => JsonValue.Create(l),
            System.Text.Json.JsonValueKind.Number when val.Value is double d => JsonValue.Create(d),
            _ => JsonValue.Create(val.Value?.ToString()),
        };
    }
}
