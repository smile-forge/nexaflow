using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Localization;
using Nexaflow.Visuals.Common.Locate;

namespace Nexaflow.Core.Help;

public partial class HelpView : UserControl, IPageView
{
    private readonly HelpViewModel _vm;
    private DispatcherTimer? _hint;

    internal HelpView(HelpViewModel vm)
    {
        InitializeComponent();
        _vm = vm;

        // The document reads these as it renders, so they go in before the first Markdown arrives through the
        // DataContext below.
        Doc.ImageResolver = vm.ResolveImage;
        Doc.LinkDecorator = (link, url) => LocateLink.Decorate(link, url, Str.Get("Help.Locate.Tooltip"));
        Doc.LinkNavigate  = url => Locate(url) || vm.FollowLink(url);
        vm.FindInRendered = Doc.FindInRendered;
        vm.StepRendered   = Doc.StepSearch;
        vm.ClearRendered  = Doc.ClearSearch;

        // A newly shown page lays out after the call that showed it returns. Once it has, the search is re-applied and
        // a heading a link asked for is scrolled to — after the search, which moves to its own first match.
        vm.DocumentShown += () => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            vm.ReapplySearch();
            if (vm.TakePendingAnchor() is { } anchor) Doc.ScrollToAnchor(anchor);
        }));
        vm.AnchorRequested += anchor => Doc.ScrollToAnchor(anchor);

        DataContext = vm;
    }

    public IPageViewModel? ViewModel => _vm;

    public void Reinitialize(Dictionary<string, string> pageParams)
        => _vm.Navigate(pageParams.GetValueOrDefault("topic"), pageParams.GetValueOrDefault("query"), fromUser: false);

    /// <summary>
    /// A <c>locate:</c> link points at the screen rather than going anywhere: lasso what it names, in this pane's window,
    /// preferring the page beside it to help's own controls. When none of it is on screen — its page isn't open — say so,
    /// since a link that does nothing at all reads as broken.
    /// </summary>
    private bool Locate(string url)
    {
        if (!LocateLink.TryParse(url, out var ids)) return false;
        if (Window.GetWindow(this) is not { } window) return true;

        LocateTour.Start(window, ids, new LocateOptions { SearchLast = this })
                  .Completion.ContinueWith(tour =>
                  {
                      if (tour.Result is { Shown: 0, Cancelled: false }) ShowHint();
                  }, TaskScheduler.FromCurrentSynchronizationContext());
        return true;
    }

    private void ShowHint()
    {
        LocateHint.Visibility = Visibility.Visible;
        _hint ??= new DispatcherTimer(TimeSpan.FromSeconds(4), DispatcherPriority.Normal,
                                      (_, _) => { _hint!.Stop(); LocateHint.Visibility = Visibility.Collapsed; }, Dispatcher);
        _hint.Stop();
        _hint.Start();
    }

    // The keys a search box is expected to have: Enter for the next match (Shift+Enter the previous), Esc to clear.
    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                _vm.Step(back: (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
                e.Handled = true;
                break;
            case Key.Escape when _vm.HasSearchText:
                _vm.ClearSearchCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void OnOtherResultSelected(object sender, SelectionChangedEventArgs e)
    {
        if (OtherResults.SelectedItem is not HelpSearchResult result) return;
        OtherResults.SelectedItem = null;
        _vm.OpenResultCommand.Execute(result);
    }
}
