using System.IO;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using SharpAssimp;
using AssimpMaterial = SharpAssimp.Material;
using WpfMaterial = System.Windows.Media.Media3D.Material;

namespace Nexaflow.Features.Model3D.Loaders;

/// <summary>
/// Loads FBX via SharpAssimp (a managed wrapper over the native Assimp library) and converts it to WPF
/// geometry. We let Assimp triangulate, generate smooth normals where missing, weld identical vertices and
/// bake the node transforms (<see cref="PostProcessSteps.PreTransformVertices"/>) so each mesh arrives in
/// final coordinates — then map meshes + diffuse materials to WPF, mirroring the glTF loader. Animations,
/// cameras, lights and skeletal rigs are reported as unsupported, not rendered.
/// </summary>
/// <remarks>The Assimp native binary ships only for win-x64/x86; on other architectures the import throws
/// and the viewer shows a friendly error rather than crashing.</remarks>
public sealed class FbxModelLoader : IModelLoader
{
    public string Name => "FBX";

    public IReadOnlyList<string> SupportedExtensions { get; } = [".fbx"];

    public bool CanLoad(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public LoadedModel Load(string path, IReadOnlyList<Color> palette)
    {
        using var context = new AssimpContext();
        var scene = context.ImportFile(path,
            PostProcessSteps.Triangulate
            | PostProcessSteps.GenerateSmoothNormals
            | PostProcessSteps.JoinIdenticalVertices
            | PostProcessSteps.PreTransformVertices);

        var group = new Model3DGroup();
        var materials = new List<ModelMaterial>();
        int triangles = 0, vertices = 0, untinted = 0;
        var hasBones = false;

        foreach (var mesh in scene?.Meshes ?? [])
        {
            var geometry = BuildMesh(mesh);
            if (geometry.Positions.Count == 0) continue;
            triangles += geometry.TriangleIndices.Count / 3;
            vertices += geometry.Positions.Count;
            hasBones |= mesh.HasBones;

            var source = scene!.Materials is { Count: > 0 } mats
                         && mesh.MaterialIndex >= 0 && mesh.MaterialIndex < mats.Count
                ? mats[mesh.MaterialIndex]
                : null;
            var (wpfMaterial, described) = Describe(source, materials.Count + 1, palette, ref untinted);
            materials.Add(described);
            group.Children.Add(new GeometryModel3D(geometry, wpfMaterial) { BackMaterial = wpfMaterial });
        }

        if (group.CanFreeze) group.Freeze();

        return new LoadedModel
        {
            Geometry = group,
            FormatName = "FBX",
            TriangleCount = triangles,
            VertexCount = vertices,
            MeshCount = group.Children.Count,
            Materials = materials,
            UnsupportedElements = DescribeUnsupported(scene, hasBones),
        };
    }

    private static MeshGeometry3D BuildMesh(Mesh mesh)
    {
        var positions = new Point3DCollection(mesh.VertexCount);
        foreach (var v in mesh.Vertices) positions.Add(new Point3D(v.X, v.Y, v.Z));

        var indices = new Int32Collection();
        foreach (var face in mesh.Faces)
            if (face.IndexCount == 3)
            {
                indices.Add(face.Indices[0]);
                indices.Add(face.Indices[1]);
                indices.Add(face.Indices[2]);
            }

        var geometry = new MeshGeometry3D { Positions = positions, TriangleIndices = indices };
        if (mesh.HasNormals && mesh.Normals.Count == mesh.VertexCount)
        {
            var normals = new Vector3DCollection(mesh.VertexCount);
            foreach (var n in mesh.Normals) normals.Add(new Vector3D(n.X, n.Y, n.Z));
            geometry.Normals = normals;
        }

        if (geometry.CanFreeze) geometry.Freeze();
        return geometry;
    }

    private static (WpfMaterial wpf, ModelMaterial described) Describe(
        AssimpMaterial? material, int ordinal, IReadOnlyList<Color> palette, ref int untinted)
    {
        var name = string.IsNullOrWhiteSpace(material?.Name) ? $"Material {ordinal}" : material!.Name;
        Color? colour = null;
        string? texture = null;

        if (material is not null)
        {
            if (material.HasColorDiffuse)
            {
                var c = material.ColorDiffuse; // RGBA 0..1; ignore alpha so a 0-alpha material isn't invisible
                colour = Color.FromRgb(ToByte(c.X), ToByte(c.Y), ToByte(c.Z));
            }
            if (material.HasTextureDiffuse)
                texture = string.IsNullOrWhiteSpace(material.TextureDiffuse.FilePath)
                    ? "texture" : material.TextureDiffuse.FilePath;
        }

        // No colour and no texture in the file → give it a distinct categorical colour so it's separable.
        if (colour is null && texture is null && palette.Count > 0)
            colour = palette[untinted++ % palette.Count];

        var brush = new SolidColorBrush(colour ?? Colors.LightGray);
        brush.Freeze();
        WpfMaterial wpf = new DiffuseMaterial(brush);
        wpf.Freeze();
        return (wpf, new ModelMaterial(name, colour, texture));
    }

    private static IReadOnlyList<string> DescribeUnsupported(Scene? scene, bool hasBones)
    {
        var list = new List<string>();
        if (scene is null) return list;
        if (scene.Animations.Count > 0) list.Add($"{scene.Animations.Count} animation(s)");
        if (scene.Cameras.Count > 0) list.Add($"{scene.Cameras.Count} camera(s)");
        if (scene.Lights.Count > 0) list.Add($"{scene.Lights.Count} light(s)");
        if (hasBones) list.Add("skeletal rig (bones) — shown in bind pose");
        return list;
    }

    private static byte ToByte(float v) => (byte)(v <= 0 ? 0 : v >= 1 ? 255 : v * 255f + 0.5f);
}
