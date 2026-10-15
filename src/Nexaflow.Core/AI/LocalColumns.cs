using Nexaflow.Providers.Common;

namespace Nexaflow.Core.AI;

/// <summary>A job the models on this machine do for a workspace, each with one model chosen for it.</summary>
public enum LocalSlot { Language, SpeechToText, TextToSpeech, ImageGeneration, HighQualityImageGeneration, VideoGeneration }

/// <summary>
/// The ability grid's Local column: the models that run on this machine — Nexaflow's own, and whisper.cpp's for dictation
/// where this machine runs none of Nexaflow's speech models — one chosen for each
/// job (<see cref="LocalSlot"/>). Each job's model is saved as a column of its own under a reserved id, so a chain names it
/// like any other column and the rest of the app needs to know nothing of it; on screen the grid shows them as one column,
/// whose cell in a row is the row's job's model.
/// </summary>
public static class LocalColumns
{
    private const string Prefix = "local-";

    /// <summary>The assemblies of the Local column's providers — Nexaflow's own models and whisper.cpp — loaded for every workspace,
    /// since every workspace has the column.</summary>
    public static readonly IReadOnlyList<string> Assemblies = ["Nexaflow.Providers.AiHost.dll", "Nexaflow.Providers.Whisper.dll"];

    /// <summary>
    /// Speech to text where this machine runs none of Nexaflow's own speech models: Whisper base on whisper.cpp, which
    /// dictates into the box. It hears words but not when a speaker has finished, so it holds no conversation; Nexaflow's
    /// own speech models always do.
    /// </summary>
    public static ProviderModelPair Dictation { get; } = new()
    {
        ProviderName = "Whisper", Model = "base", Kind = ModelKind.SpeechToText, AssemblyFileName = "Nexaflow.Providers.Whisper.dll",
    };

    /// <summary>Whether <paramref name="column"/> is whisper.cpp's — a model that only dictates.</summary>
    public static bool Dictates(ProviderModelPair column) => string.Equals(column.ProviderName, Dictation.ProviderName, StringComparison.OrdinalIgnoreCase);

    /// <summary>The reserved id of <paramref name="slot"/>'s column.</summary>
    public static string IdOf(LocalSlot slot) => Prefix + slot switch
    {
        LocalSlot.Language                   => "language",
        LocalSlot.SpeechToText               => "speech-to-text",
        LocalSlot.TextToSpeech               => "text-to-speech",
        LocalSlot.ImageGeneration            => "image-generation",
        LocalSlot.HighQualityImageGeneration => "high-quality-image-generation",
        _                                    => "video-generation",
    };

    /// <summary>Whether <paramref name="columnId"/> is a Local column's.</summary>
    public static bool IsLocal(string columnId) => columnId.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>The job whose model serves <paramref name="ability"/>.</summary>
    public static LocalSlot SlotOf(AiAbility ability) => ability switch
    {
        AiAbility.SpeechIn                   => LocalSlot.SpeechToText,
        AiAbility.SpeechOut                  => LocalSlot.TextToSpeech,
        AiAbility.ImageGeneration            => LocalSlot.ImageGeneration,
        AiAbility.HighQualityImageGeneration => LocalSlot.HighQualityImageGeneration,
        AiAbility.VideoGeneration            => LocalSlot.VideoGeneration,
        _                                    => LocalSlot.Language,
    };

    /// <summary>The job a model of <paramref name="kind"/> does first — a picture model's is the standard one.</summary>
    public static LocalSlot SlotOf(ModelKind kind) => kind switch
    {
        ModelKind.SpeechToText    => LocalSlot.SpeechToText,
        ModelKind.TextToSpeech    => LocalSlot.TextToSpeech,
        ModelKind.ImageGeneration => LocalSlot.ImageGeneration,
        ModelKind.VideoGeneration => LocalSlot.VideoGeneration,
        _                         => LocalSlot.Language,
    };

    /// <summary>The kind of model <paramref name="slot"/> takes.</summary>
    public static ModelKind KindOf(LocalSlot slot) => slot switch
    {
        LocalSlot.SpeechToText                                             => ModelKind.SpeechToText,
        LocalSlot.TextToSpeech                                             => ModelKind.TextToSpeech,
        LocalSlot.ImageGeneration or LocalSlot.HighQualityImageGeneration => ModelKind.ImageGeneration,
        LocalSlot.VideoGeneration                                          => ModelKind.VideoGeneration,
        _                                                                  => ModelKind.Language,
    };

    /// <summary>Which models a job is made for: high-quality pictures want the models built for quality, every other job the
    /// everyday ones.</summary>
    public static ModelTier TierOf(LocalSlot slot) => slot == LocalSlot.HighQualityImageGeneration ? ModelTier.HighQuality : ModelTier.Standard;

    /// <summary>The abilities <paramref name="slot"/>'s model serves.</summary>
    public static IReadOnlyList<AiAbility> AbilitiesOf(LocalSlot slot) => [.. Enum.GetValues<AiAbility>().Where(a => SlotOf(a) == slot)];

