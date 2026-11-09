using Nexaflow.IO.Common.Hardware;
using Nexaflow.Providers.Common;

namespace Nexaflow.AiHost.Client;

/// <summary>
/// What the host needs to run on an NVIDIA GPU, fetched from where NVIDIA, Microsoft and llama.cpp publish it: ONNX
/// Runtime built with its CUDA layer (nuget.org), llama.cpp's CUDA backend (its GitHub release), and the CUDA runtime,
/// cuBLAS and cuDNN (NVIDIA's wheels on PyPI) — about 1.3 GB, under <c>runtime\&lt;id&gt;</c> in the asset store.
/// Pinned to the host's own runtime versions: the install's <c>onnxruntime.dll</c> is the same file as the pack's, and
/// its llama.cpp is the build the backend is from. A version that moves the pack on gives it a new id, and so a new
/// folder; the earlier pack's (<see cref="EarlierIn"/>) is where the files the two share are taken from.
/// </summary>
public static class CudaRuntimePack
{
    /// <summary>The oldest CUDA driver the pack's CUDA 13 libraries run on.</summary>
    public static readonly Version MinimumDriver = new(13, 0);

    /// <summary>The folder under the model store each CUDA pack is kept in, by its id.</summary>
    private const string Parent = "runtime";

    /// <summary>How every CUDA pack's id begins, whichever runtimes it carries: what tells an earlier pack's folder from
    /// another runtime's beside it.</summary>
    private const string IdPrefix = "cuda-";

    private static readonly Lazy<CatalogModel> Manifest = new(() =>
    {
        using var json = typeof(CudaRuntimePack).Assembly.GetManifestResourceStream("cuda-pack.json")
            ?? throw new InvalidOperationException("The CUDA pack manifest is not embedded.");
        return ModelCatalog.Read(json).Single();
    });

    /// <summary>The pack's id, which is also its folder under <c>runtime\</c>: the runtime versions it carries.</summary>
    public static string Id => Manifest.Value.Id;

    public static IReadOnlyList<ModelAsset> Files => Manifest.Value.Files;

    /// <summary>The bytes it takes on disk.</summary>
    public static long Size => Files.Sum(f => f.Size);

    /// <summary>Whether the pack can run here: an NVIDIA GPU of its own, and a driver new enough for CUDA 13.</summary>
    public static bool Applies(ComputeHardwareReport hardware)
        => hardware.NvidiaGpus.Any()
        && hardware.CudaDriver is { } driver && driver >= MinimumDriver;

    /// <summary>The pack's folder when every file of it is on disk and verified; null otherwise.</summary>
    public static string? Locate(IModelAssetStore store)
    {
        string? folder = null;
        foreach (var file in Files)
        {
            if (store.Locate(file) is not { } path) return null;
            folder ??= Path.GetDirectoryName(path);
        }
        return folder;
    }

    /// <summary>
    /// The folders under <paramref name="storeRoot"/> holding earlier CUDA packs — each kept in a folder of its own id, so a
    /// version that moves the pack on leaves the one before it behind, replaced by this one and read by nothing.
    /// </summary>
    public static IReadOnlyList<string> EarlierIn(string storeRoot)
    {
        var parent = Path.Combine(storeRoot, Parent);
        if (!Directory.Exists(parent)) return [];
        return [.. Directory.EnumerateDirectories(parent).Where(dir => Path.GetFileName(dir) is var name
            && name.StartsWith(IdPrefix, StringComparison.OrdinalIgnoreCase)
            && !name.Equals(Id, StringComparison.OrdinalIgnoreCase))];
    }
}
