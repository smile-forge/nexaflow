using System.Text.Json;
using Nexaflow.Providers.Common;

namespace Nexaflow.Providers.AiHost;

/// <summary>
/// One model the host loads as part of a model Nexaflow runs itself — a language model is one part; listening is a
/// voice activity detector, a recogniser, a transcriber and a turn detector; speaking is a synthesizer for each set of
/// voices it has.
/// </summary>
/// <param name="Role">What it does in its model — <see cref="OwnedRoles"/>.</param>
/// <param name="Runner">What in the host loads and runs it.</param>
/// <param name="Folder">Where its files are, relative to the asset store.</param>
/// <param name="File">The file it is, for a model that is a single graph.</param>
/// <param name="Adapter">What prepares its files for the runner, if they need it.</param>
/// <param name="MemoryBytes">GPU memory it holds loaded; 0 for a part that runs on the CPU.</param>
/// <param name="ContextTokens">The longest conversation, for a language model.</param>
/// <param name="CudaOptions">CUDA options it needs beyond the host's own.</param>
/// <param name="ExecutionProvider">The one runtime it is built for (<c>cuda</c> or <c>cpu</c>), or null for either.</param>
/// <param name="Name">What it is called in its model: its role, unless the model has several parts in that role.</param>
/// <param name="Projector">A language model's multimodal projector, in its folder: what it sees and hears through.</param>
internal sealed record OwnedPart(string Role, string Runner, string Folder, string? File, string? Adapter, long MemoryBytes, int ContextTokens,
                                 IReadOnlyDictionary<string, string>? CudaOptions, string? ExecutionProvider, string Name, string? Projector = null);

/// <summary>What each part of an owned model does.</summary>
internal static class OwnedRoles
{
    public const string Chat          = "chat";
    public const string VoiceActivity = "voiceActivity";
    public const string Recognizer    = "recognizer";
    public const string Transcriber   = "transcriber";
    public const string TurnDetector  = "turnDetector";
    public const string Synthesizer   = "synthesizer";
    public const string PromptEncoder = "promptEncoder";
    public const string Denoiser      = "denoiser";
    public const string Decoder       = "decoder";

    /// <summary>The parts a model of each kind is made of.</summary>
    public static IReadOnlyList<string> Of(ModelKind kind) => kind switch
    {
        ModelKind.Language     => [Chat],
        ModelKind.SpeechToText => [VoiceActivity, Recognizer, Transcriber, TurnDetector],
        ModelKind.TextToSpeech    => [Synthesizer],
        ModelKind.ImageGeneration => [PromptEncoder, Denoiser, Decoder],
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "no owned model is of this kind"),
    };

    /// <summary>Whether a model may have several parts in <paramref name="role"/>, each named: a voice model's
    /// synthesizers — the German voice is a fine-tune of its own.</summary>
    public static bool Repeats(string role) => role == Synthesizer;
}

/// <summary>
/// A model Nexaflow runs itself: its catalog entry — every file any machine might need — its parts, and, for a model
/// that speaks, its voices. A part built for one runtime (Whisper's fp16 build for CUDA, its int8 build for the CPU) is
/// fetched and run only on that runtime. A model that draws says how it samples.
/// </summary>
internal sealed record OwnedModel(CatalogModel Catalog, ModelKind Kind, IReadOnlyList<OwnedPart> Parts, IReadOnlyList<OwnedVoice> Voices,
                                  bool Images, bool Audio, bool ToolCalls = false, OwnedSampler? Sampler = null)
{
    public string Id => Catalog.Id;

    /// <summary>Whether it has a build for <paramref name="executionProvider"/> — an image model is built for CUDA only.</summary>
    public bool RunsOn(string executionProvider) => PartsFor(executionProvider).Count > 0;

    /// <summary>The parts that run on <paramref name="executionProvider"/>: one per role.</summary>
    public IReadOnlyList<OwnedPart> PartsFor(string executionProvider)
        => [.. Parts.Where(p => p.ExecutionProvider is null || p.ExecutionProvider == executionProvider)];

    public OwnedPart Part(string role, string executionProvider) => PartsFor(executionProvider).Single(p => p.Role == role);

    /// <summary>The part called <paramref name="name"/> that runs on <paramref name="executionProvider"/>.</summary>
    public OwnedPart PartNamed(string name, string executionProvider) => PartsFor(executionProvider).Single(p => p.Name == name);

    /// <summary>The catalog entry as it stands on a machine running <paramref name="executionProvider"/>: only the files
    /// its parts there need.</summary>
    public CatalogModel CatalogFor(string executionProvider)
    {
        var folders = PartsFor(executionProvider).Select(p => p.Folder + "/").ToList();
        return Catalog with { Files = [.. Catalog.Files.Where(f => folders.Any(folder => f.RelativePath.StartsWith(folder, StringComparison.Ordinal)))] };
    }
}

