using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Nexaflow.Features.Common;

namespace Nexaflow.Core.Help;

public partial class HelpView : UserControl, IPageView
{
    private readonly HelpViewModel _vm;

    internal HelpView(HelpViewModel vm)
    {
        InitializeComponent();
        _vm = vm;

        // The document reads these as it renders, so they go in before the first Markdown arrives through the
        // DataContext below.
        Doc.ImageResolver = vm.ResolveImage;
        Doc.LinkNavigate  = vm.FollowLink;
        vm.FindInRendered = Doc.FindInRendered;
        vm.StepRendered   = Doc.StepSearch;
        vm.ClearRendered  = Doc.ClearSearch;

        // A newly shown page lays out after the call that showed it returns; the search is re-applied once it has.
        vm.DocumentShown += () => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(vm.ReapplySearch));

        DataContext = vm;
    }

    public IPageViewModel? ViewModel => _vm;

    public void Reinitialize(Dictionary<string, string> pageParams)
        => _vm.Navigate(pageParams.GetValueOrDefault("topic"), pageParams.GetValueOrDefault("query"), fromUser: false);

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
