using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Icons;
using Nexaflow.Visuals.Common.Localization;

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
    public IconRef Icon => IconRef.Emoji("🩻");
    public string DisplayName => Str.Get("Dicom.Action.AsDicom");
    public bool RequiresRefresh => false;
    public bool CanPerformAction => true;
    public bool AppliesToRoot => true;
    public bool AppliesToDrives => true;   // a DICOM CD is usually opened from its drive root

    // Offered when the folder contains a DICOMDIR or at least one .dcm instance.
    public string[]? ContainsFileGlobs => ["DICOMDIR", "*.dcm", "*.dicom"];

    public Task<bool> PerformActionAsync(string folderPath, CancellationToken ct = default)
    {
        _shell.OpenTab(DicomTabRegistration.StaticPageKind, new Dictionary<string, string> { ["path"] = folderPath });
        return Task.FromResult(true);
    }

    public Task<bool> PerformActionAsync(IEnumerable<string> folderPaths, CancellationToken ct = default)
    => folderPaths.FirstOrDefault() is { } first ? PerformActionAsync(first, ct) : Task.FromResult(false);
}
