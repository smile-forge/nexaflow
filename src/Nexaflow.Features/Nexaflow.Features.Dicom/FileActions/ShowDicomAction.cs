using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Icons;
using Nexaflow.Visuals.Common.Localization;

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
    public IconRef Icon => IconRef.Emoji("🩻");
    public string DisplayName => Str.Get("Dicom.Action.AsDicom");
    public static string? StaticExperienceId => "/dicom";
    public string ExperienceId => "/dicom";
    public string ExperienceDescription => Str.Get("Dicom.Action.ViewerDescription");
    public bool RequiresRefresh => false;
    public bool CanPerformAction => true;
    public bool OpensViewer => true;

    public Task<bool> PerformActionAsync(string filePath, CancellationToken ct = default)
    {
        _shell.OpenTab(DicomTabRegistration.StaticPageKind, new Dictionary<string, string> { ["path"] = filePath });
        return Task.FromResult(true);
    }

    public Task<bool> PerformActionAsync(IEnumerable<string> filePaths, CancellationToken ct = default)
    {
        var files = filePaths.ToList();
        if (files.Count == 0) return Task.FromResult(false);
        _shell.OpenTab(DicomTabRegistration.StaticPageKind,
            new Dictionary<string, string> { ["paths"] = string.Join('|', files) });
        return Task.FromResult(true);
    }
}
