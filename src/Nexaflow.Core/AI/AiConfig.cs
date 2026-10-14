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

    /// <summary>Every column some ability's chain names — the ones a workspace needs providers for.</summary>
    public IReadOnlySet<string> AssignedColumnIds()
        => Assignments.Values.SelectMany(c => c.ColumnIds).ToHashSet();

    /// <summary>The Whisper model Speech In starts on.</summary>
    public const string DefaultSpeechModel = "base.en";

    /// <summary>
    /// Gives Speech In its starting chain when this grid has never had that row — a grid saved before abilities of other
    /// kinds existed, or a new workspace: Whisper, on this machine, as the Local column's speech-to-text model, so dictation
    /// works without a key or a subscription. A chain the user has emptied is a chain, and is left alone. Returns whether
    /// anything was added.
    /// </summary>
    public bool AddDefaultSpeechIn(string whisperModel)
    {
        var key = nameof(AiAbility.SpeechIn);
        if (Assignments.ContainsKey(key)) return false;

        var id = LocalColumns.IdOf(LocalSlot.SpeechToText);
        if (Columns.All(c => c.Id != id))
            LocalColumns.Choose(this, LocalSlot.SpeechToText, new ProviderModelPair
            {
                ProviderName     = "Whisper",
                Model            = whisperModel,
                Kind             = ModelKind.SpeechToText,
                AssemblyFileName = "Nexaflow.Providers.Whisper.dll",
            });
        Assignments[key] = AbilityChain.Of(id);
        return true;
    }
}
