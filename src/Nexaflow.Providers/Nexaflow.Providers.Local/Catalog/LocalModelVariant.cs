namespace Nexaflow.Providers.Local.Catalog;

/// <summary>The model family — picks which prompt/tool harness drives the model.</summary>
public enum ModelFamily { Gemma, Qwen }

/// <summary>
/// One downloadable model variant from <c>catalog.json</c>. Mutable for JSON deserialization;
/// the helper members are not serialized.
/// </summary>
public sealed class LocalModelVariant
{
    /// <summary>Stable id shown in the model picker and stored as the bound model name.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>"gemma" or "qwen" (case-insensitive); anything else falls back to Gemma.</summary>
    public string Family { get; set; } = "gemma";

    public string Display { get; set; } = string.Empty;

    /// <summary>HuggingFace repo, e.g. <c>unsloth/gemma-4-12b-it-GGUF</c>.</summary>
    public string Repo { get; set; } = string.Empty;

    /// <summary>GGUF file name(s) — several in shard order for split models.</summary>
    public List<string> Files { get; set; } = [];

    public string Quant { get; set; } = "Q4_K_M";

    /// <summary>Rough Q4_K_M footprint in MB — feeds hardware gating only.</summary>
    public int ApproxVramMb { get; set; }

    /// <summary>Per-variant context window; 0 lets the provider/config decide.</summary>
    public int ContextSize { get; set; }

    /// <summary>Optional explicit download URL (single-file variants only).</summary>
    public string? Url { get; set; }

    /// <summary>Optional base URL prefix; each file is appended to it.</summary>
    public string? BaseUrl { get; set; }

    // ── Helpers (not serialized) ────────────────────────────────────────────

    public ModelFamily FamilyKind =>
        Family.Equals("qwen", StringComparison.OrdinalIgnoreCase) ? ModelFamily.Qwen : ModelFamily.Gemma;

    /// <summary>The file llama.cpp is pointed at (first shard).</summary>
    public string PrimaryFile => Files.Count > 0 ? Files[0] : string.Empty;

    /// <summary>Builds the download URL for one of this variant's files.</summary>
    public string DownloadUrlFor(string file)
    {
        if (!string.IsNullOrWhiteSpace(Url) && Files.Count == 1) return Url!;
        if (!string.IsNullOrWhiteSpace(BaseUrl)) return $"{BaseUrl!.TrimEnd('/')}/{file}";
        return $"https://huggingface.co/{Repo}/resolve/main/{file}?download=true";
    }
}
