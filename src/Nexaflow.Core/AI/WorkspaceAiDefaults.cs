using Nexaflow.Providers.Common;

namespace Nexaflow.Core.AI;

/// <summary>Who runs a workspace's AI, as its setup chooses.</summary>
public enum WorkspaceHarness
{
    /// <summary>A provider the user chooses — Nexaflow is its client.</summary>
    Bridged,
    /// <summary>Nexaflow runs the model itself; nothing leaves the machine.</summary>
    Owned,
    /// <summary>Both: the frequent, cheap roles go to Nexaflow's model first, conversation to the chosen provider.</summary>
    Mixed,
}

/// <summary>The models of Nexaflow's own a workspace starts with: a language model, and the speech models where there are some.</summary>
public sealed record OwnedColumns(ProviderModelPair Language, ProviderModelPair? SpeechIn, ProviderModelPair? SpeechOut);

/// <summary>The ability grid a newly set-up workspace starts with.</summary>
internal static class WorkspaceAiDefaults
{
    /// <summary>
    /// The roles called most and needing least — routing a request, analysing a page, describing an image — which
    /// a mixed workspace gives to the model on this machine first: that is where API cost actually goes. The
    /// conversation itself stays with the chosen provider, the stronger model, and falls back to the owned one.
    /// </summary>
    internal static readonly IReadOnlySet<AiAbility> FrequentRoles =
        new HashSet<AiAbility> { AiAbility.Disambiguation, AiAbility.Analysis, AiAbility.ImageRecognition };

    /// <summary>
    /// Every language ability served by <paramref name="bridged"/>, by <paramref name="owned"/>'s language model, or by both;
    /// Speech In on the Local column — Nexaflow's own speech model where there is one, otherwise the one the workspace settles
    /// on when it loads (<see cref="LocalColumns.Settle"/>), which is all a bridged workspace listens with; and Speech Out on
    /// Nexaflow's own voice where there is one. At least one language model must be given.
    /// </summary>
    public static void Assign(AiConfig config, ProviderModelPair? bridged, OwnedColumns? owned)
    {
        var ownedLanguage = owned?.Language;
        if (bridged is null && ownedLanguage is null) throw new ArgumentException("A workspace needs a model.");

        config.Columns = [.. new[] { bridged }.OfType<ProviderModelPair>()];
        if (ownedLanguage is not null) LocalColumns.Choose(config, LocalSlot.Language, ownedLanguage);
        var local = LocalColumns.IdOf(LocalSlot.Language);
        config.Assignments.Clear();
        foreach (var ability in Enum.GetValues<AiAbility>())
        {
            if (AiAbilityNames.Kind(ability) != ModelKind.Language) continue;
            config.Assignments[ability.ToString()] = (bridged, ownedLanguage) switch
            {
                ({ } b, { }) => FrequentRoles.Contains(ability) ? AbilityChain.Of(local, b.Id) : AbilityChain.Of(b.Id, local),
                ({ } b, null) => AbilityChain.Of(b.Id),
                _             => AbilityChain.Of(local),
            };
        }

        config.AddDefaultSpeechIn();
        if (owned?.SpeechIn is { } speechIn) LocalColumns.Choose(config, LocalSlot.SpeechToText, speechIn);
        if (owned?.SpeechOut is { } speechOut)
        {
            LocalColumns.Choose(config, LocalSlot.TextToSpeech, speechOut);
            config.Assignments[nameof(AiAbility.SpeechOut)] = AbilityChain.Of(LocalColumns.IdOf(LocalSlot.TextToSpeech));
        }
    }
}
