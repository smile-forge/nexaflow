using System.Windows.Media;

namespace Nexaflow.Features.Model3D.Loaders;

/// <summary>
/// A pluggable loader for one family of 3D file formats. The feature's <see cref="ModelLoaderRegistry"/>
/// picks the first loader whose <see cref="CanLoad"/> accepts a path. This is the feature's internal
/// extension point: add a format by implementing this interface and registering it — nothing else changes.
/// </summary>
/// <remarks>
/// Mirrors the <c>IArchiveHandler</c> + registry pattern used by the Compressed feature, scoped here so
/// model formats stay a concern of this feature alone.
/// </remarks>
public interface IModelLoader
{
    /// <summary>Short format label shown in the footer / AI context (e.g. "STL", "glTF").</summary>
    string Name { get; }

    /// <summary>Lower-case extensions (with dot) this loader handles, e.g. <c>[".gltf", ".glb"]</c>.</summary>
    IReadOnlyList<string> SupportedExtensions { get; }

    /// <summary>True when this loader handles the given file (by extension).</summary>
    bool CanLoad(string path);

    /// <summary>
    /// Reads the file and returns frozen geometry plus metadata. Called off the UI thread; implementations
    /// must freeze the geometry before returning. Throws on a genuinely unreadable file — the caller turns
    /// that into a friendly error state.
    /// </summary>
    /// <param name="palette">Distinct colours to tint materials the file doesn't colour itself, so each is
    /// separable in the viewport (see <see cref="CategoricalPalette"/>).</param>
    LoadedModel Load(string path, IReadOnlyList<Color> palette);
}
