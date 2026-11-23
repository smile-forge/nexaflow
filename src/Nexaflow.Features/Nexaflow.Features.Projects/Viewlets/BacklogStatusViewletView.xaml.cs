using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.Projects.Model;
using Nexaflow.Features.Projects.ViewModels;
using Nexaflow.Visuals.Common.Theming;
using Nexaflow.Visuals.Common.Viewing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Nexaflow.Features.Projects.Viewlets;

[ViewFor(typeof(BacklogStatusViewletViewModel))]
public partial class BacklogStatusViewletView : UserControl
{
    /// <summary>One status row in the breakdown, its swatch resolved to the theme's brush.</summary>
    public sealed record StatusChip(string Label, Brush Brush, int Count);

    private readonly BacklogStatusViewletViewModel _vm;

    public BacklogStatusViewletView(BacklogStatusViewletViewModel vm)
    {
        InitializeComponent();
        _vm = vm;

        var chips = vm.Tallies.Select(tally => new StatusChip(tally.Label, SwatchPalette.Resolve(tally.SwatchKey), tally.Count)).ToList();
        ChipList.ItemsSource = chips;
        EmptyText.Visibility = chips.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OpenProject_Click(object sender, RoutedEventArgs e) => _vm.OpenProject();
}
