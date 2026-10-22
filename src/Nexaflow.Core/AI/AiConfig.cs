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

    /// <summary>Jobs the workspace's setup gave models other than the Local column's — a workspace set up to use a provider alone
    /// keeps its reasoning rows on that provider. Turning the Local column on (<see cref="LocalColumns.Seed"/>) still chooses
    /// their model, so the column can be ticked in them, but leaves them unticked.</summary>
    public List<LocalSlot> LocalDeclined { get; set; } = [];

    /// <summary>Every column some ability's chain names — the ones a workspace needs providers for.</summary>
    public IReadOnlySet<string> AssignedColumnIds()
        => Assignments.Values.SelectMany(c => c.ColumnIds).ToHashSet();

    /// <summary>
    /// Brings a grid saved by an earlier version up to the abilities there are now — <see cref="FoldIntoGeneralIntelligence"/>,
    /// then <see cref="AddDefaultSpeechIn"/> and <see cref="AddDefaultDocuments"/>. Run on every grid as it is read; a grid already
    /// up to date is left as it is.
    /// Returns whether anything changed, so the caller saves it.
    /// </summary>
    public bool BringUpToDate() => FoldIntoGeneralIntelligence() | AddDefaultSpeechIn() | AddDefaultDocuments();

    /// <summary>The rows General Intelligence took the place of, as a grid saved before it names them.</summary>
    private static readonly string[] FoldedIntoGeneralIntelligence = ["Conversation", "ProblemSolving", "Analysis"];

    /// <summary>
    /// The Conversation row of a grid saved before General Intelligence becomes General Intelligence's chain, as it was; the
    /// Problem Solving and Analysis rows go. A grid that already has General Intelligence keeps it. Returns whether anything
    /// changed.
    /// </summary>
    public bool FoldIntoGeneralIntelligence()
    {
        bool changed = false;
        var key = nameof(AiAbility.GeneralIntelligence);
        if (!Assignments.ContainsKey(key) && Assignments.TryGetValue("Conversation", out var conversation))
        {
            Assignments[key] = conversation;
            changed = true;
        }
        foreach (var folded in FoldedIntoGeneralIntelligence) changed |= Assignments.Remove(folded);
        return changed;
    }

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
    /// models General Intelligence is given, since working through a document is the same kind of thinking, done a piece at a
    /// time. A chain the user has emptied is a chain, and is left alone. Returns whether anything was added.
    /// </summary>
    public bool AddDefaultDocuments()
    {
        var key = nameof(AiAbility.Documents);
        if (Assignments.ContainsKey(key) || !Assignments.TryGetValue(nameof(AiAbility.GeneralIntelligence), out var general)) return false;

        Assignments[key] = AbilityChain.Of([.. general.ColumnIds]);
        return true;
    }
}
