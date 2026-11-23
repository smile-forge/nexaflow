namespace Nexaflow.AiHost.Provisioning;

/// <summary>
/// A file a model needs — its weights, a vocabulary, a runtime library — fetched once and kept. <see cref="RelativePath"/>
/// is where it lives under the asset store's root and what identifies it; <see cref="Size"/> and <see cref="Sha256"/>
/// are what it must be, so a file is used only when it is exactly what its manifest says. A file published inside an
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
/// Where an app finds the files its local models need. Every model's files live under one root; a download resumes where
/// an interrupted one stopped, and a file is handed out only once its size and hash match.
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

    /// <summary>An asset arrived on disk, verified, or was removed — whoever downloaded or removed it. Whatever runs the
    /// model it belongs to can run it now, or no longer. Raised on whichever thread made the change.</summary>
    event EventHandler<ModelAsset>? Changed;
}

/// <summary>
/// One file as a manifest writes it: where it goes under the store's root, its https source, its size and SHA-256 — and, for
/// a file published inside an archive, which entry it is and what the archive must be, the source then being the archive's.
/// <code>
/// { "path": "runtime/…/cudnn64_9.dll", "url": "https://…/nvidia_cudnn_cu13-….whl", "size": 270448, "sha256": "…",
///   "archive": { "entry": "nvidia/cudnn/bin/cudnn64_9.dll", "size": 436469905, "sha256": "…" } }
/// </code>
/// </summary>
public sealed record ModelAssetEntry(string? Path, string? Url, long Size, string? Sha256, ModelAssetArchive? Archive = null)
{
    /// <summary>The file it describes, as <paramref name="ownerId"/> — the model or set that lists it — needs it.</summary>
    /// <exception cref="InvalidDataException">It does not say where it goes, where it comes from over https, its size or its
    /// SHA-256, or it is packed in an archive it does not fully describe.</exception>
    public ModelAsset ToAsset(string ownerId)
    {
        if (string.IsNullOrWhiteSpace(Path) || System.IO.Path.IsPathRooted(Path) || Path.Split('/', '\\').Contains(".."))
            throw new InvalidDataException($"'{ownerId}' lists a file without a relative path.");
        if (!Uri.TryCreate(Url, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException($"File '{Path}' has no https source.");
        if (Size <= 0 || Sha256 is not { Length: 64 })
            throw new InvalidDataException($"File '{Path}' does not say its size and SHA-256.");

        ArchiveEntry? archive = null;
        if (Archive is { } a)
        {
            if (string.IsNullOrWhiteSpace(a.Entry) || a.Size <= 0 || a.Sha256 is not { Length: 64 })
                throw new InvalidDataException($"File '{Path}' is packed in an archive it does not fully describe.");
            archive = new ArchiveEntry(a.Entry, a.Size, a.Sha256.ToLowerInvariant());
        }
        return new ModelAsset(Path.Replace('\\', '/'), url, Size, Sha256.ToLowerInvariant(), archive);
    }
}

/// <summary>The archive a <see cref="ModelAssetEntry"/> is packed in, as a manifest writes it.</summary>
public sealed record ModelAssetArchive(string? Entry, long Size, string? Sha256);
