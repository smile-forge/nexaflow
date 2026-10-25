using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.Features.Model3D.Loaders;
using Nexaflow.Features.Model3D.ViewModels;
using Nexaflow.Features.Model3D.Views;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Common.Localization;

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
    private readonly IVirtualFileSystem _vfs;

    public Model3DTabRegistration(IVirtualFileSystem vfs) => _vfs = vfs;

    public static string StaticPageKind => "Model3D";
    public string PageKind => StaticPageKind;

    public IReadOnlyList<PageParameter> Parameters =>
    [
        new("path", "Path to a 3D model file (.stl, .obj, .3ds, .ply, .gltf, .glb, .fbx, .3mf, .amf, .dae, …).", Required: true, OnDisk: true),
    ];

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var path = pageParams?.GetValueOrDefault("path") ?? string.Empty;
        var title = string.IsNullOrEmpty(path) ? Str.Get("Model3D.Page.Title") : Path.GetFileName(path);

        var page = new Page
        {
            Title = title,
            Icon = "🧊",
            ContentFactory = () => new Model3DView(new Model3DViewModel(path, _vfs, _registry)),
        };
        page.SetFileBreadcrumbs(path, title);
        return page;
    }
}
