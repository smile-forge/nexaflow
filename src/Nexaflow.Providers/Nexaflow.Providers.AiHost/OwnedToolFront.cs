using Nexaflow.AiHost.Contracts;
using Nexaflow.Providers.Common;

namespace Nexaflow.Providers.AiHost;

/// <summary>
/// A tool of the owned catalog as the app reaches it (<see cref="OwnedTool"/>): whether this machine runs it, the files it
/// fetches, whether they are here. Not a provider: a person or an assistant, local or in the cloud, uses the tool, and
/// nothing in the cloud stands in for it. Its parts are fetched and placed as an owned model's are, and load only while
/// it is used; what it does is each tool's own.
/// </summary>
public abstract class OwnedToolFront
{
    private protected readonly OwnedModelRuntime Owned;
    private protected readonly OwnedTool? Tool;

    private protected OwnedToolFront(OwnedModelRuntime runtime, OwnedTool? tool)
    {
        Owned = runtime;
        Tool = tool;
        runtime.Assets.Changed += (_, asset) =>
        {
            if (Tool is not null && Owned.IsFileOf(Tool, asset)) AvailabilityChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>Whether this machine can run it: there is a build for its runtime, and its GPU — if it has one — has room
    /// for the largest part, else the CPU takes it.</summary>
    public bool RunsHere => Tool is not null && Tool.RunsOn(Owned.ExecutionProvider) && Owned.Fits(Tool);

    /// <summary>Its models as this machine fetches them; null where it cannot run them.</summary>
    public CatalogModel? Catalog => RunsHere ? Owned.CatalogOf(Tool!) : null;

    /// <summary>The CUDA pack it runs with where it suits this machine; null where it runs on the CPU.</summary>
    public LocalRuntime? Runtime => Owned.Runtime;

    /// <summary>Everything it needs is here: its files, and the CUDA pack where it suits the machine.</summary>
    public bool IsReady => RunsHere && Owned.IsReady(Tool!);

    /// <summary>Raised when one of its files arrives or goes.</summary>
    public event EventHandler? AvailabilityChanged;

    /// <summary>The tool, ready to use; never fetched here — a tool is fetched when a person asks for it.</summary>
    /// <exception cref="InvalidOperationException">It is not on this machine, or cannot run on it.</exception>
    private protected OwnedTool Ready()
        => Tool is not null && IsReady ? Tool : throw new InvalidOperationException($"{Tool?.Catalog.DisplayName ?? "The tool"} is not on this machine yet.");

    /// <summary>Each part in <paramref name="roles"/> placed where it runs: on the GPU with room for it, else the CPU.</summary>
    private protected async Task<Dictionary<string, ModelSpec>> PlacedAsync(IEnumerable<string> roles, CancellationToken ct)
    {
        var tool = Ready();
        var parts = new Dictionary<string, ModelSpec>();
        foreach (var role in roles)
            parts[role] = await Owned.PlacedOnDemandAsync(tool, tool.Part(role, Owned.ExecutionProvider).Name, ct);
        return parts;
    }
}
