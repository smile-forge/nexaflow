using System.Collections.Generic;
using System.Linq;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.Dicom.FileActions;

/// <summary>
/// Opens one or more DICOM files (a <c>DICOMDIR</c>, or <c>.dcm</c>/<c>.dicom</c> instances) in the DICOM
/// viewer tab. Matched to files via the <c>/dicom</c> experience (extension + <c>DICM</c> magic number).
/// </summary>
public sealed class ShowDicomAction : IFileAction, ICacheable
{
    private readonly IShellServices _shell;

    public ShowDicomAction(IShellServices shell) => _shell = shell;

    public bool IsDestructive => false;
    public bool SupportsMultipleFiles => true;
    public string Icon => "🩻";
    public string DisplayName => "As DICOM";
    public static string? StaticExperienceId => "/dicom";
    public string ExperienceId => "/dicom";
    public string ExperienceDescription => "DICOM Viewer";
    public bool RequiresRefresh => false;
    public bool CanPerformAction => true;
    public bool OpensViewer => true;

    public bool PerformAction(string filePath)
    {
        _shell.OpenTab(DicomTabRegistration.StaticPageKind, new Dictionary<string, string> { ["path"] = filePath });
        return true;
    }

    public bool PerformAction(IEnumerable<string> filePaths)
    {
        var files = filePaths.ToList();
        if (files.Count == 0) return false;
        _shell.OpenTab(DicomTabRegistration.StaticPageKind,
            new Dictionary<string, string> { ["paths"] = string.Join('|', files) });
        return true;
    }
}
