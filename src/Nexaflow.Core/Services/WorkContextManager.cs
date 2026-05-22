using Nexaflow.Core.Models;
using System.Collections.ObjectModel;

namespace Nexaflow.Core.Services;

public sealed class WorkContextManager
{
    public static WorkContextManager Instance { get; } = new();

    public ObservableCollection<WorkContext> Contexts { get; } = [];

    private WorkContext _current = null!;
    public WorkContext Current
    {
        get => _current;
        set
        {
            if (value is null || !Contexts.Contains(value)) return;
            _current = value;
        }
    }

    private WorkContextManager() { }

    public void Initialize(WorkContextsConfig config)
    {
        Contexts.Clear();

        if (config.Contexts is { Count: > 0 } saved)
        {
            foreach (var ctx in saved)
                Contexts.Add(ctx);
        }
        else
        {
            Contexts.Add(new WorkContext());
        }

        _current = Contexts.FirstOrDefault(c => c.Name == config.CurrentName)
                   ?? Contexts[0];
    }

    public WorkContext Create(string name)
    {
        var ctx = new WorkContext { Name = name };
        Contexts.Add(ctx);
        return ctx;
    }

    public bool Remove(WorkContext ctx)
    {
        if (Contexts.Count <= 1) return false;
        var removed = Contexts.Remove(ctx);
        if (removed && _current == ctx)
            _current = Contexts[0];
        return removed;
    }

    public void PersistTo(WorkContextsConfig config)
    {
        config.Contexts    = [.. Contexts];
        config.CurrentName = _current?.Name ?? config.Contexts[0].Name;
    }
}
