using System.Collections.Generic;
using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.Features.Executable.ViewModels;
using Nexaflow.Features.Executable.Views;

namespace Nexaflow.Features.Executable;

/// <summary>
/// Registers the PE inspector page. Opened by the "Inspect" file action, and again by itself when a
/// node in the dependency graph is clicked — each dependency becomes its own tab, with breadcrumbs
/// built from its real path so the trail back to the module's folder comes for free.
/// </summary>
public sealed class ExecutableTabRegistration(IShellServices shell) : IPageRegistration
{
    public static string StaticPageKind => "Executable";
    public string PageKind => StaticPageKind;

    public IReadOnlyList<PageParameter> Parameters =>
    [
        new("path", "The Windows binary to inspect (.exe, .dll, .sys, or any Portable Executable)."),
    ];

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var path  = pageParams?.GetValueOrDefault("path") ?? string.Empty;
        var title = string.IsNullOrEmpty(path) ? "Executable" : Path.GetFileName(path);

        var page = new Page
        {
            Title      = title,
            Icon       = "🔬",
            PageParams = pageParams,
        };

        page.ContentFactory = () =>
        {
            // Built here rather than above so opening a tab stays cheap: the view-model queues the
            // parse in its constructor, and a page definition may be created without ever being shown.
            var vm = new ExecutableViewModel(path, shell);
            page.Closed += (_, _) => vm.Dispose();
            return new ExecutableView(vm);
        };

        page.SetFileBreadcrumbs(path, title);
        return page;
    }
}
