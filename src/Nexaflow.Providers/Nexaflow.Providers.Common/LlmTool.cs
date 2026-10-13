using System.Text.Json.Nodes;
using System.Text.Json;

namespace Nexaflow.Providers.Common;

/// <summary>
/// A tool the model may call, offered in its own protocol: the name it calls it by, what it does, and its arguments as a
/// JSON Schema object (<c>{"type": "object", "properties": {…}, "required": […]}</c>) — the shape every model's tool
/// calling is declared in.
/// </summary>
public sealed record LlmTool(string Name, string Description, JsonObject Parameters);

/// <summary>A call the model made: the id its result answers to, the tool it called, and the arguments it gave.</summary>
public sealed record LlmToolCall(string Id, string Name, JsonObject Arguments)
{
    /// <summary>What the provider sent with the call to have back with it in the history — Gemini's thought signature,
    /// without which a thinking model loses its reasoning across the call. Null for most providers.</summary>
    public byte[]? Signature { get; init; }
    /// <summary>A call whose arguments came as JSON text — a provider's wire shape, or pieces streamed and joined.
    /// Arguments that are none, or do not read as an object, are none, so the tool says what it is missing.</summary>
    public static LlmToolCall Read(string id, string name, string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments)) return new(id, name, []);
        try { return new(id, name, JsonNode.Parse(arguments) as JsonObject ?? []); }
        catch (JsonException) { return new(id, name, []); }
    }
}
