using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.Features.Hdf5.ViewModels;
using Nexaflow.Features.Hdf5.Views;
using Nexaflow.IO.Common;
using Nexaflow.IO.Hdf5;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Hdf5;

/// <summary>
/// Registers the HDF5 viewer page. <c>path</c> says which file the tab is; <c>node</c> says where in it the user is
/// looking, so opening another dataset of an open file re-points that tab rather than opening a second one.
/// </summary>
public sealed class Hdf5TabRegistration(IShellServices shell) : IPageRegistration
{
    public static string StaticPageKind => "Hdf5";
    public string PageKind => StaticPageKind;

    public IReadOnlyList<PageParameter> Parameters =>
    [
        new("path", "The HDF5 file (.h5, .hdf5 or .he5): a full path on disk or inside an archive. A path that continues into the file, e.g. C:\\data\\run.h5\\measurements\\temperature, also selects that object."),
        new("node", "Absolute path of the object inside the file to select, e.g. /measurements/temperature.", Required: false, Identity: false),
    ];

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var (file, inner) = Hdf5Location.Split(pageParams?.GetValueOrDefault("path") ?? string.Empty, VirtualFileSystem.Instance);
        var node  = pageParams?.GetValueOrDefault("node") is { Length: > 0 } n ? n : inner;
        var title = string.IsNullOrEmpty(file) ? Str.Get("Hdf5.Page.Title") : Path.GetFileName(file);

        var normalized = new Dictionary<string, string> { ["path"] = file };
        if (node is not null) normalized["node"] = node;

        var page = new Page
        {
            Title      = title,
            Icon       = "🗃",
            PageParams = normalized,
        };

        page.ContentFactory = () =>
        {
            // Built here so defining a tab stays cheap: the view-model opens the file as it is constructed.
            var vm = new Hdf5ViewModel(file, node, shell, Hdf5SourceCache.Shared);
            page.Closed += (_, _) => vm.Dispose();
            return new Hdf5View(vm);
        };

        page.SetFileBreadcrumbs(file, title);
        return page;
    }
}
