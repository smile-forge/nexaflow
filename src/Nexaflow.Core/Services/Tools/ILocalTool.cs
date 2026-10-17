using Nexaflow.Features.Common;
using Nexaflow.Providers.Common;

namespace Nexaflow.Core.Services.Tools;

/// <summary>
/// A tool Nexaflow runs on this machine with models of its own, as the Local page offers it for download and the
/// Downloaded page lists it for removal — the same for every tool, whatever it does.
/// </summary>
public interface ILocalTool : IToolModels
{
    /// <summary>What it is called where a person sees it.</summary>
    string Name { get; }

    /// <summary>What it does, in a sentence.</summary>
    string About { get; }

    /// <summary>Its own files: what removing it takes away. A runtime it shares with the models stays.</summary>
    IReadOnlyList<ModelAsset> OwnFiles { get; }
}
