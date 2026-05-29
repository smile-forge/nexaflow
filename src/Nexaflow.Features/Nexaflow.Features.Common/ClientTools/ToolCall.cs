using System.Text.Json.Nodes;

namespace Nexaflow.Features.Common.ClientTools;

/// <summary>A single tool invocation the model requested via a <c>client_tool</c> block.</summary>
/// <param name="Tool">The requested tool name.</param>
/// <param name="Arguments">The JSON arguments object (never null; empty when none supplied).</param>
/// <param name="RawBlock">The original block body, for diagnostics.</param>
public sealed record ToolCall(string Tool, JsonObject Arguments, string? RawBlock = null);
