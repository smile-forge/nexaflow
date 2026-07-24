using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.Dicom.FileActions;

/// <summary>
/// Folder action that opens a directory of DICOM content in the viewer — offered for a folder that holds a
/// <c>DICOMDIR</c> index (a burned study CD) or a run of <c>.dcm</c> instances. The viewer itself finds the
/// DICOMDIR or scans for instances, so this only needs to route the folder path.
/// </summary>
public sealed class DicomFolderAction : IFolderAction, ICacheable
{
    private readonly IShellServices _shell;

    public DicomFolderAction(IShellServices shell) => _shell = shell;

    public bool IsDestructive => false;
    public bool SupportsMultipleFiles => false;
    public string Icon => "🩻";
    public string DisplayName => "As DICOM";
    public bool RequiresRefresh => false;
    public bool CanPerformAction => true;
    public bool AppliesToRoot => true;
    public bool AppliesToDrives => true;   // a DICOM CD is usually opened from its drive root

    // Offered when the folder contains a DICOMDIR or at least one .dcm instance.
    public string[]? ContainsFileGlobs => ["DICOMDIR", "*.dcm", "*.dicom"];

    public bool PerformAction(string folderPath)
    {
        _shell.OpenTab(DicomTabRegistration.StaticPageKind, new Dictionary<string, string> { ["path"] = folderPath });
        return true;
    }

    public bool PerformAction(IEnumerable<string> folderPaths)
    {
        var first = folderPaths.FirstOrDefault();
        return first is not null && PerformAction(first);
    }
}
