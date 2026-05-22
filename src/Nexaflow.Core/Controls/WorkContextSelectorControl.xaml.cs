using Nexaflow.Core.Models;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Nexaflow.Core.Controls;

public partial class WorkContextSelectorControl : UserControl
{
    public static readonly DependencyProperty CurrentWorkContextProperty =
        DependencyProperty.Register(nameof(CurrentWorkContext), typeof(WorkContext),
            typeof(WorkContextSelectorControl), new PropertyMetadata(null));

    public static readonly DependencyProperty WorkContextsProperty =
        DependencyProperty.Register(nameof(WorkContexts), typeof(ObservableCollection<WorkContext>),
            typeof(WorkContextSelectorControl), new PropertyMetadata(null));

    public static readonly DependencyProperty SelectWorkContextCommandProperty =
        DependencyProperty.Register(nameof(SelectWorkContextCommand), typeof(ICommand),
            typeof(WorkContextSelectorControl), new PropertyMetadata(null));

    public WorkContext? CurrentWorkContext
    {
        get => (WorkContext?)GetValue(CurrentWorkContextProperty);
        set => SetValue(CurrentWorkContextProperty, value);
    }

    public ObservableCollection<WorkContext>? WorkContexts
    {
        get => (ObservableCollection<WorkContext>?)GetValue(WorkContextsProperty);
        set => SetValue(WorkContextsProperty, value);
    }

    public ICommand? SelectWorkContextCommand
    {
        get => (ICommand?)GetValue(SelectWorkContextCommandProperty);
        set => SetValue(SelectWorkContextCommandProperty, value);
    }

    public WorkContextSelectorControl()
    {
        InitializeComponent();
    }

    private void OnTriggerClick(object sender, RoutedEventArgs e)
    {
        ContextPopup.IsOpen = true;
    }

    private void OnContextItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: WorkContext ctx })
        {
            SelectWorkContextCommand?.Execute(ctx);
            ContextPopup.IsOpen = false;
        }
    }
}
