using System.Windows;
using Microsoft.Extensions.Logging;
using Nexaflow.Core.Controls;
using Nexaflow.Core.Services.Logging;
using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Viewing;

namespace Nexaflow.Core.Services;

/// <summary>
/// The view each open page is drawn with. A page holds its view-model; its view is made here the first time the page is
/// shown, by whichever view is marked as drawing that view-model, and kept until the page closes, so a tab moved between
/// panes or windows keeps its view and what is in it.
/// <para>
/// A page whose view-model or view could not be made is drawn by <see cref="PageLoadErrorView"/>, saying why, rather than
/// left blank: a blank tab is indistinguishable from a feature that drew nothing.
/// </para>
/// <para>
/// One per session, made by the app (it alone knows the views) through <see cref="ShellPlatform"/>: every window draws its
/// panes with it, and every runtime's <see cref="ShellServices"/> lets a page's view go when its tab closes.
/// </para>
/// </summary>
/// <param name="registry">Finds the view that draws a page's view-model.</param>
/// <param name="failedView">Draws a page that could not be made, with the exception that stopped it.</param>
/// <param name="log">Where the exception that stopped a page is put on record, as a fault.</param>
public sealed class PageViews(ViewRegistry registry, Func<Page, Exception?, FrameworkElement> failedView, ILogger<PageViews> log) : IPageViews
{

    /// <summary>
    /// The app's: each page drawn by the view <paramref name="catalog"/> marks as drawing its view-model, and one that could not be
    /// made by <see cref="PageLoadErrorView"/>, naming any of <paramref name="dependencies"/> missing as the likely cause, offering
    /// them on the About page through <paramref name="showComponents"/>, and pointing at the crash log of <paramref name="logging"/>,
    /// where the failure is recorded.
    /// </summary>
    public static PageViews FromCatalog(FeatureCatalog catalog, ExternalDependencyRegistry dependencies, Action showComponents, ShellLogging logging)
        => new(new ViewRegistry(catalog.ViewTypeFor),
               (page, error) => new PageLoadErrorView(page, error, dependencies, showComponents, logging.Crash.CurrentPath),
               logging.Factory.CreateLogger<PageViews>());

    /// <summary>The app's views, which every window hands down so a feature's hosts draw with them too.</summary>
    public ViewRegistry Registry => registry;

    private readonly Dictionary<Page, FrameworkElement> _views = [];

    /// <summary>The page's view if it has been shown, without making one.</summary>
    public FrameworkElement? Shown(Page page) => _views.GetValueOrDefault(page);

    /// <summary>The page's view, made on first use.</summary>
    public FrameworkElement ViewOf(Page page)
    {
        if (_views.TryGetValue(page, out var view)) return view;
        view = Make(page);
        _views[page] = view;
        return view;
    }

    /// <summary>Lets go of a closed page's view, disposing it where it holds anything.</summary>
    public void Release(Page page)
    {
        if (_views.Remove(page, out var view) && view is IDisposable disposable)
            try { disposable.Dispose(); } catch { /* a view failing to let go must not stop the tab closing */ }
    }

    private FrameworkElement Make(Page page)
    {
        if (page.GetOrCreateViewModel() is not { } model) return Failed(page, page.LoadException);

        try
        {
            return registry.Create(model);
        }
        catch (Exception ex)
        {
            return Failed(page, ex);
        }
    }

    /// <summary>
    /// The page drawn as the failure it is, the exception put on record first. The failure was caught, so it never reaches the
    /// dispatcher's handler, and nothing else writes it down — a customer reported a PDF tab failing to open, and their crash log
    /// had no record of it.
    /// </summary>
    private FrameworkElement Failed(Page page, Exception? error)
    {
        if (error is not null) log.LogError(error, "the page '{PageKind}' could not be shown", page.PageKind);
        return failedView(page, error);
    }
}
