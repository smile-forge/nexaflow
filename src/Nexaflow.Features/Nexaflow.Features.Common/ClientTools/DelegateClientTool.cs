using System.Text.Json.Nodes;

namespace Nexaflow.Features.Common.ClientTools;

/// <summary>
/// Convenience <see cref="IClientTool"/> built from a delegate, for trivial single-purpose tools
/// that don't warrant their own class (e.g. a page exposing one "format" or "search" action).
/// Tools with real logic should implement <see cref="IClientTool"/> directly.
/// </summary>
public sealed class DelegateClientTool(
    string name,
    string description,
    IReadOnlyList<ClientToolParameter> parameters,
    ToolSafety safety,
    Func<JsonObject, CancellationToken, Task<ToolResult>> invoke,
    bool parallelizable = false) : IClientTool
{
    public string Name { get; } = name;
    public string Description { get; } = description;
    public IReadOnlyList<ClientToolParameter> Parameters { get; } = parameters;
    public ToolSafety Safety { get; } = safety;
    public bool Parallelizable { get; } = parallelizable;

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct) =>
        invoke(arguments, ct);
}
