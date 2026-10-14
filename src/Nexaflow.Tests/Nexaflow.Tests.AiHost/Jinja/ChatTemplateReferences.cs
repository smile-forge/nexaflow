using System.Text.Json.Nodes;

namespace Nexaflow.Tests.AiHost.Jinja;

/// <summary>What <c>tools/ai-models/chat-templates/render_references.py</c> wrote with transformers' Jinja, embedded:
/// each shipped family's chat template with conversations rendered by it, and one-feature snippets.</summary>
internal static class ChatTemplateReferences
{
    private static readonly Lazy<JsonObject> FamiliesData = new(() => Resource("chat-templates.json"));
    private static readonly Lazy<JsonObject> SnippetsData = new(() => Resource("jinja-snippets.json"));

    public static JsonObject Families => FamiliesData.Value;

    public static JsonObject Snippets => SnippetsData.Value;

    /// <summary>A family's own chat template: <c>gemma-4</c>, <c>qwen-3.5</c>, <c>qwen-3.6</c>, <c>qwen-3.8</c>.</summary>
    public static string Template(string family) => (string)Families[family]!["template"]!;

    private static JsonObject Resource(string name)
    {
        using var stream = typeof(ChatTemplateReferences).Assembly.GetManifestResourceStream(name)
                           ?? throw new InvalidOperationException($"{name} is not embedded.");
        return JsonNode.Parse(stream)!.AsObject();
    }
}
