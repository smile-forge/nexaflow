using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace Nexaflow.Core.Controls;

public partial class WorkContextsConfigControl : UserControl, ICustomConfigApply
{
    private ObservableCollection<WorkContext>? _editContexts;
    private WorkContextsConfig?               _config;

    public WorkContextsConfigControl()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is WorkContextsConfig cfg)
            {
                _config       = cfg;
                _editContexts = new ObservableCollection<WorkContext>(
                    cfg.Contexts.Select(c => new WorkContext
                    {
                        Name  = c.Name,
                        Color = c.Color,
                        Icon  = c.Icon
                    }));
                ContextItems.ItemsSource = _editContexts;
            }
        };
    }

    public void Apply()
    {
        if (_config is null || _editContexts is null) return;

        _config.Contexts = [.. _editContexts];

        // Ensure at least one context survives
        if (_config.Contexts.Count == 0)
            _config.Contexts.Add(new WorkContext());

        // Re-initialize the live manager from the saved config
        WorkContextManager.Instance.Initialize(_config);
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        _editContexts?.Add(new WorkContext { Name = "New Context" });
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (_editContexts is null || _editContexts.Count <= 1) return;
        if (sender is Button { Tag: WorkContext ctx })
            _editContexts.Remove(ctx);
    }
}
