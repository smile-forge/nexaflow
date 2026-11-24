using System.Windows;
using Nexaflow.Core.Controls;
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
public sealed class PageViews(ViewRegistry registry, Func<Page, Exception?, FrameworkElement> failedView)
{

    /// <summary>
    /// The app's: each page drawn by the view the feature catalogue marks as drawing its view-model, and one that could not be
    /// made by <see cref="PageLoadErrorView"/>, naming any of <paramref name="dependencies"/> missing as the likely cause.
    /// </summary>
    public static PageViews FromCatalog(ExternalDependencyRegistry dependencies)
        => new(new ViewRegistry(FeatureCatalog.Instance.ViewTypeFor), (page, error) => new PageLoadErrorView(page, error, dependencies));

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
        if (page.GetOrCreateViewModel() is not { } model) return failedView(page, page.LoadException);

        try
        {
            return registry.Create(model);
        }
        catch (Exception ex)
        {
            return failedView(page, ex);
        }
    }
}
