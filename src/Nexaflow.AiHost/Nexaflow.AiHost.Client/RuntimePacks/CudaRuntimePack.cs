using Nexaflow.IO.Common.Hardware;
using Nexaflow.Providers.Common;

namespace Nexaflow.AiHost.Client;

/// <summary>
/// What the host needs to run on an NVIDIA GPU, fetched from where NVIDIA, Microsoft and llama.cpp publish it: ONNX
/// Runtime and onnxruntime-genai built with their CUDA layers (nuget.org), llama.cpp's CUDA backend (its GitHub
/// release), and the CUDA runtime, cuBLAS and cuDNN (NVIDIA's wheels on PyPI) — about 1.4 GB, under <c>runtime\</c> in
/// the asset store. Pinned to the host's own runtime versions: the install's <c>onnxruntime.dll</c> and
/// <c>onnxruntime-genai.dll</c> are the same files as the pack's, and its llama.cpp is the build the backend is from.
/// </summary>
public static class CudaRuntimePack
{
    /// <summary>The oldest CUDA driver the pack's CUDA 13 libraries run on.</summary>
    public static readonly Version MinimumDriver = new(13, 0);

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
        => hardware.Adapters.Any(a => a.Vendor == ComputeVendor.Nvidia && !a.IsIntegrated && !a.IsNpu)
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
}
