using System.Text.Json;
using Nexaflow.Providers.Common;

namespace Nexaflow.Providers.AiHost;

/// <summary>
/// One model the host loads as part of a model Nexaflow runs itself — a language model is one part; listening is a
/// voice activity detector, a recogniser, a transcriber and a turn detector.
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
internal sealed record OwnedPart(string Role, string Runner, string Folder, string? File, string? Adapter, long MemoryBytes, int ContextTokens,
                                 IReadOnlyDictionary<string, string>? CudaOptions, string? ExecutionProvider);

/// <summary>What each part of an owned model does.</summary>
internal static class OwnedRoles
{
    public const string Chat          = "chat";
    public const string VoiceActivity = "voiceActivity";
    public const string Recognizer    = "recognizer";
    public const string Transcriber   = "transcriber";
    public const string TurnDetector  = "turnDetector";

    /// <summary>The parts a model of each kind is made of.</summary>
    public static IReadOnlyList<string> Of(ModelKind kind) => kind switch
    {
        ModelKind.Language     => [Chat],
        ModelKind.SpeechToText => [VoiceActivity, Recognizer, Transcriber, TurnDetector],
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "no owned model is of this kind"),
    };
}

/// <summary>
/// A model Nexaflow runs itself: its catalog entry — every file any machine might need — and its parts. A part built for
/// one runtime (Whisper's fp16 build for CUDA, its int8 build for the CPU) is fetched and run only on that runtime.
/// </summary>
internal sealed record OwnedModel(CatalogModel Catalog, ModelKind Kind, IReadOnlyList<OwnedPart> Parts, bool Images, bool Audio)
{
    public string Id => Catalog.Id;

    /// <summary>The parts that run on <paramref name="executionProvider"/>: one per role.</summary>
    public IReadOnlyList<OwnedPart> PartsFor(string executionProvider)
        => [.. Parts.Where(p => p.ExecutionProvider is null || p.ExecutionProvider == executionProvider)];

    public OwnedPart Part(string role, string executionProvider) => PartsFor(executionProvider).Single(p => p.Role == role);

    /// <summary>The catalog entry as it stands on a machine running <paramref name="executionProvider"/>: only the files
    /// its parts there need.</summary>
    public CatalogModel CatalogFor(string executionProvider)
    {
        var folders = PartsFor(executionProvider).Select(p => p.Folder + "/").ToList();
        return Catalog with { Files = [.. Catalog.Files.Where(f => folders.Any(folder => f.RelativePath.StartsWith(folder, StringComparison.Ordinal)))] };
    }
}

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
    private sealed record Entry(string? Id, ModelKind? Kind, bool Images, bool Audio, List<Part>? Parts);
    private sealed record Part(string? Role, string? Runner, string? Folder, string? File, string? Adapter, long MemoryBytes, int ContextTokens,
                               Dictionary<string, string>? CudaOptions, string? ExecutionProvider);

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

            var parts = (e.Parts ?? []).Select(p => ToPart(e.Id, kind, p)).ToList();
            var owned = new OwnedModel(model, kind, parts, e.Images, e.Audio);
            foreach (var runtime in new[] { Cuda, Cpu })
            {
                var roles = owned.PartsFor(runtime).Select(p => p.Role).ToList();
                if (!roles.Order().SequenceEqual(OwnedRoles.Of(kind).Order()))
                    throw new InvalidDataException($"Owned model '{e.Id}' needs one part each of {string.Join(", ", OwnedRoles.Of(kind))} on {runtime}, and has {string.Join(", ", roles)}.");
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
        return new OwnedPart(p.Role, p.Runner, p.Folder, p.File, p.Adapter, p.MemoryBytes, p.ContextTokens, p.CudaOptions, p.ExecutionProvider);
    }
}
