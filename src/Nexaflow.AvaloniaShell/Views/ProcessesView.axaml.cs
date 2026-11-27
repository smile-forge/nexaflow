using Avalonia.Controls;
using Nexaflow.Features.Processes.ViewModels;

namespace Nexaflow.AvaloniaShell.Views;

/// <summary>
/// The Processes page on Avalonia: the feature's own view-model, unchanged, drawn by a view of this app. The model samples while its
/// view is shown, as the feature's WPF view has it do.
/// </summary>
[AvaloniaViewFor(typeof(ProcessesViewModel))]
public partial class ProcessesView : UserControl
{
    /// <summary>Only for Avalonia's designer and loader; the page views make it over its model.</summary>
    public ProcessesView() => InitializeComponent();

    public ProcessesView(ProcessesViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        AttachedToVisualTree   += (_, _) => model.OnActivated();
        DetachedFromVisualTree += (_, _) => model.OnDeactivated();
    }
}
