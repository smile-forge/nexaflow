using Nexaflow.Core.Controls;
using Nexaflow.Providers.Common;
using System.Text.Json.Serialization;
using ProviderCustomControl = Nexaflow.Providers.Common.CustomControlAttribute;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.AI;

[ProviderCustomControl(typeof(AiAbilityGridControl))]
public sealed class AiConfig : IProviderConfig
{
    public string ConfigName   => "ai-abilities";
    public string FriendlyName => Str.Get("Shell.Config.Ai");

    /// <summary>
    /// The editing workspace's provider set — supplies the ability grid with the provider instances
    /// and configs to list/query. Runtime-only; assigned by <see cref="Services.WorkspaceManager"/>.
    /// </summary>
    [JsonIgnore]
    public ProviderSet? Providers { get; set; }

    /// <summary>Ordered list of provider/model columns the user has configured.</summary>
    public List<ProviderModelPair> Columns { get; set; } = [];

    /// <summary>
    /// Maps AiAbility.ToString() → the chain of columns that serve it. A missing or empty chain means no model.
    /// </summary>
    public Dictionary<string, AbilityChain> Assignments { get; set; } = [];

    /// <summary>Whether the Local column has been turned on for this grid — once, the first time a workspace with it loads, so a
    /// job the user later takes it out of stays out.</summary>
    public bool LocalSeeded { get; set; }

    /// <summary>Every column some ability's chain names — the ones a workspace needs providers for.</summary>
    public IReadOnlySet<string> AssignedColumnIds()
        => Assignments.Values.SelectMany(c => c.ColumnIds).ToHashSet();

    /// <summary>
    /// Gives Speech In its starting chain when this grid has never had that row — a grid saved before abilities of other
    /// kinds existed, or a new workspace: the Local column, whose speech model the workspace settles on when it loads
    /// (<see cref="LocalColumns.Settle"/>), so the mic works without a key or a subscription. A chain the user has emptied
    /// is a chain, and is left alone. Returns whether anything was added.
    /// </summary>
    public bool AddDefaultSpeechIn()
    {
        var key = nameof(AiAbility.SpeechIn);
        if (Assignments.ContainsKey(key)) return false;

        Assignments[key] = AbilityChain.Of(LocalColumns.IdOf(LocalSlot.SpeechToText));
        return true;
    }

    /// <summary>
    /// Gives Documents its starting chain when this grid has never had that row — a grid saved before the row existed: the
    /// models Analysis is given, since working through a document a piece at a time is the same kind of reading. A chain the
    /// user has emptied is a chain, and is left alone. Returns whether anything was added.
    /// </summary>
    public bool AddDefaultDocuments()
    {
        var key = nameof(AiAbility.Documents);
        if (Assignments.ContainsKey(key) || !Assignments.TryGetValue(nameof(AiAbility.Analysis), out var analysis)) return false;

        Assignments[key] = AbilityChain.Of([.. analysis.ColumnIds]);
        return true;
    }
}