/// <summary>A voice of a model that speaks: the language it reads, the part that speaks it, its file in that part's
/// folder, and — for a language read from a lexicon — the lexicon's file there.</summary>
internal sealed record OwnedVoice(string Language, string Part, string File, string? Lexicon = null);

/// <summary>How a model that draws samples: its number of denoising steps and the shift of the noise levels they run through.</summary>
internal sealed record OwnedSampler(int Steps, float Shift);

/// <summary>
/// The models in <c>owned-models.json</c>: a model catalog (<see cref="ModelCatalog"/>) whose entries also say what kind
/// of model each is and how the host runs its parts.
/// </summary>
internal static class OwnedModels
{
    public const string Cuda = "cuda";
    public const string Cpu  = "cpu";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private sealed record Manifest(List<Entry>? Models);
    private sealed record Entry(string? Id, ModelKind? Kind, bool Images, bool Audio, bool ToolCalls, List<Part>? Parts, List<Voice>? Voices, Sampler? Sampler);
    private sealed record Sampler(int Steps, float Shift);
    private sealed record Voice(string? Language, string? Part, string? File, string? Lexicon);
    private sealed record Part(string? Role, string? Runner, string? Folder, string? File, string? Adapter, long MemoryBytes, int ContextTokens,
                               Dictionary<string, string>? CudaOptions, string? ExecutionProvider, string? Name, string? Projector);

    public static IReadOnlyList<OwnedModel> All { get; } = Read();

    /// <summary>The models of <paramref name="kind"/>.</summary>
    public static IEnumerable<OwnedModel> OfKind(ModelKind kind) => All.Where(m => m.Kind == kind);

