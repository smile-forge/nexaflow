using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Nexaflow.Features.Model3D.Loaders;

/// <summary>A material described for the inspector panel — name plus the bits we can show (diffuse
/// colour, texture reference). Both optional: many meshes carry only a colour, some only a texture.</summary>
public sealed record ModelMaterial(string Name, Color? DiffuseColor = null, string? Texture = null);

/// <summary>An authored camera viewpoint read from the file (currently only glTF carries one). The view
/// renders from <see cref="Position"/> along the unit <see cref="LookDirection"/>; the viewer fills in the
/// framing distance from the model bounds. Used as both the initial view and the "reset view" home.</summary>
public sealed record ModelCameraView(Point3D Position, Vector3D LookDirection, Vector3D UpDirection);

/// <summary>
/// The result of loading a 3D file: a <b>frozen</b> <see cref="Model3DGroup"/> ready to drop into the
/// viewport, plus the metadata the footer and inspector show. Built off the UI thread by an
/// <see cref="IModelLoader"/>; <see cref="Geometry"/> must be frozen so it can cross to the UI thread.
/// </summary>
public sealed class LoadedModel
{
    /// <summary>The frozen geometry. Empty group if the file held no renderable mesh.</summary>
    public required Model3DGroup Geometry { get; init; }

    /// <summary>The loader that produced this (e.g. "STL", "glTF") — shown in context/footer.</summary>
    public required string FormatName { get; init; }

    public int TriangleCount { get; init; }
    public int VertexCount { get; init; }

    /// <summary>Distinct renderable meshes (≈ "polygon groups").</summary>
    public int MeshCount { get; init; }

    public IReadOnlyList<ModelMaterial> Materials { get; init; } = [];

    /// <summary>Things the file declares that this viewer does not render — glTF animations, cameras,
    /// lights, skins; 3DS cameras; etc. Surfaced in the inspector so the user knows what's omitted.</summary>
    public IReadOnlyList<string> UnsupportedElements { get; init; } = [];

    /// <summary>The file's authored camera, if any (currently glTF only); null → the viewer frames the
    /// model itself. Becomes the initial view and the "reset view" home.</summary>
    public ModelCameraView? InitialView { get; init; }
}
