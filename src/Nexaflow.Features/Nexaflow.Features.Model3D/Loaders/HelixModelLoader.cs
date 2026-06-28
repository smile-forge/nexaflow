using System.IO;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace Nexaflow.Features.Model3D.Loaders;

/// <summary>
/// Loads the mesh formats HelixToolkit reads natively — STL, OBJ (+ MTL), 3DS and PLY — using the
/// matching <see cref="ModelReader"/>. Reading by path lets the readers resolve sibling resources
/// (an OBJ's .mtl and textures) relative to the file's directory. Geometry is frozen so it can be
/// built off the UI thread and handed to the viewport.
/// </summary>
public sealed class HelixModelLoader : IModelLoader
{
    public string Name => "Mesh (STL/OBJ/3DS/PLY)";

    public IReadOnlyList<string> SupportedExtensions { get; } = [".stl", ".obj", ".3ds", ".ply"];

    public bool CanLoad(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public LoadedModel Load(string path, IReadOnlyList<Color> palette)
    {
        var (reader, format) = CreateReader(Path.GetExtension(path).ToLowerInvariant());
        reader.Freeze = false; // keep it mutable so we can re-skin indistinct meshes before freezing

        var group = reader.Read(path) ?? new Model3DGroup();

        var stats = ModelInspection.CollectAndColorize(group, palette);
        if (group.CanFreeze && !group.IsFrozen) group.Freeze(); // now safe to cross to the UI thread

        return new LoadedModel
        {
            Geometry = group,
            FormatName = format,
            TriangleCount = stats.TriangleCount,
            VertexCount = stats.VertexCount,
            MeshCount = stats.MeshCount,
            Materials = stats.Materials,
        };
    }

    private static (ModelReader reader, string format) CreateReader(string ext) => ext switch
    {
        ".stl" => (new StLReader(null), "STL"),
        ".obj" => (new ObjReader(null), "OBJ"),
        ".3ds" => (new StudioReader(null), "3DS"),
        ".ply" => (new PlyReader(null), "PLY"),
        _ => throw new NotSupportedException($"No HelixToolkit reader for '{ext}'."),
    };
}
