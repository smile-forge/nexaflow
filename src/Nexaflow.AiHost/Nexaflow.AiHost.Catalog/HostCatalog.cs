using System.Text.Json;
using System.Text.Json.Serialization;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Provisioning;

namespace Nexaflow.AiHost.Catalog;

/// <summary>A catalog's models and its tools.</summary>
public sealed record HostedContents(IReadOnlyList<HostedModel> Models, IReadOnlyList<HostedTool> Tools);

/// <summary>
/// The models the AI host runs, as <c>models.json</c> lists them: for each, an id, a display name, the job of its kind it is made
/// for (<c>"tier": "highQuality"</c>; standard when it says nothing), what kind of model it is, its files with their source, size
/// and SHA-256 (<see cref="ModelAssetEntry"/> — a file published inside an archive gives the archive's address, and says which
/// entry it is and what the archive must be), and how the host runs each of its parts; beside them, the tools, in the same shape.
/// </summary>
public static partial class HostCatalog
{
    public const string Cuda = "cuda";
    public const string Cpu  = "cpu";

    /// <summary>The shortest context a language model is given: room for the page it works on, its tools and a reply.</summary>
    public const int ShortestContext = 8192;

    /// <summary>The catalog this assembly ships, read once for its models and its tools alike.</summary>
    private static readonly HostedContents Shipped = Read(Embedded());

    public static IReadOnlyList<HostedModel> Models => Shipped.Models;

    /// <summary>The tools' entries: the models each tool works with.</summary>
    public static IReadOnlyList<HostedTool> Tools => Shipped.Tools;

    /// <summary>The models of <paramref name="kind"/>.</summary>
    public static IEnumerable<HostedModel> OfKind(HostedKind kind) => Models.Where(m => m.Kind == kind);

    /// <summary>The entry — a model or a tool — whose id is <paramref name="id"/>; null where the catalog has none.</summary>
    public static HostedEntry? Find(string id)
        => (HostedEntry?)Models.FirstOrDefault(m => m.Id == id) ?? Tools.FirstOrDefault(t => t.Id == id);

    /// <exception cref="InvalidDataException">The catalog is malformed: a model or tool does not say what it is or how its parts
    /// run, lacks a part its kind needs on a runtime, or has a file outside its parts' folders.</exception>
    public static HostedContents Read(byte[] json)
    {
        Manifest manifest;
        try { manifest = JsonSerializer.Deserialize(json, ManifestJson.Default.Manifest) ?? new Manifest(null, null); }
        catch (JsonException ex) { throw new InvalidDataException($"The model catalog does not read: {ex.Message}", ex); }
        return new([.. (manifest.Models ?? []).Select(ToModel)], [.. (manifest.Tools ?? []).Select(ToTool)]);
    }

