using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nexaflow.Providers.Common;

/// <summary>
/// A file a model needs — its weights, a vocabulary, a runtime library — fetched once and kept. <see cref="RelativePath"/>
/// is where it lives under the asset store's root and what identifies it; <see cref="Size"/> and <see cref="Sha256"/>
/// are what it must be, so a file is used only when it is exactly what the catalog says. A file published inside an
/// archive names it as <see cref="Archive"/>, and <see cref="Source"/> is then the archive's address.
/// </summary>
public sealed record ModelAsset(string RelativePath, Uri Source, long Size, string Sha256, ArchiveEntry? Archive = null);

/// <summary>
/// Where an asset is packed when it is not published on its own — an entry of a zip archive such as a Python wheel or
/// a NuGet package, as NVIDIA and Microsoft ship their runtime libraries. The archive is fetched and checked whole,
/// once for every asset packed in it, and only the entries wanted are kept.
/// </summary>
/// <param name="Entry">The entry's path inside the archive.</param>
public sealed record ArchiveEntry(string Entry, long ArchiveSize, string ArchiveSha256);

/// <summary>How far an asset's download has got.</summary>
public readonly record struct AssetProgress(long Received, long Total)
{
    public double Fraction => Total > 0 ? (double)Received / Total : 0;
}

/// <summary>
/// Where providers find the files their models need. Every model's files live under one root, outside the roaming
/// profile; a download resumes where an interrupted one stopped, and a file is handed out only once its size and
/// hash match.
/// </summary>
public interface IModelAssetStore
{
    /// <summary>The asset's path when it is on disk, complete and verified; null otherwise.</summary>
    string? Locate(ModelAsset asset);

    /// <summary>Makes the asset present — downloading it, or resuming a download — and returns its path.</summary>
    /// <exception cref="InvalidDataException">What arrived is not what the catalog describes.</exception>
    Task<string> EnsureAsync(ModelAsset asset, IProgress<AssetProgress>? progress = null, CancellationToken ct = default);

    /// <summary>Makes every asset present and returns their paths in order. Each archive they are packed in is fetched
    /// once for all of them; <paramref name="progress"/> counts every byte the set still needs.</summary>
    /// <exception cref="InvalidDataException">What arrived is not what the catalog describes.</exception>
    Task<IReadOnlyList<string>> EnsureAllAsync(IReadOnlyList<ModelAsset> assets, IProgress<AssetProgress>? progress = null,
                                               CancellationToken ct = default);

    /// <summary>An asset arrived on disk, verified, or was removed — whoever downloaded or removed it. A provider
    /// whose model it is becomes available, or stops being. Raised on whichever thread made the change.</summary>
    event EventHandler<ModelAsset>? Changed;
}

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
/// its files with their source, size and SHA-256. A file published inside an archive gives the
/// archive's address as its url, and says which entry it is and what the archive must be:
/// <code>
/// { "models": [ { "id": "base.en", "displayName": "…",
///                 "files": [ { "path": "whisper/ggml-base.en.bin", "url": "https://…", "size": 147964211, "sha256": "…" },
///                            { "path": "runtime/…/cudnn64_9.dll", "url": "https://…/nvidia_cudnn_cu13-….whl", "size": 270448, "sha256": "…",
///                              "archive": { "entry": "nvidia/cudnn/bin/cudnn64_9.dll", "size": 436469905, "sha256": "…" } } ] } ] }
/// </code>
/// </summary>
public static class ModelCatalog
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private sealed record Manifest(List<ManifestModel>? Models, List<ManifestModel>? Tools);
    private sealed record ManifestModel(string? Id, string? DisplayName, List<ManifestFile>? Files, ModelTier? Tier);
    private sealed record ManifestFile(string? Path, string? Url, long Size, string? Sha256, ManifestArchive? Archive);
    private sealed record ManifestArchive(string? Entry, long Size, string? Sha256);

    /// <exception cref="InvalidDataException">The manifest is malformed, or a model or file lacks what it must say.</exception>
    public static IReadOnlyList<CatalogModel> Read(Stream json)
    {
        Manifest? manifest;
        try { manifest = JsonSerializer.Deserialize<Manifest>(json, Options); }
        catch (JsonException ex) { throw new InvalidDataException($"The model catalog does not read: {ex.Message}", ex); }

        return [.. (manifest?.Models ?? []).Select(ToModel)];
    }

    /// <summary>
    /// The manifest's tools: the models a tool works with, in the same shape as its models — fetched and kept as they are, but
    /// serving no provider (<c>"tools": [ … ]</c> beside <c>"models"</c>).
    /// </summary>
    /// <exception cref="InvalidDataException">The manifest is malformed, or a tool or file lacks what it must say.</exception>
    public static IReadOnlyList<CatalogModel> ReadTools(Stream json)
    {
        Manifest? manifest;
        try { manifest = JsonSerializer.Deserialize<Manifest>(json, Options); }
        catch (JsonException ex) { throw new InvalidDataException($"The model catalog does not read: {ex.Message}", ex); }

        return [.. (manifest?.Tools ?? []).Select(ToModel)];
    }

    private static CatalogModel ToModel(ManifestModel m)
    {
        if (string.IsNullOrWhiteSpace(m.Id)) throw new InvalidDataException("A catalog model has no id.");
        if (m.Files is not { Count: > 0 }) throw new InvalidDataException($"Catalog model '{m.Id}' lists no files.");

        return new CatalogModel(m.Id, string.IsNullOrWhiteSpace(m.DisplayName) ? m.Id : m.DisplayName!,
            [.. m.Files.Select(f => ToAsset(m.Id, f))], m.Tier ?? ModelTier.Standard);
    }

    private static ModelAsset ToAsset(string modelId, ManifestFile f)
    {
        if (string.IsNullOrWhiteSpace(f.Path) || Path.IsPathRooted(f.Path) || f.Path.Split('/', '\\').Contains(".."))
            throw new InvalidDataException($"Catalog model '{modelId}' has a file without a relative path.");
        if (!Uri.TryCreate(f.Url, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException($"Catalog file '{f.Path}' has no https source.");
        if (f.Size <= 0 || f.Sha256 is not { Length: 64 })
            throw new InvalidDataException($"Catalog file '{f.Path}' does not say its size and SHA-256.");

        ArchiveEntry? archive = null;
        if (f.Archive is { } a)
        {
            if (string.IsNullOrWhiteSpace(a.Entry) || a.Size <= 0 || a.Sha256 is not { Length: 64 })
                throw new InvalidDataException($"Catalog file '{f.Path}' is packed in an archive it does not fully describe.");
            archive = new ArchiveEntry(a.Entry, a.Size, a.Sha256.ToLowerInvariant());
        }

        return new ModelAsset(f.Path.Replace('\\', '/'), url, f.Size, f.Sha256.ToLowerInvariant(), archive);
    }
}
