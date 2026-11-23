using Nexaflow.AiHost.Catalog;
using Nexaflow.AiHost.Contracts;
using Nexaflow.Providers.Common;
using Nexaflow.Providers.Local;

namespace Nexaflow.Providers.AiHost;

/// <summary>
/// A tool of the owned catalog as the app reaches it (<see cref="HostedTool"/>): whether this machine runs it, the files it
/// fetches, whether they are here. Not a provider: a person or an assistant, local or in the cloud, uses the tool, and
/// nothing in the cloud stands in for it. Its files are fetched as an owned model's are, and its parts placed by the host and
/// loaded only while it is used; what it does is each tool's own.
/// </summary>
public abstract class OwnedToolFront
{
    private protected readonly OwnedModelRuntime Owned;
    private protected readonly HostedTool? Tool;

    private protected OwnedToolFront(OwnedModelRuntime runtime, HostedTool? tool)
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
    private protected HostedTool Ready()
        => Tool is not null && IsReady ? Tool : throw new InvalidOperationException($"{Tool?.Catalog.DisplayName ?? "The tool"} is not on this machine yet.");

    /// <summary>The tool as a request names it, once it is ready: the host runs the parts the request needs, each where it places
    /// them — on the GPU with room for it, else the CPU.</summary>
    /// <exception cref="InvalidOperationException">It is not on this machine, or cannot run on it.</exception>
    private protected string ToolId()
    {
        var tool = Ready();
        Owned.Host.Environment = Owned.Environment;
        return tool.Id;
    }

    /// <summary>The tool's part that does <paramref name="role"/>, as a request names it — for one asked of on its own.</summary>
    /// <exception cref="InvalidOperationException">It is not on this machine, or cannot run on it.</exception>
    private protected ModelRef PartOf(string role)
    {
        var tool = Ready();
        Owned.Host.Environment = Owned.Environment;
        return new ModelRef(tool.Id, tool.Part(role, Owned.ExecutionProvider).Name);
    }
}
