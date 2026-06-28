using System.Collections.Generic;
using System.Linq;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.Model3D.FileActions;

/// <summary>
/// Opens a 3D model file (STL/OBJ/3DS/PLY/glTF/glb/FBX) in the Model3D viewer tab. The bundled file-type map
/// points the <c>/model3d</c> experience at those extensions; this action realises it by opening the tab.
/// One model per tab — opening several files opens the first.
/// </summary>
public sealed class ShowModel3DAction : IFileAction, ICacheable
{
    private readonly IShellServices _shellServices;

    public ShowModel3DAction(IShellServices shellServices) => _shellServices = shellServices;

    public bool IsDestructive => false;
    public bool SupportsMultipleFiles => false;
    public string Icon => "🧊";
    public string DisplayName => "As 3D Model";
    public static string? StaticExperienceId => "/model3d";
    public string ExperienceId => "/model3d";
    public string ExperienceDescription => "3D model viewer: rotate/zoom/pan, wireframe, mesh stats and materials (STL/OBJ/3DS/PLY/glTF/FBX)";
    public bool RequiresRefresh => false;
    public bool CanPerformAction => true;
    public bool OpensViewer => true;

    public bool PerformAction(string filePath)
    {
        _shellServices.OpenTab("Model3D", new Dictionary<string, string> { ["path"] = filePath });
        return true;
    }

    public bool PerformAction(IEnumerable<string> filePaths)
    {
        var first = filePaths.FirstOrDefault();
        if (first is null) return false;
        return PerformAction(first);
    }
}
