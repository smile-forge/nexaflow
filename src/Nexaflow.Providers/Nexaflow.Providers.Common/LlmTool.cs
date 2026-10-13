using System.Text.Json.Nodes;

namespace Nexaflow.Providers.Common;

/// <summary>
/// A tool the model may call, offered in its own protocol: the name it calls it by, what it does, and its arguments as a
/// JSON Schema object (<c>{"type": "object", "properties": {…}, "required": […]}</c>) — the shape every model's tool
/// calling is declared in.
/// </summary>
public sealed record LlmTool(string Name, string Description, JsonObject Parameters);

/// <summary>A call the model made: the id its result answers to, the tool it called, and the arguments it gave.</summary>
public sealed record LlmToolCall(string Id, string Name, JsonObject Arguments);
