using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Viewing;
using Page = Nexaflow.Features.Common.Page;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// How a page gets the view it is shown with: made once from its view-model, kept while the page is open, let go when it
/// closes, and drawn as the failure when its view-model or its view cannot be made, so a broken page says why instead of
/// unwinding through tab activation.
/// <para>Needs an STA thread to construct WPF elements; opens no window.</para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("chrome-page-views")]
public class PageViewsTests
{
    private sealed class Model : IPageViewModel
    {
        public string GetContext() => string.Empty;
    }

    private sealed class ModelView(Model model) : UserControl, IDisposable
    {
        public Model Model { get; } = model;
        public int Disposed { get; private set; }
        public void Dispose() => Disposed++;
    }

    private sealed class FailingView : UserControl
    {
        public FailingView(Model model) => throw new InvalidOperationException("view ctor blew up");
    }

    private sealed class FailureView(Page page, Exception? error) : UserControl
    {
        public Page Page { get; } = page;
        public Exception? Error { get; } = error;
    }

    private static PageViews Views(Type view)
        => new(new ViewRegistry(model => model == typeof(Model) ? view : null), (page, error) => new FailureView(page, error));

    private static Page PageOf(Func<IPageViewModel> factory) => new() { Title = "Test", PageKind = "Test", ViewModelFactory = factory };

    [TestMethod]
    public void APage_IsDrawnByTheViewOfItsModel_AndKeepsThatView() => UiThread.Run(() =>
    {
        var model = new Model();
        var page  = PageOf(() => model);
        var views = Views(typeof(ModelView));

        Assert.IsNull(views.Shown(page), "nothing is made before the page is shown");
        var view = views.ViewOf(page);

        Assert.AreSame(model, ((ModelView)view).Model);
        Assert.AreSame(model, ViewRegistry.ModelOf(view), "the view says what it draws, for a drop or a key that reaches it");
        Assert.AreSame(view, views.ViewOf(page), "a tab shown again, or moved to another pane, keeps its view");
        Assert.AreSame(view, views.Shown(page));
    });

    [TestMethod]
    public void AClosedPage_LetsGoOfItsView_AndDisposesIt() => UiThread.Run(() =>
    {
        var page  = PageOf(() => new Model());
        var views = Views(typeof(ModelView));
        var view  = (ModelView)views.ViewOf(page);

        views.Release(page);

        Assert.AreEqual(1, view.Disposed);
        Assert.IsNull(views.Shown(page));
    });

    [TestMethod]
    public void AThrowingViewModelFactory_IsDrawnAsTheFailure_AndNotRunAgain() => UiThread.Run(() =>
    {
        var attempts = 0;
        var page = PageOf(() =>
        {
            attempts++;
            throw new InvalidOperationException("view-model blew up");
        });
        var views = Views(typeof(ModelView));

        var shown = views.ViewOf(page);
        page.GetOrCreateViewModel();

        var failure = (FailureView)shown;
        Assert.AreSame(page, failure.Page);
        StringAssert.Contains(failure.Error?.Message, "view-model blew up");
        // Tab activation asks on every switch; a factory that has proven it throws would redo its side effects each time.
        Assert.AreEqual(1, attempts);
        Assert.AreSame(failure.Error, page.LoadException);
    });

    [TestMethod]
    public void AViewThatCannotBeMade_IsDrawnAsTheFailure() => UiThread.Run(() =>
    {
        var page  = PageOf(() => new Model());
        var views = Views(typeof(FailingView));

        var failure = (FailureView)views.ViewOf(page);

        StringAssert.Contains(failure.Error?.Message, "view ctor blew up");
        Assert.IsNotNull(page.ViewModel, "the page itself was made; only its view failed");
    });

    [TestMethod]
    public void AModelNoViewDraws_IsDrawnAsTheFailure_NamingIt() => UiThread.Run(() =>
    {
        var page  = PageOf(() => new Model());
        var views = new PageViews(new ViewRegistry(_ => null), (p, error) => new FailureView(p, error));

        var failure = (FailureView)views.ViewOf(page);

        StringAssert.Contains(failure.Error?.Message, nameof(Model));
    });
}
