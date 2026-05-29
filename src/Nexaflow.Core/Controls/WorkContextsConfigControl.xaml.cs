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

    // Maps each editable copy back to the original WorkContext so Apply can write the edited
    // Name/Color/Icon onto the original — preserving its AiConfig (and live runtime services)
    // instead of replacing it with a stripped copy.
    private readonly Dictionary<WorkContext, WorkContext> _editToOriginal = [];

    public WorkContextsConfigControl()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is WorkContextsConfig cfg)
            {
                _config = cfg;
                _editToOriginal.Clear();
                _editContexts = [];
                foreach (var c in cfg.Contexts)
                {
                    var edit = new WorkContext { Name = c.Name, Color = c.Color, Icon = c.Icon };
                    _editToOriginal[edit] = c;
                    _editContexts.Add(edit);
                }
                ContextItems.ItemsSource = _editContexts;
            }
        };
    }

    public void Apply()
    {
        if (_config is null || _editContexts is null) return;

        // This editor only manages the LIST of work contexts (app-wide). It must not rebuild or
        // reset existing contexts, touch their per-context AI config, or change the active one —
        // so we reconcile the live collection in place rather than re-initialising the manager.
        var mgr       = WorkContextManager.Instance;
        var survivors = new HashSet<WorkContext>();
        var ordered   = new List<WorkContext>();

        foreach (var edit in _editContexts)
        {
            if (_editToOriginal.TryGetValue(edit, out var original))
            {
                // Existing context — apply name/colour/icon edits, keep its AiConfig + services.
                original.Name  = edit.Name;
                original.Color = edit.Color;
                original.Icon  = edit.Icon;
                survivors.Add(original);
                ordered.Add(original);
            }
            else
            {
                // Newly added in the editor — create a fully-initialised context (does not activate it).
                var created = mgr.Create(edit.Name);
                created.Color = edit.Color;
                created.Icon  = edit.Icon;
                survivors.Add(created);
                ordered.Add(created);
            }
        }

        // Remove contexts the user deleted in the editor (Remove() keeps at least one).
        foreach (var ctx in mgr.Contexts.ToList())
            if (!survivors.Contains(ctx))
                mgr.Remove(ctx);

        // Persist the resulting set (order follows the editor); existing AiConfig is preserved.
        _config.Contexts = ordered.Count > 0 ? ordered : [.. mgr.Contexts];
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
