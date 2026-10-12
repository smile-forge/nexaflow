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
    /// Every language ability served by <paramref name="bridged"/>, by <paramref name="owned"/>, or by both — then Speech
    /// In on <paramref name="ownedSpeech"/> where Nexaflow has its own, with Whisper behind it, which is all a bridged
    /// workspace listens with. At least one language model must be given.
    /// </summary>
    public static void Assign(AiConfig config, ProviderModelPair? bridged, ProviderModelPair? owned, ProviderModelPair? ownedSpeech, string whisperModel)
    {
        if (bridged is null && owned is null) throw new ArgumentException("A workspace needs a model.");

        config.Columns = [.. new[] { bridged, owned }.OfType<ProviderModelPair>()];
        config.Assignments.Clear();
        foreach (var ability in Enum.GetValues<AiAbility>())
        {
            if (AiAbilityNames.Kind(ability) != ModelKind.Language) continue;
            config.Assignments[ability.ToString()] = (bridged, owned) switch
            {
                ({ } b, { } o) => FrequentRoles.Contains(ability) ? AbilityChain.Of(o.Id, b.Id) : AbilityChain.Of(b.Id, o.Id),
                ({ } b, null)  => AbilityChain.Of(b.Id),
                _              => AbilityChain.Of(owned!.Id),
            };
        }

        config.AddDefaultSpeechIn(whisperModel);
        if (ownedSpeech is null) return;
        var speechIn = nameof(AiAbility.SpeechIn);
        config.Columns.Add(ownedSpeech);
        config.Assignments[speechIn] = AbilityChain.Of([ownedSpeech.Id, .. config.Assignments[speechIn].ColumnIds]);
    }
}
