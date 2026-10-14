namespace Nexaflow.AiHost.Models;

/// <summary>
/// Turns a model's downloaded files into a folder onnxruntime-genai runs: graph headers rewritten where an export
/// needs it, and a provider-neutral <c>genai_config.base.json</c>. Runs once per folder and version, on the user's
/// machine — never a conversion of weights, which would need Python and tens of gigabytes (docs/local-ai.md).
/// </summary>
internal interface IModelAdapter
{
    string Id { get; }

    /// <summary>Raised when what the adapter writes changes, so folders prepared by an older one are prepared again.</summary>
    int Version { get; }

    void Prepare(string folder);
}

/// <summary>The adapters this host knows, and the record of which has prepared a folder.</summary>
internal sealed class ModelAdapters(IEnumerable<IModelAdapter> adapters)
{
    private const string PreparedMark = ".prepared";

    private readonly Dictionary<string, IModelAdapter> _adapters = adapters.ToDictionary(a => a.Id, StringComparer.Ordinal);

    public static ModelAdapters Default { get; } = new([new GenAiNativeAdapter()]);

    /// <summary>Prepares <paramref name="folder"/> with the named adapter, unless that version already has.</summary>
    /// <exception cref="HostRequestException">No such adapter, or a file it needs is missing.</exception>
    public void EnsurePrepared(string folder, string adapterId)
    {
        if (!_adapters.TryGetValue(adapterId, out var adapter))
            throw new HostRequestException(Contracts.HostErrorKind.Invalid, $"This host has no '{adapterId}' adapter.");

        string mark = Path.Combine(folder, PreparedMark);
        string stamp = $"{adapter.Id}@{adapter.Version}";
        if (File.Exists(mark) && File.ReadAllText(mark).Trim() == stamp) return;

        try { adapter.Prepare(folder); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new HostRequestException(Contracts.HostErrorKind.NotFound, $"The model's files in {folder} are incomplete: {ex.Message}");
        }
        File.WriteAllText(mark, stamp);
    }
}
