using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.Features.Notebook.ViewModels;
using Nexaflow.Features.Notebook.Views;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Notebook;

/// <summary>Registers the "Notebook" page: a Jupyter <c>.ipynb</c> rendered as cells (markdown + highlighted
/// code) with a code-structure outline. Discovered by <c>FeatureManager</c> via <see cref="StaticPageKind"/>.</summary>
public sealed class NotebookTabRegistration(IShellServices shell, IVirtualFileSystem vfs) : IPageRegistration
{
    public static string StaticPageKind => "Notebook";
    public string PageKind => StaticPageKind;

    public IReadOnlyList<PageParameter> Parameters =>
    [
        new("path", "Full path of the notebook (.ipynb) to open.", OnDisk: true),
    ];

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var path  = pageParams?.GetValueOrDefault("path") ?? string.Empty;
        var title = string.IsNullOrEmpty(path) ? Str.Get("Notebook.Page.Title") : Path.GetFileName(path);

        var page = new Page
        {
            Title       = title,
            Icon        = "📓",
            ContentFactory = () => new NotebookView(new NotebookViewModel(path, shell, vfs)),
        };
        page.SetFileBreadcrumbs(path, title);
        return page;
    }
}
