using Nexaflow.Core.Models;
using Nexaflow.Features.Common;
using System;
using System.Collections;
using System.Collections.Generic;

namespace Nexaflow.Core.Services;

/// <summary>
/// A <b>live</b>, read-through view of the feature configs available to a workspace: the global config map
/// (which grows as feature assemblies are lazily activated) overlaid with the workspace's per-profile
/// scoped configs (materialized on demand). Handed to feature registrations and
/// <see cref="FileSystemFeatureRegistry"/> instead of a point-in-time snapshot — because with lazy feature
/// loading a config is registered only when its owning assembly activates, which can happen <i>after</i> the
/// view was created (e.g. while a registry discovers file actions across features). A snapshot would miss
/// those, so the dependent actions/viewlets would fail to construct.
/// </summary>
internal sealed class LiveFeatureConfigView : IReadOnlyDictionary<Type, IFeatureConfig>
{
    private readonly IReadOnlyDictionary<Type, IFeatureConfig> _global;   // FeatureManager._configs (live)
    private readonly Workspace? _workspace;

    public LiveFeatureConfigView(IReadOnlyDictionary<Type, IFeatureConfig> global, Workspace? workspace)
    {
        _global    = global;
        _workspace = workspace;
    }

    public bool TryGetValue(Type key, out IFeatureConfig value)
    {
        // Global first (cheap concurrent lookup); only scoped configs miss here, then materialize on demand.
        if (_global.TryGetValue(key, out value!)) return true;
        if (_workspace?.Profile.FindWorkspaceConfig(key) is { } scoped) { value = scoped; return true; }
        value = null!;
        return false;
    }

    public IFeatureConfig this[Type key]
        => TryGetValue(key, out var v) ? v : throw new KeyNotFoundException(key.FullName);

    public bool ContainsKey(Type key) => TryGetValue(key, out _);

    // Enumeration is rarely used on this view; build a current snapshot when asked.
    private Dictionary<Type, IFeatureConfig> Snapshot()
    {
        var d = new Dictionary<Type, IFeatureConfig>(_global);
        if (_workspace is not null)
            foreach (var c in _workspace.Profile.WorkspaceConfigs)
                d[c.GetType()] = c;
        return d;
    }

    public IEnumerable<Type> Keys => Snapshot().Keys;
    public IEnumerable<IFeatureConfig> Values => Snapshot().Values;
    public int Count => Snapshot().Count;
    public IEnumerator<KeyValuePair<Type, IFeatureConfig>> GetEnumerator() => Snapshot().GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
