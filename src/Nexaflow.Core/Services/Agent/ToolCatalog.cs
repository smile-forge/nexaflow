using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nexaflow.Features.Common.ClientTools;

namespace Nexaflow.Core.Services.Agent;

/// <summary>Tools described in words for a model to read: each with what it does and its parameters.</summary>
internal static class ToolCatalog
{
    /// <summary>JSON as a model reads it: every character as itself, where the default writer escapes &lt;, &gt;, &amp;,
    /// quotes and everything past ASCII to <c>\uXXXX</c>.</summary>
    public static readonly JsonSerializerOptions Readable = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public static string Describe(IEnumerable<IClientTool> tools)
    {
        var sb = new StringBuilder();
        foreach (var t in tools)
            sb.Append(Describe(t)).Append('\n');
        var s = sb.ToString().TrimEnd();
        return s.Length == 0 ? "(no tools available here)" : s;
    }

    private static string Describe(IClientTool t)
    {
        var sb = new StringBuilder();
        sb.Append("- ").Append(t.Name).Append(": ").Append(t.Description);
        foreach (var p in t.Parameters)
        {
            sb.Append("\n    • ").Append(p.Name).Append(" (").Append(p.Type);
            if (!p.Required) sb.Append(", optional");
            if (p.Choices is { Count: > 0 } choices) sb.Append(", one of ").Append(string.Join(" | ", choices));
            sb.Append("): ").Append(p.Description);
        }
        return sb.ToString();
    }

    /// <summary>Arguments for a call to the tool, as an example to write one from: each required parameter with a stand-in
    /// of its type — or every parameter, for a tool whose parameters are all optional.</summary>
    public static JsonObject Example(IClientTool tool)
    {
        var shown = tool.Parameters.Any(p => p.Required) ? tool.Parameters.Where(p => p.Required) : tool.Parameters;
        var arguments = new JsonObject();
        foreach (var p in shown)
            arguments[p.Name] = p.Type switch
            {
                "number" or "integer" => 1,
                "boolean"             => true,
                "array"               => new JsonArray(),
                "object"              => new JsonObject(),
                _                     => p.Choices is [var first, ..] ? first : $"<{p.Name}>",
            };
        return arguments;
    }
}
