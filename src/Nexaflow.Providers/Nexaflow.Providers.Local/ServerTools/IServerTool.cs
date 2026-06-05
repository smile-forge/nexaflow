namespace Nexaflow.Providers.Local.ServerTools;

/// <summary>One parameter a server-side tool accepts. <paramref name="Type"/> is a JSON-ish hint
/// ("string", "number", "boolean", "integer").</summary>
public sealed record ServerToolParam(string Name, string Type, string Description, bool Required = true);

/// <summary>
/// A tool the LOCAL model can call inside the provider's own server-side harness — distinct from
/// Nexaflow's client-side tools. Resolved entirely within <c>CompleteAsync</c>; the model never leaves
/// the provider to use one. Implement this and register it (built-in, or via an
/// <see cref="IServerToolSource"/> for MCP) to add a capability.
/// </summary>
public interface IServerTool
{
    /// <summary>Stable, lower-case identifier the model emits in a native tool call.</summary>
    string Name { get; }

    /// <summary>One-line description shown to the model in the tool catalogue.</summary>
    string Description { get; }

    IReadOnlyList<ServerToolParam> Parameters { get; }

    /// <summary>Runs the tool. Return a short, model-readable result string. Do not throw for an
    /// expected failure — return an error message the model can read and recover from.</summary>
    Task<string> InvokeAsync(IReadOnlyDictionary<string, object?> arguments, CancellationToken ct);
}

/// <summary>
/// A source of server-side tools discovered at runtime — the seam MCP servers will plug into.
/// (No implementations yet; the built-in tools are registered directly.)
/// </summary>
public interface IServerToolSource
{
    IReadOnlyList<IServerTool> GetTools();
}
