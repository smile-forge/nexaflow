namespace Nexaflow.Providers.Local.ServerTools;

/// <summary>
/// Holds the server-side tools enabled for one provider instance and dispatches calls by name.
/// Built from the config's enabled-tool list plus any <see cref="IServerToolSource"/>s (the MCP seam).
/// </summary>
public sealed class ServerToolRegistry
{
    private readonly Dictionary<string, IServerTool> _byName;

    public ServerToolRegistry(IEnumerable<IServerTool> tools)
    {
        _byName = tools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        Tools   = [.. _byName.Values];
    }

    public IReadOnlyList<IServerTool> Tools { get; }

    /// <summary>The built-in server tools available to every Local provider (filtered per-config by name).
    /// Add new built-ins here.</summary>
    private static readonly IReadOnlyList<IServerTool> BuiltInTools = [new CalculatorServerTool()];

    /// <summary>Names of the built-in tools — used by the config UI to render the enable checkboxes.</summary>
    public static IReadOnlyList<string> BuiltInNames { get; } = [.. BuiltInTools.Select(t => t.Name)];

    /// <summary>Built-in tools whose names appear in <paramref name="enabled"/>, plus any from
    /// <paramref name="sources"/> (MCP, wired later).</summary>
    public static ServerToolRegistry Build(
        IEnumerable<string> enabled,
        IEnumerable<IServerToolSource>? sources = null)
    {
        var enabledSet = new HashSet<string>(enabled, StringComparer.OrdinalIgnoreCase);
        var tools      = new List<IServerTool>();

        foreach (var t in BuiltInTools)
            if (enabledSet.Contains(t.Name)) tools.Add(t);

        if (sources is not null)
            foreach (var s in sources)
                tools.AddRange(s.GetTools());

        return new ServerToolRegistry(tools);
    }

    /// <summary>Runs a parsed tool call; returns a model-readable result (or error) string. Never throws
    /// except on cancellation.</summary>
    public async Task<string> InvokeAsync(ServerToolCall call, CancellationToken ct)
    {
        if (!_byName.TryGetValue(call.Name, out var tool))
            return $"Error: no server-side tool named '{call.Name}'.";

        try { return await tool.InvokeAsync(call.Arguments, ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return $"Error running '{call.Name}': {ex.Message}"; }
    }
}
