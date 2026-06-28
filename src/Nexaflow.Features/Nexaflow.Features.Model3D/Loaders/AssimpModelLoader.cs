using System.IO;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using SharpAssimp;
using AssimpMaterial = SharpAssimp.Material;
using WpfMaterial = System.Windows.Media.Media3D.Material;

namespace Nexaflow.Features.Model3D.Loaders;

/// <summary>
/// The catch-all mesh loader, backed by the native Assimp library (via SharpAssimp). Assimp imports dozens
/// of formats; this loader claims the genuine 3D-model ones that the dedicated HelixToolkit/SharpGLTF
/// loaders don't already handle (FBX, 3MF, AMF, Collada, Blender, DirectX X, and many more). We let Assimp
/// triangulate, smooth-normal where missing, weld vertices and bake node transforms, then map meshes +
/// diffuse materials to WPF — animations, cameras, lights and rigs are reported, not rendered.
/// </summary>
/// <remarks>
/// Registered after the dedicated loaders, so STL/OBJ/3DS/PLY (HelixToolkit) and glTF/glb (SharpGLTF) keep
/// their tuned readers; Assimp covers the long tail. Ambiguous or non-mesh formats Assimp also lists
/// (e.g. <c>.xml</c>, <c>.raw</c>, <c>.uc</c>, motion-capture <c>.bvh</c>) are deliberately excluded so they
/// don't hijack other file types. The native binary ships for win-x64/x86; elsewhere the import throws and
/// the viewer shows a friendly error rather than crashing.
/// </remarks>
public sealed class AssimpModelLoader : IModelLoader
{
    public string Name => "Assimp (FBX/3MF/AMF/Collada/…)";

    // Curated from Assimp's import list — real 3D-model formats minus those the dedicated loaders own
    // (STL/OBJ/3DS/PLY/glTF/glb) and minus generic/animation-only/ambiguous ones.
    public IReadOnlyList<string> SupportedExtensions { get; } =
    [
        ".3d", ".3mf", ".ac", ".ac3d", ".acc", ".amf", ".ase", ".b3d", ".blend", ".bsp", ".cob", ".dae",
        ".dxf", ".enff", ".fbx", ".hmp", ".ifc", ".ifczip", ".iqm", ".irr", ".irrmesh", ".lwo", ".lws",
        ".lxo", ".md2", ".md3", ".md5mesh", ".mdc", ".mdl", ".mesh", ".ms3d", ".ndo", ".nff", ".off",
        ".ogex", ".pmx", ".q3o", ".q3s", ".sib", ".smd", ".ter", ".vrm", ".x", ".x3d", ".x3db", ".xgl",
        ".zae", ".zgl",
    ];

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
        int triangles = 0, vertices = 0;
        var hasBones = false;

        if (scene is { MeshCount: > 0 })
        {
            var source = scene.Materials;
            // Resolve a display colour per source material with the shared tinting rule (so multiple
            // materials the file left the same colour become individually visible).
            var described = source.Select(Describe).ToList();
            var resolved = CategoricalTinting.Resolve(
                described.Select(d => (d.Colour, d.Texture is not null)).ToList(), palette);

            var wpfByIndex = new WpfMaterial?[source.Count]; // one shared material per source material
            var used = new bool[source.Count];

            foreach (var mesh in scene.Meshes)
            {
                var geometry = BuildMesh(mesh);
                if (geometry.Positions.Count == 0) continue;
                triangles += geometry.TriangleIndices.Count / 3;
                vertices += geometry.Positions.Count;
                hasBones |= mesh.HasBones;

                var index = mesh.MaterialIndex >= 0 && mesh.MaterialIndex < source.Count ? mesh.MaterialIndex : -1;
                var wpfMaterial = index >= 0
                    ? (wpfByIndex[index] ??= CategoricalTinting.ToDiffuseMaterial(resolved[index]))
                    : CategoricalTinting.ToDiffuseMaterial(null);
                if (index >= 0) used[index] = true;
                group.Children.Add(new GeometryModel3D(geometry, wpfMaterial) { BackMaterial = wpfMaterial });
            }

            // Inspector: one row per material actually used, with its resolved colour.
            for (var i = 0; i < source.Count; i++)
            {
                if (!used[i]) continue;
                materials.Add(new ModelMaterial(
                    string.IsNullOrWhiteSpace(source[i].Name) ? $"Material {materials.Count + 1}" : source[i].Name,
                    resolved[i], described[i].Texture));
            }
        }

        if (group.CanFreeze) group.Freeze();

        return new LoadedModel
        {
            Geometry = group,
            FormatName = Path.GetExtension(path).TrimStart('.').ToUpperInvariant(),
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

    /// <summary>Reads an Assimp material's diffuse colour and whether it has a diffuse texture.
    /// The shared tinter decides the final display colour.</summary>
    private static (Color? Colour, string? Texture) Describe(AssimpMaterial material)
    {
        Color? colour = null;
        string? texture = null;

        if (material.HasColorDiffuse)
        {
            var c = material.ColorDiffuse; // RGBA 0..1; ignore alpha so a 0-alpha material isn't invisible
            colour = Color.FromRgb(ToByte(c.X), ToByte(c.Y), ToByte(c.Z));
        }
        if (material.HasTextureDiffuse)
            texture = string.IsNullOrWhiteSpace(material.TextureDiffuse.FilePath)
                ? "texture" : material.TextureDiffuse.FilePath;

        return (colour, texture);
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
