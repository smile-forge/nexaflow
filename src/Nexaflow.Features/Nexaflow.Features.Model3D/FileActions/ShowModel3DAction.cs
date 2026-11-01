using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Icons;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Model3D.FileActions;

/// <summary>
/// Opens a 3D model file (STL/OBJ/3DS/PLY/glTF/glb plus FBX, 3MF, AMF, Collada and many more via Assimp)
/// in the Model3D viewer tab. The bundled file-type map
/// points the <c>/model3d</c> experience at those extensions; this action realises it by opening the tab.
/// One model per tab — opening several files opens the first.
/// </summary>
public sealed class ShowModel3DAction : IFileAction, ICacheable
{
    private readonly IShellServices _shellServices;

    public ShowModel3DAction(IShellServices shellServices) => _shellServices = shellServices;

    public bool IsDestructive => false;
    public bool SupportsMultipleFiles => false;
    public IconRef Icon => IconRef.Emoji("🧊");
    public string DisplayName => Str.Get("Model3D.Action.As3DModel");
    public static string? StaticExperienceId => "/model3d";
    public string ExperienceId => "/model3d";
    public string ExperienceDescription => "3D model viewer: rotate/zoom/pan, wireframe, mesh stats and materials (STL/OBJ/glTF/FBX/3MF/Collada and many more)";
    public bool RequiresRefresh => false;
    public bool CanPerformAction => true;
    public bool OpensViewer => true;

    public Task<bool> PerformActionAsync(string filePath, CancellationToken ct = default)
    {
        _shellServices.OpenTab("Model3D", new Dictionary<string, string> { ["path"] = filePath });
        return Task.FromResult(true);
    }

    public Task<bool> PerformActionAsync(IEnumerable<string> filePaths, CancellationToken ct = default)
    {
        var first = filePaths.FirstOrDefault();
        if (first is null) return Task.FromResult(false);
        return PerformActionAsync(first, ct);
    }
}
