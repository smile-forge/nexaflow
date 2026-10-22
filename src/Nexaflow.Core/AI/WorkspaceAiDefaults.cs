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
/// The roles called most and needing least — routing a request, describing an image, working through a document a piece
/// at a time — which a mixed workspace gives to the model on this machine first: that is where API cost actually goes.
/// General Intelligence holds both models with the chosen provider first; each request is judged, so light work still runs
/// on this machine.
/// </summary>
internal static readonly IReadOnlySet<AiAbility> FrequentRoles =
    new HashSet<AiAbility> { AiAbility.Disambiguation, AiAbility.ImageRecognition, AiAbility.Documents };

    /// <summary>
    /// The grid a setup chooses. Nexaflow alone: the Local column in every row it serves. A provider alone: the provider's
    /// language model in every reasoning row and nothing else — the rows its other models take are filled by
    /// <see cref="AssignKinds"/> — with the Local column keeping a model chosen for each job, ticked in none. Both: the
    /// reasoning rows hold both, Disambiguation and Image Recognition this machine's model first and General Intelligence the
    /// provider's first, each request judged; the Local column takes every other row it serves. Speech In and Speech Out start
    /// on Nexaflow's own speech models where there are some. The Local column is turned on afresh for the grid this makes
    /// (<see cref="LocalColumns.Seed"/>). At least one language model must be given.
    /// </summary>
    public static void Assign(AiConfig config, ProviderModelPair? bridged, OwnedColumns? owned)
    {
        var ownedLanguage = owned?.Language;
        if (bridged is null && ownedLanguage is null) throw new ArgumentException("A workspace needs a model.");

        config.Columns = [.. new[] { bridged }.OfType<ProviderModelPair>()];
        if (ownedLanguage is not null) LocalColumns.Choose(config, LocalSlot.Language, ownedLanguage);
        config.LocalSeeded   = false;
        config.LocalDeclined = ownedLanguage is null ? [.. Enum.GetValues<LocalSlot>()] : [];
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

        // Speech In holds what the setup gives it, or nothing: a grid with the row is not given the default later.
        config.Assignments[nameof(AiAbility.SpeechIn)] = AbilityChain.Empty;
        if (owned?.SpeechIn is { } speechIn)
        {
            LocalColumns.Choose(config, LocalSlot.SpeechToText, speechIn);
            config.Assignments[nameof(AiAbility.SpeechIn)] = AbilityChain.Of(LocalColumns.IdOf(LocalSlot.SpeechToText));
        }
        if (owned?.SpeechOut is { } speechOut)
        {
            LocalColumns.Choose(config, LocalSlot.TextToSpeech, speechOut);
            config.Assignments[nameof(AiAbility.SpeechOut)] = AbilityChain.Of(LocalColumns.IdOf(LocalSlot.TextToSpeech));
        }
    }

    /// <summary>
    /// The provider's other models a setup chose — speech in and out, pictures, video — each a column of its own, first in
    /// every row that takes its kind.
    /// </summary>
    public static void AssignKinds(AiConfig config, IEnumerable<ProviderModelPair> chosen)
    {
        foreach (var column in chosen)
        {
            config.Columns.Add(column);
            foreach (var ability in Enum.GetValues<AiAbility>().Where(a => AiAbilityNames.Kind(a) == column.Kind))
            {
                var key = ability.ToString();
                config.Assignments[key] = AbilityChain.Of([column.Id, .. config.Assignments.GetValueOrDefault(key)?.ColumnIds ?? []]);
            }
        }
    }
}
