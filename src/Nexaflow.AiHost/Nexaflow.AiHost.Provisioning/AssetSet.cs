using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nexaflow.AiHost.Provisioning;

/// <summary>
/// Files fetched and kept together under one name — a runtime, a model — and the manifest that lists them:
/// <c>{ "id": "…", "displayName": "…", "files": [ … ] }</c>, each file a <see cref="ModelAssetEntry"/>.
/// </summary>
public sealed partial record AssetSet(string Id, string DisplayName, IReadOnlyList<ModelAsset> Files)
{
    /// <summary>The manifest's shape, read through serializers generated at build time rather than built by reflection.</summary>
    [JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
    [JsonSerializable(typeof(Manifest))]
    private sealed partial class ManifestJson : JsonSerializerContext;

    private sealed record Manifest(string? Id, string? DisplayName, List<ModelAssetEntry>? Files);

    /// <exception cref="InvalidDataException">The manifest is malformed, or it or a file lacks what it must say.</exception>
    public static AssetSet Read(Stream json)
    {
        Manifest? manifest;
        try { manifest = JsonSerializer.Deserialize(json, ManifestJson.Default.Manifest); }
        catch (JsonException ex) { throw new InvalidDataException($"The manifest does not read: {ex.Message}", ex); }
        if (string.IsNullOrWhiteSpace(manifest?.Id)) throw new InvalidDataException("The manifest names no set.");
        if (manifest.Files is not { Count: > 0 }) throw new InvalidDataException($"'{manifest.Id}' lists no files.");
        return new AssetSet(manifest.Id, string.IsNullOrWhiteSpace(manifest.DisplayName) ? manifest.Id : manifest.DisplayName,
                            [.. manifest.Files.Select(f => f.ToAsset(manifest.Id))]);
    }
}
