using System.IO;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using SharpGLTF.Geometry;
using SharpGLTF.Schema2;
using GltfMaterial = SharpGLTF.Schema2.Material;
using WpfMaterial = System.Windows.Media.Media3D.Material;

namespace Nexaflow.Features.Model3D.Loaders;

/// <summary>
/// Loads glTF / glb via SharpGLTF and converts it to WPF geometry. SharpGLTF evaluates the scene graph
/// into world-space triangles (applying node transforms and instancing), which we group per material into
/// frozen <see cref="MeshGeometry3D"/> meshes. Only base-colour shading is reproduced — richer glTF
/// content (animations, cameras, lights, skins, PBR textures) is reported as unsupported, not rendered.
/// </summary>
public sealed class GltfModelLoader : IModelLoader
{
    public string Name => "glTF";

    public IReadOnlyList<string> SupportedExtensions { get; } = [".gltf", ".glb"];

    public bool CanLoad(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public LoadedModel Load(string path, IReadOnlyList<Color> palette)
    {
        var model = ModelRoot.Load(path);
        var scene = model.DefaultScene ?? (model.LogicalScenes.Count > 0 ? model.LogicalScenes[0] : null);

        var group = new Model3DGroup();
        var materials = new List<ModelMaterial>();
        int triangles = 0, vertices = 0, untinted = 0;

        if (scene is not null)
        {
            // Group triangles by source material (keyed by logical index; -1 = no material) so each
            // material becomes one shaded mesh.
            var buckets = new Dictionary<int, MeshAccumulator>();
            foreach (var (a, b, c, material) in scene.EvaluateTriangles())
            {
                var key = material?.LogicalIndex ?? -1;
                if (!buckets.TryGetValue(key, out var accum))
                    buckets[key] = accum = new MeshAccumulator();
                accum.AddTriangle(a, b, c);
            }

            foreach (var (key, accum) in buckets)
            {
                var mesh = accum.Build();
                if (mesh.Positions.Count == 0) continue;
                triangles += accum.TriangleCount;
                vertices += mesh.Positions.Count;

                var material = key >= 0 ? model.LogicalMaterials[key] : null;
                var (wpfMaterial, described) = Describe(material, materials.Count + 1, palette, ref untinted);
                materials.Add(described);
                group.Children.Add(new GeometryModel3D(mesh, wpfMaterial) { BackMaterial = wpfMaterial });
            }
        }

        if (group.CanFreeze) group.Freeze();

        var initialView = ExtractCamera(model);
        var isGlb = Path.GetExtension(path).Equals(".glb", StringComparison.OrdinalIgnoreCase);
        return new LoadedModel
        {
            Geometry = group,
            FormatName = isGlb ? "glTF (GLB)" : "glTF",
            TriangleCount = triangles,
            VertexCount = vertices,
            MeshCount = group.Children.Count,
            Materials = materials,
            UnsupportedElements = DescribeUnsupported(model, usedCamera: initialView is not null),
            InitialView = initialView,
        };
    }

    /// <summary>The first node with a camera becomes the authored view: glTF cameras look down local -Z
    /// with +Y up, so derive world position/look/up from the node's world matrix.</summary>
    private static ModelCameraView? ExtractCamera(ModelRoot model)
    {
        foreach (var node in model.LogicalNodes)
        {
            if (node.Camera is null) continue;
            var m = node.WorldMatrix; // translation in M41..M43; basis rows M11.. / M21.. / M31..
            var look = new Vector3D(-m.M31, -m.M32, -m.M33);
            if (look.Length < 1e-9) continue;
            look.Normalize();
            var up = new Vector3D(m.M21, m.M22, m.M23);
            if (up.Length < 1e-9) up = new Vector3D(0, 1, 0); else up.Normalize();
            return new ModelCameraView(new Point3D(m.M41, m.M42, m.M43), look, up);
        }
        return null;
    }

    private static (WpfMaterial wpf, ModelMaterial described) Describe(
        GltfMaterial? material, int ordinal, IReadOnlyList<Color> palette, ref int untinted)
    {
        var name = string.IsNullOrWhiteSpace(material?.Name) ? $"Material {ordinal}" : material!.Name;
        Color? color = null;
        string? texture = null;

        if (material?.FindChannel("BaseColor") is { } channel)
        {
#pragma warning disable CS0618 // Parameter (single Vector4) is the base-colour factor; ample for a viewer.
            var p = channel.Parameter; // Vector4 RGBA, linear 0..1
#pragma warning restore CS0618
            color = Color.FromScRgb(Clamp01(p.W), Clamp01(p.X), Clamp01(p.Y), Clamp01(p.Z));
            if (channel.Texture is { } tex)
                texture = string.IsNullOrWhiteSpace(tex.PrimaryImage?.Name) ? "embedded texture" : tex.PrimaryImage!.Name;
        }

        // No colour and no texture in the file → give it a distinct categorical colour so it's separable.
        if (color is null && texture is null && palette.Count > 0)
            color = palette[untinted++ % palette.Count];

        var brush = new SolidColorBrush(color ?? Colors.LightGray);
        brush.Freeze();
        WpfMaterial wpf = new DiffuseMaterial(brush);
        wpf.Freeze();
        return (wpf, new ModelMaterial(name, color, texture));
    }

    private static IReadOnlyList<string> DescribeUnsupported(ModelRoot model, bool usedCamera)
    {
        var list = new List<string>();
        if (model.LogicalAnimations.Count > 0) list.Add($"{model.LogicalAnimations.Count} animation(s)");
        if (model.LogicalCameras.Count > 0 && !usedCamera) list.Add($"{model.LogicalCameras.Count} camera(s)");
        if (model.LogicalSkins.Count > 0) list.Add($"{model.LogicalSkins.Count} skin(s) / skeletal rig");
        if (model.LogicalPunctualLights.Count > 0) list.Add($"{model.LogicalPunctualLights.Count} punctual light(s)");
        if (model.LogicalImages.Count > 0)
            list.Add($"{model.LogicalImages.Count} texture image(s) — base colour shown, full PBR not rendered");
        return list;
    }

    private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

    /// <summary>Accumulates world-space triangles into a WPF mesh, sharing vertices by position+normal.</summary>
    private sealed class MeshAccumulator
    {
        private readonly Point3DCollection _positions = new();
        private readonly Vector3DCollection _normals = new();
        private readonly Int32Collection _indices = new();
        private readonly Dictionary<(float, float, float, float, float, float), int> _index = new();
        private bool _allHaveNormals = true;

        public int TriangleCount { get; private set; }

        public void AddTriangle(IVertexBuilder a, IVertexBuilder b, IVertexBuilder c)
        {
            _indices.Add(Resolve(a));
            _indices.Add(Resolve(b));
            _indices.Add(Resolve(c));
            TriangleCount++;
        }

        private int Resolve(IVertexBuilder vertex)
        {
            var geometry = vertex.GetGeometry();
            var p = geometry.GetPosition();
            if (!geometry.TryGetNormal(out var n)) _allHaveNormals = false;

            var key = (p.X, p.Y, p.Z, n.X, n.Y, n.Z);
            if (_index.TryGetValue(key, out var existing)) return existing;

            var idx = _positions.Count;
            _positions.Add(new Point3D(p.X, p.Y, p.Z));
            _normals.Add(new Vector3D(n.X, n.Y, n.Z));
            _index[key] = idx;
            return idx;
        }

        public MeshGeometry3D Build()
        {
            var mesh = new MeshGeometry3D { Positions = _positions, TriangleIndices = _indices };
            if (_allHaveNormals && _normals.Count == _positions.Count) mesh.Normals = _normals;
            if (mesh.CanFreeze) mesh.Freeze();
            return mesh;
        }
    }
}
