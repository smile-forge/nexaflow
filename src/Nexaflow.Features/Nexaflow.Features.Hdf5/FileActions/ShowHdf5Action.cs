using Nexaflow.Features.Common;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Hdf5.FileActions;

/// <summary>
/// Opens an HDF5 file in the viewer. The file map claims <c>.h5</c> / <c>.hdf5</c> / <c>.he5</c> for it, and every
/// path inside one, so a dataset double-clicked in a browsed file opens the viewer at that dataset.
/// </summary>
public sealed class ShowHdf5Action(IShellServices shell) : IFileAction, ICacheable
{
    public static string? StaticExperienceId => "/hdf5";

    public string ExperienceId => "/hdf5";
    public string ExperienceDescription => Str.Get("Hdf5.Action.OpenDescription");

    public string DisplayName => Str.Get("Hdf5.Action.Open");
    public string Icon        => "🗃";
    public string? Tooltip    => Str.Get("Hdf5.Action.OpenTooltip");

    public bool IsDestructive         => false;
    public bool SupportsMultipleFiles => false;
    public bool RequiresRefresh       => false;
    public bool CanPerformAction      => true;
    public bool OpensViewer           => true;

    /// <summary>The viewer reads through the VFS, so a file inside an archive — or a dataset inside a file — opens too.</summary>
    public bool RequiresFullyBackedPath => false;

    public bool PerformAction(string filePath)
    {
        var (file, node) = Hdf5Location.Split(filePath, VirtualFileSystem.Instance);
        if (!Hdf5Location.HasHdf5Extension(file)) return false;

        var parameters = new Dictionary<string, string> { ["path"] = file };
        if (node is not null) parameters["node"] = node;
        shell.OpenTab(Hdf5TabRegistration.StaticPageKind, parameters);
        return true;
    }

    public bool PerformAction(IEnumerable<string> filePaths)
    {
        foreach (var path in filePaths) return PerformAction(path);
        return false;
    }
}
