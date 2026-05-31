using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace Nexaflow.Core.Controls;

public partial class WorkContextsConfigControl : UserControl, ICustomConfigApply
{
    private ObservableCollection<WorkContextConfig>? _editContexts;
    private WorkContextsConfig?                      _config;

    // Maps each editable copy back to the original config so Apply can write the edited
    // Name/Color/Icon onto the original without disturbing live runtime contexts.
    private readonly Dictionary<WorkContextConfig, WorkContextConfig> _editToOriginal = [];

    // Maps an editable copy that represents an unsaved CLONE to the source config it should
    // be cloned from at Apply time (copying its AiConfig + provider configs).
    private readonly Dictionary<WorkContextConfig, WorkContextConfig> _editCloneSource = [];

    public WorkContextsConfigControl()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is WorkContextsConfig cfg)
            {
                _config = cfg;
                _editToOriginal.Clear();
                _editCloneSource.Clear();
                _editContexts = [];
                foreach (var c in cfg.Contexts)
                {
                    var edit = new WorkContextConfig { Name = c.Name, Color = c.Color, Icon = c.Icon };
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

        var mgr       = WorkContextManager.Instance;
        var survivors = new HashSet<WorkContextConfig>();
        var ordered   = new List<WorkContextConfig>();

        foreach (var edit in _editContexts)
        {
            if (_editToOriginal.TryGetValue(edit, out var original))
            {
                original.Name  = edit.Name;
                original.Color = edit.Color;
                original.Icon  = edit.Icon;
                survivors.Add(original);
                ordered.Add(original);
            }
            else if (_editCloneSource.TryGetValue(edit, out var sourceConfig))
            {
                var created = mgr.CloneConfig(sourceConfig, edit.Name);
                created.Color = edit.Color;
                created.Icon  = edit.Icon;
                survivors.Add(created);
                ordered.Add(created);
            }
            else
            {
                var created = mgr.AddConfig(edit.Name);
                created.Color = edit.Color;
                created.Icon  = edit.Icon;
                survivors.Add(created);
                ordered.Add(created);
            }
        }

        foreach (var cfg in mgr.Configs.ToList())
            if (!survivors.Contains(cfg))
                mgr.RemoveConfig(cfg);

        _config.Contexts = ordered.Count > 0 ? ordered : [.. mgr.Configs];
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        var (icon, color) = WorkContextStyle.Random();
        _editContexts?.Add(new WorkContextConfig { Name = "New Context", Color = color, Icon = icon });
    }

    private void OnCloneClick(object sender, RoutedEventArgs e)
    {
        if (_editContexts is null) return;
        if (sender is not Button { Tag: WorkContextConfig edit }) return;

        var source = _editToOriginal.TryGetValue(edit, out var original) ? original
                   : _editCloneSource.TryGetValue(edit, out var src)     ? src
                   : null;
        if (source is null) return;

        var (icon, color) = WorkContextStyle.Random();
        var clone = new WorkContextConfig { Name = edit.Name + " copy", Color = color, Icon = icon };
        _editCloneSource[clone] = source;

        _editContexts.Insert(_editContexts.IndexOf(edit) + 1, clone);
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (_editContexts is null || _editContexts.Count <= 1) return;
        if (sender is Button { Tag: WorkContextConfig cfg })
            _editContexts.Remove(cfg);
    }
}
