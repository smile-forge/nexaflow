using System.Text.Json.Nodes;

namespace Nexaflow.Features.Common.ClientTools;

/// <summary>
/// Optional base for logic-bearing <see cref="IClientTool"/> implementations. Collapses the
/// per-class ceremony to the members that actually differ: <see cref="Name"/> and
/// <see cref="Description"/> are abstract, everything else defaults to the safest choice —
/// no parameters, <see cref="ToolSafety.SafeOperation"/>, non-parallelizable. Override what differs.
/// Use <see cref="DelegateClientTool"/> for one-liners instead.
/// </summary>
public abstract class ClientToolBase : IClientTool
{
    public abstract string Name { get; }
    public abstract string Description { get; }

    /// <summary>Named arguments the tool accepts. Default: none.</summary>
    public virtual IReadOnlyList<ClientToolParameter> Parameters => [];

    /// <summary>Default <see cref="ToolSafety.SafeOperation"/> (auto-runs) — override to
    /// <see cref="ToolSafety.RequiresApproval"/> for tools with real, not-obviously-reversible consequence.</summary>
    public virtual ToolSafety Safety => ToolSafety.SafeOperation;

    /// <summary>Default false (runs alone in a batch) — override to true only for independent, side-effect-free tools.</summary>
    public virtual bool Parallelizable => false;

    /// <inheritdoc cref="IClientTool.InvokeAsync"/>
    public abstract Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct);
}
