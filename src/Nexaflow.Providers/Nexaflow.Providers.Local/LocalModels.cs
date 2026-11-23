using System.Text.Json;
using System.Text.Json.Serialization;
using Nexaflow.AiHost.Provisioning;

namespace Nexaflow.Providers.Local;

/// <summary>
/// A provider whose models are files kept on this machine, fetched through the <see cref="IModelAssetStore"/>: its
/// catalog says which models there are and what each needs, so the workspace can show them, fetch them and remove them.
/// </summary>
public interface ILocalModelCatalog
{
    IReadOnlyList<CatalogModel> Catalog { get; }

    /// <summary>The catalog's models this machine cannot run, each with why — shown so the user sees what there is, never
    /// chosen. Default: none.</summary>
    IReadOnlyList<UnrunnableModel> Unrunnable => [];

    /// <summary>What <paramref name="modelId"/> needs besides its own files that other models share — a GPU runtime — fetched
    /// with it, and kept while any model that needs it is; null when it needs nothing more. Default: null.</summary>
    LocalRuntime? RuntimeOf(string modelId) => null;
}

/// <summary>One model a provider offers, and the files it needs.</summary>
/// <param name="Tier">Which job of its kind it is made for: the everyday one, or the one where quality is worth the wait.</param>
public sealed record CatalogModel(string Id, string DisplayName, IReadOnlyList<ModelAsset> Files, ModelTier Tier = ModelTier.Standard);

/// <summary>What a catalog manifest lists: the models providers offer, and the models tools work with.</summary>
public sealed record CatalogContents(IReadOnlyList<CatalogModel> Models, IReadOnlyList<CatalogModel> Tools);

/// <summary>Which job of its kind a model is made for.</summary>
public enum ModelTier
{
    /// <summary>The everyday job: quick, and good enough.</summary>
    Standard,

    /// <summary>The job where quality is worth the wait — a high-quality picture.</summary>
    HighQuality,
}

/// <summary>Why this machine cannot run a model.</summary>
public enum UnrunnableReason
{
    /// <summary>The model has no build for this machine's hardware.</summary>
    NoBuildForThisMachine,
    /// <summary>The model needs more GPU memory than this machine has.</summary>
    TooLargeForThisMachine,
}

/// <summary>A model of a catalog this machine cannot run, and why.</summary>
public sealed record UnrunnableModel(CatalogModel Model, UnrunnableReason Why);

/// <summary>Files several local models need besides their own — a GPU runtime — under the name it is shown by.</summary>
public sealed record LocalRuntime(string Id, string DisplayName, IReadOnlyList<ModelAsset> Files);

/// <summary>
/// The manifest a provider ships of the models it can fetch: for each, an id (what an ability-grid column names),
/// a display name, the job of its kind it is made for (<c>"tier": "highQuality"</c>; standard when it says nothing), and
/// its files with their source, size and SHA-256, each as the AI host SDK writes one (<see cref="ModelAssetEntry"/>). A file
/// published inside an archive gives the archive's address as its url, and says which entry it is and what the archive must be:
/// <code>
/// { "models": [ { "id": "base.en", "displayName": "…",
///                 "files": [ { "path": "whisper/ggml-base.en.bin", "url": "https://…", "size": 147964211, "sha256": "…" },
///                            { "path": "runtime/…/cudnn64_9.dll", "url": "https://…/nvidia_cudnn_cu13-….whl", "size": 270448, "sha256": "…",
///                              "archive": { "entry": "nvidia/cudnn/bin/cudnn64_9.dll", "size": 436469905, "sha256": "…" } } ] } ] }
/// </code>
/// </summary>
public static partial class ModelCatalog
{
    /// <summary>The manifest's shape, read through serializers generated at build time rather than built by reflection on
    /// first use — the catalogs are read at startup, where that first use is paid on the UI thread.</summary>
    [JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, UseStringEnumConverter = true)]
    [JsonSerializable(typeof(Manifest))]
    private sealed partial class ManifestJson : JsonSerializerContext;

    private sealed record Manifest(List<ManifestModel>? Models, List<ManifestModel>? Tools);
    private sealed record ManifestModel(string? Id, string? DisplayName, List<ModelAssetEntry>? Files, ModelTier? Tier);

    /// <exception cref="InvalidDataException">The manifest is malformed, or a model or file lacks what it must say.</exception>
    public static IReadOnlyList<CatalogModel> Read(Stream json) => [.. (ParseManifest(json).Models ?? []).Select(ToModel)];

    /// <summary>
    /// The manifest's tools: the models a tool works with, in the same shape as its models — fetched and kept as they are, but
    /// serving no provider (<c>"tools": [ … ]</c> beside <c>"models"</c>).
    /// </summary>
    /// <exception cref="InvalidDataException">The manifest is malformed, or a tool or file lacks what it must say.</exception>
    public static IReadOnlyList<CatalogModel> ReadTools(Stream json) => [.. (ParseManifest(json).Tools ?? []).Select(ToModel)];

    /// <summary>The manifest's models and its tools, from one read of it — for a manifest that lists both.</summary>
    /// <exception cref="InvalidDataException">The manifest is malformed, or a model, tool or file lacks what it must say.</exception>
    public static CatalogContents ReadContents(Stream json)
    {
        var manifest = ParseManifest(json);
        return new([.. (manifest.Models ?? []).Select(ToModel)], [.. (manifest.Tools ?? []).Select(ToModel)]);
    }

    /// <exception cref="InvalidDataException">The manifest is not JSON of its shape.</exception>
    private static Manifest ParseManifest(Stream json)
    {
        try { return JsonSerializer.Deserialize(json, ManifestJson.Default.Manifest) ?? new Manifest(null, null); }
        catch (JsonException ex) { throw new InvalidDataException($"The model catalog does not read: {ex.Message}", ex); }
    }

    /// <summary>A file an entry lists apart from its own files — fetched only when a request first needs it, as a LoRA is —
    /// held to the same rules as the files a model lists.</summary>
    /// <exception cref="InvalidDataException">It does not say where it goes, where it comes from over https, its size or its SHA-256.</exception>
    public static ModelAsset FileOf(string ownerId, string? path, string? url, long size, string? sha256)
        => new ModelAssetEntry(path, url, size, sha256).ToAsset(ownerId);

    private static CatalogModel ToModel(ManifestModel m)
    {
        if (string.IsNullOrWhiteSpace(m.Id)) throw new InvalidDataException("A catalog model has no id.");
        if (m.Files is not { Count: > 0 }) throw new InvalidDataException($"Catalog model '{m.Id}' lists no files.");

        return new CatalogModel(m.Id, string.IsNullOrWhiteSpace(m.DisplayName) ? m.Id : m.DisplayName!,
            [.. m.Files.Select(f => f.ToAsset(m.Id))], m.Tier ?? ModelTier.Standard);
    }
}
