using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.Features.Model3D.Loaders;
using Nexaflow.Features.Model3D.ViewModels;
using Nexaflow.Features.Model3D.Views;

namespace Nexaflow.Features.Model3D;

/// <summary>
/// Registers the "Model3D" page: an interactive 3D viewer for mesh files (STL/OBJ/3DS/PLY), glTF/glb and a
/// wide range of formats via Assimp (FBX, 3MF, AMF, Collada, …), with rotate/zoom/pan, a wireframe toggle,
/// a stats footer and a material inspector. Discovered by
/// <c>FeatureManager</c> via <see cref="StaticPageKind"/>.
/// </summary>
public sealed class Model3DTabRegistration : IPageRegistration
{
    // One registry per workspace registration — cheap to build, shared by every tab it opens.
    private readonly ModelLoaderRegistry _registry = new();

    public static string StaticPageKind => "Model3D";
    public string PageKind => StaticPageKind;

    public IReadOnlyList<PageParameter> Parameters =>
    [
        new("path", "Path to a 3D model file (.stl, .obj, .3ds, .ply, .gltf, .glb, .fbx, .3mf, .amf, .dae, …).", Required: true),
    ];

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var path = pageParams?.GetValueOrDefault("path") ?? string.Empty;
        var title = string.IsNullOrEmpty(path) ? "3D Model" : Path.GetFileName(path);

        return new Page
        {
            Title = title,
            Icon = "🧊",
            Breadcrumbs = { new BreadcrumbSegment { Label = title } },
            ContentFactory = () => new Model3DView(new Model3DViewModel(path, _registry)),
        };
    }
}
