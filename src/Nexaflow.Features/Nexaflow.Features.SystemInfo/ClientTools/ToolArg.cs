using System.Text.Json.Nodes;

namespace Nexaflow.Features.SystemInfo.ClientTools;

/// <summary>Tiny reader for the loosely-typed <see cref="JsonObject"/> arguments the model emits.</summary>
internal static class ToolArg
{
    public static string? Str(JsonObject o, params string[] keys)
    {
        foreach (var k in keys)
            if (o.TryGetPropertyValue(k, out var node) && node is not null)
            {
                var s = node.ToString();
                if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
            }
        return null;
    }
}
