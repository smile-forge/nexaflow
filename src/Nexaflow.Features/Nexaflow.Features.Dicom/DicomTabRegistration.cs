using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexaflow.Features.Common;
using Nexaflow.Features.Dicom.Services;
using Nexaflow.Features.Dicom.ViewModels;
using Nexaflow.Features.Dicom.Views;

namespace Nexaflow.Features.Dicom;

/// <summary>
/// Registers the DICOM viewer page. Accepts either a single <c>path</c> (a <c>DICOMDIR</c>, a folder of
/// instances, or one <c>.dcm</c>) or a pipe-separated <c>paths</c> selection. The same params identify the
/// tab, so a report opened elsewhere can point its breadcrumb back here (see
/// <see cref="OriginBreadcrumbs"/>).
/// </summary>
public sealed class DicomTabRegistration : IPageRegistration
{
    private readonly IShellServices _shell;

    public DicomTabRegistration(IShellServices shell)
    {
        _shell = shell;
        // Install fo-dicom's native codecs once, up front, so the first render doesn't stall on setup.
        DicomBootstrap.EnsureInitialized();
    }

    public static string StaticPageKind => "Dicom";
    public string PageKind => StaticPageKind;

    public IReadOnlyList<PageParameter> Parameters =>
    [
        new("path", "A DICOMDIR, a folder of DICOM instances, or a single .dcm file", Required: false),
        new("paths", "Pipe-separated DICOM instance paths (a multi-file selection)", Required: false),
    ];

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var paths = ResolvePaths(pageParams);
        var title = paths.Count switch
        {
            0 => "DICOM",
            1 => DisplayName(paths[0]),
            _ => $"DICOM ({paths.Count})",
        };

        var page = new Page
        {
            Title = title,
            Icon = "🩻",
        };

        // Params that re-open THIS exact tab — carried by any report's origin breadcrumb.
        var originParams = pageParams is not null
            ? new Dictionary<string, string>(pageParams)
            : new Dictionary<string, string> { ["path"] = paths.FirstOrDefault() ?? string.Empty };

        page.ContentFactory = () =>
        {
            var vm = new DicomViewModel(paths, _shell, originParams);
            page.Closed += (_, _) => vm.Dispose();
            return new DicomView(vm);
        };

        // A DICOMDIR/folder points its parent crumb at the containing folder; a single file at its directory.
        if (paths.Count == 1)
            page.SetFileBreadcrumbs(paths[0], title);
        else if (paths.Count > 1)
            page.SetMultiFileBreadcrumbs(paths, $"{paths.Count} DICOM files");

        return page;
    }

    private static IReadOnlyList<string> ResolvePaths(Dictionary<string, string>? pageParams)
    {
        if (pageParams is null) return [];
        if (pageParams.TryGetValue("paths", out var multi) && !string.IsNullOrWhiteSpace(multi))
            return multi.Split('|', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (pageParams.TryGetValue("path", out var single) && !string.IsNullOrWhiteSpace(single))
            return [single];
        return [];
    }

    private static string DisplayName(string path)
    {
        if (Directory.Exists(path)) return new DirectoryInfo(path).Name;
        if (string.Equals(Path.GetFileName(path), "DICOMDIR", StringComparison.OrdinalIgnoreCase))
        {
            var dir = Path.GetDirectoryName(path);
            return string.IsNullOrEmpty(dir) ? "DICOMDIR" : new DirectoryInfo(dir).Name;
        }
        return Path.GetFileName(path);
    }
}