    /// <summary>
    /// Folds a grid's columns of models on this machine that predate the Local column into it: in each chain, the first such
    /// column takes the place of its job's Local column — whose model it becomes when the job has none yet — and any after it
    /// for the same job leave; then they are gone. Returns whether anything changed.
    /// </summary>
    /// <param name="isLocal">Whether a column's model runs on this machine — its provider is Nexaflow's own harness.</param>
    public static bool Gather(AiConfig config, Func<ProviderModelPair, bool> isLocal)
    {
        var old = config.Columns.Where(c => !IsLocal(c.Id) && isLocal(c)).ToList();
        if (old.Count == 0) return false;

        var byId = old.ToDictionary(c => c.Id);
        foreach (var ability in Enum.GetValues<AiAbility>())
        {
            var key = ability.ToString();
            if (!config.Assignments.TryGetValue(key, out var chain)) continue;

            var slot = SlotOf(ability);
            var id   = IdOf(slot);
            var ids  = new List<string>();
            foreach (var columnId in chain.ColumnIds)
            {
                if (!byId.TryGetValue(columnId, out var column)) { if (!ids.Contains(columnId)) ids.Add(columnId); continue; }
                if (ids.Contains(id)) continue;
                if (config.Columns.All(c => c.Id != id)) config.Columns.Add(Copy(column, id));
                ids.Add(id);
            }
            config.Assignments[key] = new AbilityChain(ids);
        }
        config.Columns.RemoveAll(c => byId.ContainsKey(c.Id));
        return true;
    }

    /// <summary>Makes <paramref name="model"/> <paramref name="slot"/>'s model, in place of the one it had.</summary>
    public static void Choose(AiConfig config, LocalSlot slot, ProviderModelPair model)
    {
        var id = IdOf(slot);
        config.Columns.RemoveAll(c => c.Id == id);
        config.Columns.Add(Copy(model, id));
    }

    /// <summary>
    /// Makes <paramref name="model"/> <paramref name="slot"/>'s model; a job none of whose rows uses the Local column yet starts
    /// using it in every one, at the end of each chain — the Local column is on wherever it can be.
    /// </summary>
    public static void Use(AiConfig config, LocalSlot slot, ProviderModelPair model)
    {
        Choose(config, slot, model);
        var id   = IdOf(slot);
        var keys = AbilitiesOf(slot).Select(a => a.ToString()).ToList();
        if (keys.Any(k => config.Assignments.GetValueOrDefault(k)?.ColumnIds.Contains(id) == true)) return;
        foreach (var key in keys)
            config.Assignments[key] = new AbilityChain([.. config.Assignments.GetValueOrDefault(key)?.ColumnIds ?? [], id]);
    }

    /// <summary>
    /// Turns the Local column on for a grid that has never had it: each job this machine runs a model for, and none is chosen
    /// for, gets its first model — one made for the job before any other, and of those one already here — and every row of
    /// the job uses it. Nothing is downloaded: a model not here is the grid's to offer. Returns whether anything changed.
    /// </summary>
    /// <param name="options">The models each job can hold; whether this machine runs each, whether it is here, and whether it
    /// is made for the job (<see cref="TierOf"/>).</param>
    public static bool Seed(AiConfig config, Func<LocalSlot, IReadOnlyList<(ProviderModelPair Model, bool Runs, bool Here, bool Suits)>> options)
    {
        if (config.LocalSeeded) return false;
        foreach (var slot in Enum.GetValues<LocalSlot>())
        {
            if (config.Columns.Any(c => c.Id == IdOf(slot))) continue;
            if (FirstOf(options(slot)) is { } first) Use(config, slot, first);
        }
        config.LocalSeeded = true;
        return true;
    }

    /// <summary>
    /// Keeps every job a row uses the Local column for on a model the job offers here: one with no model yet, or whose model
    /// the job no longer offers — whisper.cpp's sizes where this machine runs Nexaflow's own speech models, or those models
    /// where it can only dictate — gets the job's first model, as <see cref="Seed"/> chooses. Nothing is downloaded: a model
    /// not here is the grid's to offer. A job that offers nothing here is left as it is. Returns whether anything changed.
    /// </summary>
    /// <param name="options">The models each job offers, as for <see cref="Seed"/>.</param>
    public static bool Settle(AiConfig config, Func<LocalSlot, IReadOnlyList<(ProviderModelPair Model, bool Runs, bool Here, bool Suits)>> options)
    {
        bool changed = false;
        foreach (var slot in Enum.GetValues<LocalSlot>())
        {
            var id = IdOf(slot);
            if (!AbilitiesOf(slot).Any(a => config.Assignments.GetValueOrDefault(a.ToString())?.ColumnIds.Contains(id) == true)) continue;

            var offered = options(slot);
            var chosen  = config.Columns.FirstOrDefault(c => c.Id == id);
            if (chosen is not null && offered.Any(o => o.Model.Model == chosen.Model
                                                     && string.Equals(o.Model.ProviderName, chosen.ProviderName, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (FirstOf(offered) is not { } first) continue;
            Choose(config, slot, first);
            changed = true;
        }
        return changed;
    }

    /// <summary>The model a job starts on: one that runs here, made for the job before any other, and of those one already here.</summary>
    private static ProviderModelPair? FirstOf(IReadOnlyList<(ProviderModelPair Model, bool Runs, bool Here, bool Suits)> options)
        => options.Where(o => o.Runs).OrderBy(o => o.Suits ? 0 : 1).ThenBy(o => o.Here ? 0 : 1).Select(o => o.Model).FirstOrDefault();

    private static ProviderModelPair Copy(ProviderModelPair column, string id) => new()
    {
        Id = id, ProviderName = column.ProviderName, Model = column.Model, Kind = column.Kind, AssemblyFileName = column.AssemblyFileName,
    };
}
