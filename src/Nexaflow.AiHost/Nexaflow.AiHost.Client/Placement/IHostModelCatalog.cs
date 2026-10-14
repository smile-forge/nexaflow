namespace Nexaflow.AiHost.Client;

/// <summary>A provider of models the host runs, as the machine's setup is described before any of them loads: what
/// each of its models needs placed.</summary>
public interface IHostModelCatalog
{
    /// <summary>Whether its models load for each use — a picture's parts, one after another — rather than staying
    /// loaded while they are used.</summary>
    bool LoadsOnDemand { get; }

    /// <summary>What the parts of <paramref name="modelId"/> need placed on this machine; none for a model it does not
    /// have, or one with no build for this machine.</summary>
    IReadOnlyList<PlacementNeed> NeedsOf(string modelId);
}
