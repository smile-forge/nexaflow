using System.Text.Json.Nodes;

namespace Nexaflow.Features.Font.ClientTools;

/// <summary>Reads a tool argument as a string, tolerating a model that sends a number for a string slot.</summary>
internal static class FontToolArgs
{
    public static string? GetString(JsonObject arguments, string key)
    {
        if (!arguments.TryGetPropertyValue(key, out var node) || node is null) return null;
        try { return node.GetValue<string>(); }
        catch { return node.ToString(); }
    }
}
