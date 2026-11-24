using Nexaflow.AiHost.Catalog;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Provisioning;

namespace Nexaflow.AiHost.Store;

/// <summary>Where the files a request names lie: each a plain path inside the model store.</summary>
internal interface IModelFiles
{
    /// <summary>Where <paramref name="relative"/> lies in the store.</summary>
    /// <exception cref="HostRequestException">It is not a plain path inside the store.</exception>
    string PathOf(string relative);
}

/// <summary>The store as what runs models reads it: whether a build's files are here, and fetching one a placement needs.</summary>
internal interface IModelStore : IModelFiles
{
    /// <summary>Whether every one of <paramref name="files"/> is in the store, verified.</summary>
    bool Holds(IEnumerable<ModelAsset> files);

    /// <exception cref="HostRequestException">A path the catalog does not name.</exception>
    /// <exception cref="ModelRoomException">The store's drive has too little room left; nothing was fetched.</exception>
    /// <exception cref="ModelDownloadsOffException">A file is not here, and this run downloads nothing.</exception>
    Task FetchAsync(IReadOnlyList<string> paths, IProgress<AssetProgress>? progress, CancellationToken ct);
}

/// <summary>
/// The model store this host keeps — the one thing that reads and writes it, whichever app asks: <c>%ProgramData%\Smile-Forge\Models</c>,
/// one copy on the machine, written as the service's account where the host is installed as a service and as the user who started
/// it where an app did. It fetches only the files its catalog names — every model's and tool's, every LoRA's, the CUDA pack's —
/// and moves in those an earlier store kept, each when it is the same file.
/// </summary>
internal sealed class HostModelStore : IModelStore
{
    private readonly ModelAssetStore _store;
    private readonly Dictionary<string, ModelAsset> _files;
    private readonly MachineCards _machine;
    private readonly Lock _retiring = new();

    /// <param name="adoptFrom">Earlier stores whose files are moved in, each when it is the same file.</param>
    /// <param name="machine">This machine's cards: which CUDA pack, if any, it runs on.</param>
    /// <param name="downloadsAllowed">False keeps the store to what is in it or can be moved in.</param>
    public HostModelStore(string root, IReadOnlyList<string> adoptFrom, MachineCards machine, bool downloadsAllowed = true, HttpClient? http = null)
    {
        Root = root;
        _machine = machine;
        Directory.CreateDirectory(root);
        _store = new ModelAssetStore(root, [.. adoptFrom, .. CudaRuntimePack.EarlierIn(root)], http, downloadsAllowed);
        _files = CatalogFiles().DistinctBy(f => f.RelativePath, StringComparer.Ordinal).ToDictionary(f => f.RelativePath, StringComparer.Ordinal);
        _store.Changed += (_, asset) => Changed?.Invoke(this, asset.RelativePath);
        // An earlier pack is read by nothing once this one is whole, nor on a machine no pack suits.
        if (!CudaRuntimePack.Applies(machine) || CudaRuntimePack.FolderIn(_store) is not null) _ = Task.Run(RetireEarlierPacks);
    }

    /// <summary>The machine's own store, shared by the service and by every host an app starts.</summary>
    public static string DefaultRoot
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Smile-Forge", "Models");

    public string Root { get; }

    /// <summary>A file arrived in the store, verified, or left it: its path in the store.</summary>
    public event EventHandler<string>? Changed;

    /// <summary>Every file the catalog names.</summary>
    private static IEnumerable<ModelAsset> CatalogFiles()
        => HostCatalog.Models.SelectMany(m => m.Files.Concat(m.Loras.SelectMany(l => l.Files)))
                             .Concat(HostCatalog.Tools.SelectMany(t => t.Files))
                             .Concat(CudaRuntimePack.Files);

    public bool Holds(IEnumerable<ModelAsset> files) => files.All(_store.Has);

    /// <summary>The store as it stands, for every file the catalog names.</summary>
    public ModelStoreState Describe()
    {
        var read = _store.Read(_files.Values);
        return new ModelStoreState([.. read.Files.Select(f => new ModelFileState(f.Path, f.Here, f.Partial, f.Adoptable))],
                                   read.ArchivePartials, read.UnderWay, read.Free, read.Drive, CudaRuntimePack.EarlierIn(Root).Count > 0);
    }

    /// <summary>Fetches <paramref name="paths"/>; once the CUDA pack is whole, however it came, the earlier packs go.</summary>
    /// <exception cref="HostRequestException">A path the catalog does not name.</exception>
    /// <exception cref="ModelRoomException">The store's drive has too little room left; nothing was fetched.</exception>
    public async Task FetchAsync(IReadOnlyList<string> paths, IProgress<AssetProgress>? progress, CancellationToken ct)
    {
        await _store.EnsureAllAsync(AssetsOf(paths), progress, ct);
        if (CudaRuntimePack.FolderIn(_store) is not null) RetireEarlierPacks();
    }

    /// <exception cref="HostRequestException">A path the catalog does not name.</exception>
    public async Task RemoveAsync(IReadOnlyList<string> paths, CancellationToken ct)
    {
        foreach (var asset in AssetsOf(paths)) await _store.RemoveAsync(asset, ct);
    }

    /// <summary>Moves in every file the catalog names that an earlier folder it was given holds as the same file, fetching nothing.</summary>
    public Task AdoptEarlierAsync(CancellationToken ct) => _store.AdoptEarlierAsync([.. _files.Values], ct);

    /// <summary>The folder of the CUDA pack this machine runs on, when it is in the store; null where the machine cannot run it, or
    /// it is not here.</summary>
    public string? RuntimePack() => CudaRuntimePack.Applies(_machine) ? CudaRuntimePack.FolderIn(_store) : null;

    /// <summary>Deletes the earlier packs. One still held open — by an earlier host running from it — stays, and the next try takes it.</summary>
    private void RetireEarlierPacks()
    {
        lock (_retiring)
            foreach (var folder in CudaRuntimePack.EarlierIn(Root))
            {
                try { Directory.Delete(folder, recursive: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
    }

    public string PathOf(string relative)
        => StorePath.IsPlain(relative) ? StorePath.Resolve(Root, relative)
           : throw new HostRequestException(HostErrorKind.Invalid, $"'{relative}' is not a plain path inside the model store.");

    private List<ModelAsset> AssetsOf(IReadOnlyList<string> paths)
        => [.. paths.Select(p => _files.TryGetValue(p, out var asset) ? asset
                                 : throw new HostRequestException(HostErrorKind.NotFound, $"'{p}' is not a file this host's catalog names."))];
}
