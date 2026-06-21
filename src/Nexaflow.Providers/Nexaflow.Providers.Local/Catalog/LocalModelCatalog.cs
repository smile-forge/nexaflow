using Nexaflow.Providers.Common;
using System.IO;
using System.Text.Json;

namespace Nexaflow.Providers.Local.Catalog;

/// <summary>
/// Loads the editable model <c>catalog.json</c> and filters variants to what the host can run.
/// The catalog ships bundled with the provider and is seeded once into the user's models folder so
/// edits persist across updates.
/// </summary>
public static class LocalModelCatalog
{
    private const int MinCudaVramMb = 2048;

    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling         = JsonCommentHandling.Skip,
        AllowTrailingCommas         = true,
    };

    /// <summary>Loads the variants from the user-editable catalog (seeding it from the bundled copy if absent).</summary>
    public static IReadOnlyList<LocalModelVariant> Load(string modelsDir)
    {
        try
        {
            var path = EnsureUserCatalog(modelsDir);
            if (path is null || !File.Exists(path)) return [];

            var doc = JsonSerializer.Deserialize<CatalogFile>(File.ReadAllText(path), Opts);
            return doc?.Variants?
                       .Where(v => !string.IsNullOrWhiteSpace(v.Id) && v.Files.Count > 0)
                       .ToList()
                   ?? [];
        }
        catch { return []; }   // never throw — the picker degrades to "no models"
    }

    /// <summary>Variants whose footprint fits the detected hardware (VRAM on usable CUDA, else system RAM).</summary>
    public static IEnumerable<LocalModelVariant> FittingHost(
        IEnumerable<LocalModelVariant> all, HostCapabilities? caps)
    {
        if (caps is null)
            return all.Where(v => v.ApproxVramMb <= 6000);   // CPU-safe fallback until the probe completes

        bool cudaUsable = caps.CudaAvailable && caps.GpuVramMb >= MinCudaVramMb;
        int  budgetMb   = cudaUsable ? caps.GpuVramMb : caps.TotalRamMb;
        int  usable     = (int)(budgetMb * 0.85);            // headroom for KV cache + OS
        return all.Where(v => v.ApproxVramMb <= usable);
    }

    public static LocalModelVariant? Find(IEnumerable<LocalModelVariant> all, string id)
        => all.FirstOrDefault(v => string.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The live, user-editable catalog path for a models folder (this is the file the app reads —
    /// edits to the bundled copy in source do NOT reach an already-seeded folder).</summary>
    public static string UserCatalogPath(string modelsDir) => Path.Combine(modelsDir, "catalog.json");

    /// <summary>Overwrites the live catalog with the freshly bundled default (the "Reset to bundled" action).</summary>
    public static void ResetToBundled(string modelsDir)
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "catalog.json");
        if (!File.Exists(bundled)) return;
        Directory.CreateDirectory(modelsDir);
        File.Copy(bundled, UserCatalogPath(modelsDir), overwrite: true);
    }

    /// <summary>Returns the user-editable catalog path, copying the bundled default in on first use.</summary>
    private static string? EnsureUserCatalog(string modelsDir)
    {
        var userPath = UserCatalogPath(modelsDir);
        if (File.Exists(userPath)) return userPath;

        var bundled = Path.Combine(AppContext.BaseDirectory, "catalog.json");
        try
        {
            Directory.CreateDirectory(modelsDir);
            if (File.Exists(bundled)) { File.Copy(bundled, userPath); return userPath; }
        }
        catch { /* fall back to reading the bundled copy directly */ }

        return File.Exists(bundled) ? bundled : userPath;
    }

    private sealed class CatalogFile
    {
        public List<LocalModelVariant> Variants { get; set; } = [];
    }
}