    /// <summary>The manifest's shape, read through serializers generated at build time rather than built by reflection on
    /// first use — the shipped catalog is read at startup.</summary>
    [JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, UseStringEnumConverter = true)]
    [JsonSerializable(typeof(Manifest))]
    private sealed partial class ManifestJson : JsonSerializerContext;

    private sealed record Manifest(List<Entry>? Models, List<ToolEntry>? Tools);
    private sealed record ToolEntry(string? Id, string? DisplayName, List<ModelAssetEntry>? Files, HostedToolKind? Kind, List<Part>? Parts);
    private sealed record Entry(string? Id, string? DisplayName, List<ModelAssetEntry>? Files, HostedTier? Tier, HostedKind? Kind, bool Images, bool Audio,
                                bool ToolCalls, List<Part>? Parts, List<Voice>? Voices, Sampler? Sampler, Choosing? TokenSampling, Filming? Filming,
                                List<Lora>? Loras);
    private sealed record Lora(string? Id, string? DisplayName, string? Description, string? Trigger, float Strength, bool Adult, string? Adapter,
                               string? Sampler, List<LoraFile>? Files);
    private sealed record LoraFile(string? Path, string? Url, long Size, string? Sha256);
    private sealed record Filming(FilmingMethod? Method, int Steps, float Shift, float Guidance, List<int>? Timesteps, string? NegativePrompt,
                                  int FramesPerSecond, List<int>? Seconds, Dictionary<string, int[]>? Sizes, bool StartsFromPictures);
    private sealed record Choosing(Choice? Thinking, Choice? Answering);
    private sealed record Choice(float Temperature, int TopK, float TopP, float PresencePenalty);
    private sealed record Sampler(DrawingMethod? Method, int Steps, float Shift, float Guidance, Dictionary<string, int[]>? Sizes, bool NegativePrompts,
                                  Editing? Editing);
    private sealed record Editing(int Sources, bool Masks);
    private sealed record Voice(string? Language, string? Part, string? File, string? Lexicon);
    private sealed record Part(string? Role, string? Runner, string? Folder, string? File, long MemoryBytes, int ContextTokens,
                               Dictionary<string, string>? CudaOptions, string? ExecutionProvider, string? Name, string? Projector, long BytesPerToken,
                               bool RunsOnCpu, bool LoadsOnDemand, int Layers = 0, long BatchBytesPerToken = 0,
                               long BytesPerSequence = 0, long BackgroundBytes = 0, long StateBytes = 0, long ExpertBytes = 0,
                               StreamedBuild? Streamed = null, TiledBuild? Tiled = null);
    private sealed record StreamedBuild(string? File, long MemoryBytes, long HostBytes);
    private sealed record TiledBuild(int Cells, long MemoryBytes, long HostBytes);

    private static byte[] Embedded()
    {
        using var stream = typeof(HostCatalog).Assembly.GetManifestResourceStream("models.json")
                           ?? throw new InvalidOperationException("The model catalog is missing from its assembly.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>An entry's id, display name and files, held to the same rules for a model and a tool.</summary>
    private static (string Id, string DisplayName, IReadOnlyList<ModelAsset> Files) Identity(string what, string? id, string? displayName,
                                                                                         List<ModelAssetEntry>? files)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new InvalidDataException($"A catalog {what} has no id.");
        if (files is not { Count: > 0 }) throw new InvalidDataException($"Catalog {what} '{id}' lists no files.");
        return (id, string.IsNullOrWhiteSpace(displayName) ? id : displayName, [.. files.Select(f => f.ToAsset(id))]);
    }

    private static HostedModel ToModel(Entry e)
    {
        var (id, displayName, files) = Identity("model", e.Id, e.DisplayName, e.Files);
        if (e.Kind is not { } kind) throw new InvalidDataException($"Model '{id}' does not say what kind of model it is.");

        bool onDemand = kind is HostedKind.ImageGeneration or HostedKind.VideoGeneration;
        var parts  = (e.Parts ?? []).Select(p => ToPart("Model", id, p, language: kind == HostedKind.Language, onDemand)).ToList();
        var voices = (e.Voices ?? []).Select(v => ToVoice(id, files, parts, v)).ToList();
        if (kind == HostedKind.TextToSpeech && voices.Count == 0) throw new InvalidDataException($"Model '{id}' speaks with no voice.");
        var sampler = e.Sampler is { } s ? ToSampler(id, s) : null;
        if (kind == HostedKind.ImageGeneration && sampler is null)
            throw new InvalidDataException($"Model '{id}' draws without saying how it samples.");
        if (kind == HostedKind.Language && (e.Images || e.Audio) && parts.Any(p => p.Projector is null))
            throw new InvalidDataException($"Model '{id}' sees or hears with no projector to do it through.");
        if (e.TokenSampling is not null && kind != HostedKind.Language)
            throw new InvalidDataException($"Model '{id}' chooses tokens but writes none.");
        var choosing = e.TokenSampling is { } c ? ToTokenSampling(id, c) : null;
        var filming = e.Filming is { } f ? ToFilming(id, f) : null;
        if ((kind == HostedKind.VideoGeneration) != (filming is not null))
            throw new InvalidDataException($"Model '{id}' says how it films, or films without saying how.");
        var loras = (e.Loras ?? []).Select(l => ToLora(id, l)).ToList();
        if (loras.Count > 0 && kind != HostedKind.ImageGeneration)
            throw new InvalidDataException($"Model '{id}' lists LoRAs, which only a model that draws takes.");
        if (loras.GroupBy(l => l.Id).Any(g => g.Count() > 1))
            throw new InvalidDataException($"Model '{id}' lists two LoRAs of one id.");
        if (sampler is { NegativePrompts: false } && loras.Any(l => l.Sampler is null))
            throw new InvalidDataException($"Model '{id}' takes no negative prompt, so a LoRA it draws with in its own steps would be told one.");
        var model = new HostedModel(id, displayName, files, e.Tier ?? HostedTier.Standard, kind, parts, voices, e.Images, e.Audio, e.ToolCalls,
                                    sampler, choosing, filming) { Loras = loras };
        Check("Model", model, HostedRoles.Of(kind, sampler, filming));
        return model;
    }

    private static HostedTool ToTool(ToolEntry e)
    {
        var (id, displayName, files) = Identity("tool", e.Id, e.DisplayName, e.Files);
        if (e.Kind is not { } kind) throw new InvalidDataException($"Tool '{id}' does not say what it does.");
        var tool = new HostedTool(id, displayName, files, kind, [.. (e.Parts ?? []).Select(p => ToPart("Tool", id, p, language: false, onDemand: true))]);
        Check("Tool", tool, HostedRoles.Of(kind));
        return tool;
    }

    /// <summary>That <paramref name="entry"/> runs somewhere, with one part of each of <paramref name="needed"/> on each
    /// runtime it is built for — several synthesizers each named — and that its files and its parts' folders match.</summary>
    /// <exception cref="InvalidDataException">They do not.</exception>
    private static void Check(string what, HostedEntry entry, IReadOnlyList<string> needed)
    {
        if (!entry.RunsOn(Cuda) && !entry.RunsOn(Cpu)) throw new InvalidDataException($"{what} '{entry.Id}' runs on no runtime.");
        foreach (var runtime in new[] { Cuda, Cpu })
        {
            var onRuntime = entry.PartsFor(runtime);
            if (onRuntime.Count == 0) continue;   // not built for this runtime
            if (!entry.RunsOn(runtime))
            {
                // Parts built for a runtime the model does not run on whole are fallbacks — a GPU part's CPU build, for a GPU that cannot
                // hold it — each beside the part it stands in for.
                if (onRuntime.FirstOrDefault(p => p.ExecutionProvider == runtime && !entry.Parts.Any(o => o.Name == p.Name && o.ExecutionProvider != runtime)) is { } alone)
                    throw new InvalidDataException($"{what} '{entry.Id}' has a {runtime} build of its {alone.Name} beside no other build of it, and does not run on {runtime} whole.");
                continue;
            }
            var roles = onRuntime.Select(p => p.Role).ToList();
            bool complete = needed.All(roles.Contains) && roles.All(needed.Contains)
                         && roles.Where(r => !HostedRoles.Repeats(r)).GroupBy(r => r).All(g => g.Count() == 1)
                         && onRuntime.GroupBy(p => p.Name).All(g => g.Count() == 1);
            if (!complete)
                throw new InvalidDataException($"{what} '{entry.Id}' needs one part each of {string.Join(", ", needed)} on {runtime}, where it runs — several synthesizers each named — and has {string.Join(", ", onRuntime.Select(p => p.Name))}.");
        }
        var folders = entry.Parts.Select(p => p.Folder + "/").Distinct().ToList();
        if (entry.Files.Any(f => !folders.Any(folder => f.RelativePath.StartsWith(folder, StringComparison.Ordinal))))
            throw new InvalidDataException($"{what} '{entry.Id}' has files outside its parts' folders.");
        if (entry.Parts.Any(p => !entry.Files.Any(f => f.RelativePath.StartsWith(p.Folder + "/", StringComparison.Ordinal))))
            throw new InvalidDataException($"{what} '{entry.Id}' has a part with no files.");
    }

    /// <summary>The sizes a sampler or a filming entry gives its shapes, held to <paramref name="shapes"/>.</summary>
    private static Dictionary<HostedShape, (int Width, int Height)> Sizes(string modelId, Dictionary<string, int[]>? given, IReadOnlyList<HostedShape> shapes,
                                                                          string what)
    {
        var sizes = new Dictionary<HostedShape, (int Width, int Height)>();
        foreach (var (name, size) in given ?? [])
        {
            if (!Enum.TryParse<HostedShape>(name, ignoreCase: true, out var shape) || !shapes.Contains(shape) || size is not [> 0, > 0])
                throw new InvalidDataException($"Model '{modelId}' gives '{name}' a size that is not a {what}'s width and height.");
            sizes[shape] = (size[0], size[1]);
        }
        return sizes;
    }

    /// <exception cref="InvalidDataException">The sampler does not say how it draws, how many steps and how far shifted — a
    /// shift only for a method that does not shift by the picture's size — or a size for every shape, or how it works from
    /// pictures where it says it does.</exception>
    private static HostedSampler ToSampler(string modelId, Sampler s)
    {
        if (s is not { Method: { } method, Steps: > 0, Shift: >= 0, Guidance: >= 0 } || (s.Shift > 0) == DrawingMethods.ShiftsBySize(method))
            throw new InvalidDataException($"Model '{modelId}' draws without saying how it samples.");
        var sizes = Sizes(modelId, s.Sizes, Enum.GetValues<HostedShape>(), "shape");
        // A model that only changes pictures says no sizes; one that draws from prompts, a size for every shape.
        if ((sizes.Count > 0 || s.Editing is null) && Enum.GetValues<HostedShape>().Any(shape => !sizes.ContainsKey(shape)))
            throw new InvalidDataException($"Model '{modelId}' does not say what size it draws each shape at.");
        HostedEditing? editing = null;
        if (s.Editing is { } e)
        {
            if (e.Sources <= 0)
                throw new InvalidDataException($"Model '{modelId}' works from pictures without saying how many.");
            editing = new HostedEditing(e.Sources, e.Masks);
        }
        return new HostedSampler(method, s.Steps, s.Shift, s.Guidance, sizes, s.NegativePrompts, editing);
    }

    /// <exception cref="InvalidDataException">The LoRA does not say what it is, what it does, how strongly it is drawn
    /// with, or which of its files is its adapter and which its sampler — or lists a file that is neither.</exception>
    private static HostedLora ToLora(string modelId, Lora l)
    {
        if (string.IsNullOrWhiteSpace(l.Id) || string.IsNullOrWhiteSpace(l.DisplayName) || string.IsNullOrWhiteSpace(l.Description) || l.Strength <= 0)
            throw new InvalidDataException($"A LoRA of model '{modelId}' does not say what it is, what it does or how strongly it is drawn with.");
        var files = (l.Files ?? []).Select(f => new ModelAssetEntry(f.Path, f.Url, f.Size, f.Sha256).ToAsset(l.Id)).ToList();
        var adapter = files.FirstOrDefault(f => f.RelativePath == l.Adapter)
                      ?? throw new InvalidDataException($"LoRA '{l.Id}' of model '{modelId}' does not list its adapter among its files.");
        ModelAsset? sampler = null;
        if (l.Sampler is not null)
            sampler = files.FirstOrDefault(f => f.RelativePath == l.Sampler)
                      ?? throw new InvalidDataException($"LoRA '{l.Id}' of model '{modelId}' does not list its sampler among its files.");
        if (files.Count != (sampler is null ? 1 : 2))
            throw new InvalidDataException($"LoRA '{l.Id}' of model '{modelId}' lists a file that is neither its adapter nor its sampler.");
        return new HostedLora(l.Id, l.DisplayName, l.Description, l.Adult, string.IsNullOrWhiteSpace(l.Trigger) ? null : l.Trigger, l.Strength, adapter, sampler);
    }

    /// <exception cref="InvalidDataException">It does not say how it films, how many steps and how far shifted — a distilled
    /// model, through which timesteps, one a step; a guided one, from which negative prompt — at what frame rate, in which
    /// lengths, or a size for each shape a clip is made in.</exception>
    private static HostedFilming ToFilming(string modelId, Filming f)
    {
        if (f is not { Method: { } method, Steps: > 0, Shift: > 0, Guidance: >= 0, FramesPerSecond: > 0, Seconds: { Count: > 0 } seconds }
            || seconds.Any(s => s <= 0))
            throw new InvalidDataException($"Model '{modelId}' films without saying how it samples, at what rate or for how long.");
        var timesteps = f.Timesteps ?? [];
        if (method == FilmingMethod.WanDistilled && (timesteps.Count != f.Steps || timesteps.Any(t => t is <= 0 or > 1000) || f.Guidance != 0))
            throw new InvalidDataException($"Model '{modelId}' is distilled, so it steps through a timestep a step and is not guided.");
        if (method == FilmingMethod.Wan && (timesteps.Count > 0 || f.Guidance <= 0 || string.IsNullOrWhiteSpace(f.NegativePrompt)))
            throw new InvalidDataException($"Model '{modelId}' is guided, so it steers away from a negative prompt and its steps follow from their number.");
        var sizes = Sizes(modelId, f.Sizes, HostedFilming.Shapes, "clip shape");
        if (HostedFilming.Shapes.Any(shape => !sizes.ContainsKey(shape)))
            throw new InvalidDataException($"Model '{modelId}' does not say what size it films each shape at.");
        return new HostedFilming(method, f.Steps, f.Shift, f.Guidance, [.. timesteps], f.NegativePrompt ?? "", f.FramesPerSecond,
                                 [.. seconds.Order()], sizes, f.StartsFromPictures);
    }

    /// <exception cref="InvalidDataException">It says nothing of how the model chooses when it reasons, or gives a temperature,
    /// top-k or top-p no sampler takes.</exception>
    private static HostedTokenSampling ToTokenSampling(string modelId, Choosing c)
    {
        if (c.Thinking is not { } thinking)
            throw new InvalidDataException($"Model '{modelId}' says how it chooses tokens without saying how when it reasons.");
        var reasoning = Read(thinking);
        return new(reasoning, c.Answering is { } answering ? Read(answering) : reasoning);

        TokenSampling Read(Choice choice)
        {
            if (choice.Temperature <= 0 || choice.TopK < 0 || choice.TopP is <= 0 or > 1)
                throw new InvalidDataException($"Model '{modelId}' chooses tokens at temperature {choice.Temperature}, top-k {choice.TopK}, top-p {choice.TopP}.");
            return new TokenSampling(choice.Temperature, choice.TopK, choice.TopP, choice.PresencePenalty);
        }
    }

    /// <param name="language">A language model's part: what it and each token of its context hold must be known.</param>
    /// <param name="onDemand">A part loaded on demand against the GPU budget — a picture's, a clip's, a tool's — so what it
    /// holds at its peak must be known, unless it says it runs on the CPU on every machine: a part so small a GPU would only
    /// keep it waiting.</param>
    private static HostedPart ToPart(string what, string id, Part p, bool language, bool onDemand)
    {
        if (string.IsNullOrWhiteSpace(p.Role) || string.IsNullOrWhiteSpace(p.Runner) || string.IsNullOrWhiteSpace(p.Folder))
            throw new InvalidDataException($"A part of {what.ToLowerInvariant()} '{id}' does not say what it is, what runs it or where it is.");
        if (!StorePath.IsPlain(p.Folder) || p.File is { } file && !StorePath.IsPlain(file) || p.Projector is { } projector && !StorePath.IsPlain(projector))
            throw new InvalidDataException($"{what} '{id}' names a folder or file of its {p.Role} that is not a plain path inside the model store.");
        if (p.ExecutionProvider is not (null or Cuda or Cpu))
            throw new InvalidDataException($"{what} '{id}' has a part for '{p.ExecutionProvider}', a runtime the host does not have.");
        if (language && (p.MemoryBytes <= 0 || p.BytesPerToken <= 0 || p.ContextTokens < ShortestContext))
            throw new InvalidDataException($"{what} '{id}' does not say how much it and each token of its context hold, or how long a conversation it is given.");
        if (p.RunsOnCpu && (p.MemoryBytes != 0 || p.ExecutionProvider is not null))
            throw new InvalidDataException($"{what} '{id}' runs its {p.Role} on the CPU everywhere, so it holds no GPU memory and has no build for one runtime.");
        if (onDemand && p.ExecutionProvider != Cpu && !p.RunsOnCpu && p.MemoryBytes <= 0)
            throw new InvalidDataException($"{what} '{id}' does not say how much its {p.Role} holds.");

        if (p.Streamed is { } streamed && (!onDemand || p.ExecutionProvider != Cuda || streamed.File?.EndsWith(".stream.json", StringComparison.Ordinal) != true
                                           || streamed.MemoryBytes <= 0 || streamed.HostBytes <= 0))
            throw new InvalidDataException($"{what} '{id}' has a streamed build of its {p.Role} that is not a CUDA part's loaded on demand, names no manifest, or does not say what it holds.");
        if (p.Tiled is { } tiled && (!onDemand || p.ExecutionProvider != Cuda || tiled.Cells < 16 || tiled.MemoryBytes <= 0))
            throw new InvalidDataException($"{what} '{id}' runs its {p.Role} in tiles that are not a CUDA part's loaded on demand, are under 16 cells, or do not say what they hold.");
        if (p.LoadsOnDemand && (language || onDemand))
            throw new InvalidDataException($"{what} '{id}' loads its {p.Role} on demand, which only a part of a model kept loaded can.");

        return new HostedPart(p.Role, p.Runner, p.Folder, p.File, p.MemoryBytes, p.ContextTokens, p.CudaOptions, p.ExecutionProvider,
                              p.Name ?? p.Role, p.Projector, p.BytesPerToken, p.LoadsOnDemand, p.Layers, p.BatchBytesPerToken, p.BytesPerSequence, p.BackgroundBytes,
                              p.StateBytes, p.ExpertBytes, p.Streamed is { } s ? new HostedStreamedBuild(s.File!, s.MemoryBytes, s.HostBytes) : null,
                              p.Tiled is { } t ? new HostedTiledBuild(t.Cells, t.MemoryBytes, t.HostBytes) : null);
    }

    private static HostedVoice ToVoice(string modelId, IReadOnlyList<ModelAsset> files, List<HostedPart> parts, Voice v)
    {
        if (string.IsNullOrWhiteSpace(v.Language) || string.IsNullOrWhiteSpace(v.File) || parts.FirstOrDefault(p => p.Name == v.Part) is not { } part)
            throw new InvalidDataException($"A voice of model '{modelId}' does not say what it reads, which part speaks it, or its file.");
        if (!StorePath.IsPlain(v.File) || v.Lexicon is { } lexicon && !StorePath.IsPlain(lexicon))
            throw new InvalidDataException($"A voice of model '{modelId}' names a file that is not a plain path inside the model store.");
        foreach (var file in new[] { v.File, v.Lexicon }.OfType<string>())
            if (!files.Any(f => f.RelativePath == $"{part.Folder}/{file}"))
                throw new InvalidDataException($"Model '{modelId}' has a voice that needs {file}, which is not among its files.");
        return new HostedVoice(v.Language, part.Name, v.File, v.Lexicon);
    }
}
