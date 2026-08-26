using Nexaflow.Features.Common;
using Nexaflow.Features.Solver.ViewModels;
using Nexaflow.Features.Solver.Views;

namespace Nexaflow.Features.Solver;

/// <summary>
/// Advertises the Solver page.
/// <para>
/// It takes no parameters, and that is the whole of why pinning the tab does not pin what was typed
/// into it: a pinned button carries a page kind and its <c>PageParams</c>, so a page with none
/// re-opens as an empty surface by construction rather than by remembering to clear anything.
/// </para>
/// </summary>
public sealed class SolverTabRegistration(SolverConfig config, IShellServices shell, IAIService ai) : IPageRegistration
{
    /// <summary>The page kind, read by reflection without instantiating the registration.</summary>
    public static string StaticPageKind => "Solver";

    /// <inheritdoc/>
    public string PageKind => StaticPageKind;

    /// <summary>Opens standalone and is useful on its own, so it belongs in the ribbon and quick-open lists.</summary>
    public bool CanBeContextItem => true;

    /// <inheritdoc/>
    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var page = new Page
        {
            Title = "Solver",
            Icon = "🧮",
            Breadcrumbs = { new BreadcrumbSegment { Label = "Solver" } },
        };

        // Built lazily. A definition is speculative — the shell creates one just to read a title for
        // a menu — so nothing outside this factory may touch the algebra engine or build a view.
        page.ContentFactory = () =>
        {
            var vm = new SolverViewModel(config, shell, ai);
            page.Closed += (_, _) => vm.Dispose();
            return new SolverView(vm);
        };

        return page;
    }
}
