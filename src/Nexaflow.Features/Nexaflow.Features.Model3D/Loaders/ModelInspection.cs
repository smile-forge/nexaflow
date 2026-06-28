using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using WpfModel3D = System.Windows.Media.Media3D.Model3D;

namespace Nexaflow.Features.Model3D.Loaders;

/// <summary>
/// Walks a WPF <see cref="Model3DGroup"/> to derive the stats the footer/inspector need — triangle, vertex
/// and mesh counts — describe each mesh's material, and, when a file gives its meshes no distinguishing
/// colours (and no textures), re-skin them with a categorical palette so you can tell which part is which.
/// Used by the loaders that produce WPF geometry directly (the HelixToolkit readers); the group must be
/// mutable (unfrozen) when passed in so the re-skin can take effect before the loader freezes it.
/// </summary>
internal static class ModelInspection
{
    internal readonly record struct GeometryStats(
        int TriangleCount, int VertexCount, int MeshCount, IReadOnlyList<ModelMaterial> Materials);

    private readonly record struct MeshEntry(GeometryModel3D Gm, MeshGeometry3D Mesh, Color? Color, string? Texture);

    public static GeometryStats CollectAndColorize(Model3DGroup group, IReadOnlyList<Color> palette)
    {
        var meshes = new List<MeshEntry>();
        Gather(group, meshes);

        int triangles = 0, vertices = 0;
        foreach (var e in meshes)
        {
            vertices += e.Mesh.Positions.Count;
            triangles += e.Mesh.TriangleIndices.Count > 0
                ? e.Mesh.TriangleIndices.Count / 3
                : e.Mesh.Positions.Count / 3;
        }

        // Tint indistinct meshes using the shared rule (same as glTF/FBX). The HelixToolkit readers don't
        // surface material names, so each mesh is treated as its own material here.
        var resolved = CategoricalTinting.Resolve(
            meshes.Select(e => (e.Color, e.Texture is not null)).ToList(), palette);

        var materials = new List<ModelMaterial>(meshes.Count);
        for (int i = 0; i < meshes.Count; i++)
        {
            var e = meshes[i];
            var colour = resolved[i];
            if (e.Texture is null && colour is { } c) ReSkin(e.Gm, c);
            materials.Add(new ModelMaterial($"Material {i + 1}", colour, e.Texture));
        }

        return new GeometryStats(triangles, vertices, meshes.Count, materials);
    }

    private static void Gather(WpfModel3D? model, List<MeshEntry> into)
    {
        switch (model)
        {
            case Model3DGroup g:
                foreach (var child in g.Children) Gather(child, into);
                break;

            case GeometryModel3D gm when gm.Geometry is MeshGeometry3D mesh:
                Color? colour = null;
                string? texture = null;
                if ((gm.Material ?? gm.BackMaterial) is { } material) Read(material, ref colour, ref texture);
                into.Add(new MeshEntry(gm, mesh, colour, texture));
                break;
        }
    }

    private static void ReSkin(GeometryModel3D gm, Color colour)
    {
        var brush = new SolidColorBrush(colour);
        brush.Freeze();
        var material = new DiffuseMaterial(brush);
        material.Freeze();
        gm.Material = material;
        gm.BackMaterial = material;
    }

    /// <summary>Best-effort read of a WPF material: a diffuse colour from a <see cref="SolidColorBrush"/> and a
    /// texture path from an <see cref="ImageBrush"/>, looking through a <see cref="MaterialGroup"/>.</summary>
    private static void Read(Material material, ref Color? colour, ref string? texture)
    {
        switch (material)
        {
            case MaterialGroup mg:
                foreach (var child in mg.Children) Read(child, ref colour, ref texture);
                break;
            case DiffuseMaterial dm:
                ReadBrush(dm.Brush, ref colour, ref texture);
                break;
            case EmissiveMaterial em:
                ReadBrush(em.Brush, ref colour, ref texture);
                break;
            case SpecularMaterial sm:
                ReadBrush(sm.Brush, ref colour, ref texture);
                break;
        }
    }

    private static void ReadBrush(Brush? brush, ref Color? colour, ref string? texture)
    {
        switch (brush)
        {
            case SolidColorBrush scb:
                colour ??= scb.Color;
                break;
            case ImageBrush ib when ib.ImageSource is BitmapImage { UriSource: { } uri }:
                texture ??= uri.IsAbsoluteUri && uri.IsFile ? uri.LocalPath : uri.ToString();
                break;
        }
    }
}