    private static IReadOnlyList<OwnedModel> Read()
    {
        using var stream = typeof(OwnedModels).Assembly.GetManifestResourceStream("owned-models.json")
                           ?? throw new InvalidOperationException("The owned-model catalog is missing from its assembly.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return Read(copy.ToArray());
    }

    /// <exception cref="InvalidDataException">A model does not say what it is or how its parts run, lacks a part its
    /// kind needs on a runtime, or has a file outside its parts' folders.</exception>
    internal static IReadOnlyList<OwnedModel> Read(byte[] json)
    {
        var catalog = ModelCatalog.Read(new MemoryStream(json)).ToDictionary(m => m.Id);
        var entries = JsonSerializer.Deserialize<Manifest>(json, Options)?.Models ?? [];
        return [.. entries.Select(e =>
        {
            if (e.Id is null || !catalog.TryGetValue(e.Id, out var model)) throw new InvalidDataException("An owned model has no id.");
            if (e.Kind is not { } kind) throw new InvalidDataException($"Owned model '{e.Id}' does not say what kind of model it is.");

            var parts  = (e.Parts ?? []).Select(p => ToPart(e.Id, kind, p)).ToList();
            var voices = (e.Voices ?? []).Select(v => ToVoice(e.Id, model, parts, v)).ToList();
            if (kind == ModelKind.TextToSpeech && voices.Count == 0) throw new InvalidDataException($"Owned model '{e.Id}' speaks with no voice.");
            if (kind == ModelKind.ImageGeneration && e.Sampler is not { Steps: > 0, Shift: > 0 })
                throw new InvalidDataException($"Owned model '{e.Id}' draws without saying how it samples.");
            if (kind == ModelKind.Language && (e.Images || e.Audio) && parts.Any(p => p.Projector is null))
                throw new InvalidDataException($"Owned model '{e.Id}' sees or hears with no projector to do it through.");
            var sampler = e.Sampler is { } s ? new OwnedSampler(s.Steps, s.Shift) : null;
            var owned = new OwnedModel(model, kind, parts, voices, e.Images, e.Audio, e.ToolCalls, sampler);
            if (!owned.RunsOn(Cuda) && !owned.RunsOn(Cpu)) throw new InvalidDataException($"Owned model '{e.Id}' runs on no runtime.");
            foreach (var runtime in new[] { Cuda, Cpu })
            {
                var onRuntime = owned.PartsFor(runtime);
                if (onRuntime.Count == 0) continue;   // not built for this runtime
                var roles = onRuntime.Select(p => p.Role).ToList();
                var needed = OwnedRoles.Of(kind);
                bool complete = needed.All(roles.Contains) && roles.All(needed.Contains)
                             && roles.Where(r => !OwnedRoles.Repeats(r)).GroupBy(r => r).All(g => g.Count() == 1)
                             && onRuntime.GroupBy(p => p.Name).All(g => g.Count() == 1);
                if (!complete)
                    throw new InvalidDataException($"Owned model '{e.Id}' needs one part each of {string.Join(", ", needed)} on {runtime}, where it runs — several synthesizers each named — and has {string.Join(", ", onRuntime.Select(p => p.Name))}.");
            }
            var folders = parts.Select(p => p.Folder + "/").Distinct().ToList();
            if (model.Files.Any(f => !folders.Any(folder => f.RelativePath.StartsWith(folder, StringComparison.Ordinal))))
                throw new InvalidDataException($"Owned model '{e.Id}' has files outside its parts' folders.");
            if (parts.Any(p => !model.Files.Any(f => f.RelativePath.StartsWith(p.Folder + "/", StringComparison.Ordinal))))
                throw new InvalidDataException($"Owned model '{e.Id}' has a part with no files.");
            return owned;
        })];
    }

    private static OwnedPart ToPart(string modelId, ModelKind kind, Part p)
    {
        if (string.IsNullOrWhiteSpace(p.Role) || string.IsNullOrWhiteSpace(p.Runner) || string.IsNullOrWhiteSpace(p.Folder))
            throw new InvalidDataException($"A part of owned model '{modelId}' does not say what it is, what runs it or where it is.");
        if (p.ExecutionProvider is not (null or Cuda or Cpu))
            throw new InvalidDataException($"Owned model '{modelId}' has a part for '{p.ExecutionProvider}', a runtime the host does not have.");
        if (kind == ModelKind.Language && (p.MemoryBytes <= 0 || p.ContextTokens <= 0))
            throw new InvalidDataException($"Owned model '{modelId}' does not say how much it holds or how long a conversation may grow.");
        // A picture's parts load on demand against the GPU budget, so what each holds at its peak must be known.
        if (kind == ModelKind.ImageGeneration && p.ExecutionProvider != Cpu && p.MemoryBytes <= 0)
            throw new InvalidDataException($"Owned model '{modelId}' does not say how much its {p.Role} holds.");
        return new OwnedPart(p.Role, p.Runner, p.Folder, p.File, p.Adapter, p.MemoryBytes, p.ContextTokens, p.CudaOptions, p.ExecutionProvider,
                             p.Name ?? p.Role, p.Projector);
    }

    private static OwnedVoice ToVoice(string modelId, CatalogModel model, List<OwnedPart> parts, Voice v)
    {
        if (string.IsNullOrWhiteSpace(v.Language) || string.IsNullOrWhiteSpace(v.File) || parts.FirstOrDefault(p => p.Name == v.Part) is not { } part)
            throw new InvalidDataException($"A voice of owned model '{modelId}' does not say what it reads, which part speaks it, or its file.");
        foreach (var file in new[] { v.File, v.Lexicon }.OfType<string>())
            if (!model.Files.Any(f => f.RelativePath == $"{part.Folder}/{file}"))
                throw new InvalidDataException($"Owned model '{modelId}' has a voice that needs {file}, which is not among its files.");
        return new OwnedVoice(v.Language, part.Name, v.File, v.Lexicon);
    }
}
