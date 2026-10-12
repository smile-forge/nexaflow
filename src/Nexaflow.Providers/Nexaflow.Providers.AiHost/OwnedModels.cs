using System.Text.Json;
using Nexaflow.Providers.Common;

namespace Nexaflow.Providers.AiHost;

/// <summary>
/// A model Nexaflow runs itself: its files, and what the host needs to know to run it — the adapter that prepares
/// the files, the GPU memory it holds with a working context, the longest context, and the media it reads.
/// </summary>
internal sealed record OwnedModel(CatalogModel Catalog, string Folder, string Adapter, long MemoryBytes, int ContextTokens,
                                  bool Images, bool Audio)
{
    public string Id => Catalog.Id;
}

/// <summary>
/// The models in <c>owned-models.json</c>: a model catalog (<see cref="ModelCatalog"/>) whose entries also say how
/// the host runs them.
/// </summary>
internal static class OwnedModels
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private sealed record Manifest(List<Entry>? Models);
    private sealed record Entry(string? Id, string? Folder, string? Adapter, long MemoryBytes, int ContextTokens, bool Images, bool Audio);

    public static IReadOnlyList<OwnedModel> All { get; } = Read();

    private static IReadOnlyList<OwnedModel> Read()
    {
        byte[] json;
        using (var stream = typeof(OwnedModels).Assembly.GetManifestResourceStream("owned-models.json")
                            ?? throw new InvalidOperationException("The owned-model catalog is missing from its assembly."))
        using (var copy = new MemoryStream())
        {
            stream.CopyTo(copy);
            json = copy.ToArray();
        }
        return Read(json);
    }

    /// <exception cref="InvalidDataException">A model does not say how it is run, or its files are not under its folder.</exception>
    internal static IReadOnlyList<OwnedModel> Read(byte[] json)
    {
        var catalog = ModelCatalog.Read(new MemoryStream(json)).ToDictionary(m => m.Id);
        var entries = JsonSerializer.Deserialize<Manifest>(json, Options)?.Models ?? [];
        return [.. entries.Select(e =>
        {
            if (e.Id is null || !catalog.TryGetValue(e.Id, out var model)) throw new InvalidDataException("An owned model has no id.");
            if (string.IsNullOrWhiteSpace(e.Folder) || string.IsNullOrWhiteSpace(e.Adapter) || e.MemoryBytes <= 0 || e.ContextTokens <= 0)
                throw new InvalidDataException($"Owned model '{e.Id}' does not say how it is run.");
            if (model.Files.Any(f => !f.RelativePath.StartsWith(e.Folder + "/", StringComparison.Ordinal)))
                throw new InvalidDataException($"Owned model '{e.Id}' has files outside its folder.");
            return new OwnedModel(model, e.Folder, e.Adapter, e.MemoryBytes, e.ContextTokens, e.Images, e.Audio);
        })];
    }
}
