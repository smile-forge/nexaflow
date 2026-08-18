using Nexaflow.Core.Models;
using Nexaflow.Plugins;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Nexaflow.Core.Services;

/// <summary>
/// Resolves <c>[Subfeature]</c> plugins for a host feature. Sits beside <see cref="FeatureCatalog"/> and
/// borrows everything from it: the DLL-set-stamped discovery index answers "which assemblies contain a
/// plugin implementing <c>T</c>" from JSON with <b>no assembly loaded</b>, and
/// <see cref="FeatureManager.Instantiate"/> builds the instance through the same DI (and the same
/// per-workspace cache and eviction) a page registration gets.
///
/// <para>
/// This generalises the ad-hoc archive-backend registry: that one push-registered every handler at assembly
/// activation via a parameterless <c>Activator</c>, had no metadata, no enable/disable, no stable order, and
/// resolved conflicts by whichever DLL happened to load first. Here a plugin is <i>described</i> in the
/// index, ordered deterministically, and constructed only when its host actually reaches for it.
/// </para>
///
/// <para>
/// Deliberately absent: enablement. <c>DefaultEnabled</c> is metadata; the host feature owns the on/off map
/// in its own <c>IFeatureConfig</c> and simply doesn't touch the handles it has disabled. Keeping that policy
/// out of Core is the point — what "enabled" means differs per feature, and a shared implementation would
/// only get in the way of the next one.
/// </para>
/// </summary>
public sealed class SubfeatureCatalog
{
    private readonly FeatureCatalog _catalog;

    /// <summary>The process-wide instance, over the app's discovery index.</summary>
    public static SubfeatureCatalog Instance { get; } = new(FeatureCatalog.Instance);

    /// <summary>
    /// Builds a resolver over a specific <see cref="FeatureCatalog"/>. The catalog is injected rather than
    /// reached for so this is testable against a freshly-scanned index — the app's singleton is only
    /// initialised during startup, and a test that had to initialise it would drag the whole shell's
    /// activation side-effects in with it.
    /// </summary>
    public SubfeatureCatalog(FeatureCatalog catalog) => _catalog = catalog;

    /// <summary>
    /// Lazy handles for every indexed subfeature implementing <paramref name="contract"/>, ordered by
    /// <c>Order</c> then <c>Id</c>. <b>Index-only — loads no assembly.</b> The returned list is typed as
    /// <c>IReadOnlyList&lt;ISubfeatureHandle&lt;contract&gt;&gt;</c> so it can be injected directly.
    /// </summary>
    public IList Handles(Type contract, WorkspaceRuntime workspace)
    {
        var found = _catalog.Subfeatures(contract);

        var handleType = typeof(SubfeatureHandle<>).MakeGenericType(contract);
        var listType   = typeof(List<>).MakeGenericType(typeof(ISubfeatureHandle<>).MakeGenericType(contract));
        var list       = (IList)Activator.CreateInstance(listType)!;

        foreach (var (file, type, meta) in found)
            list.Add(Activator.CreateInstance(handleType, _catalog, file, type, meta, workspace)!);

        return list;
    }

    /// <summary>
    /// Eager form: every handle's <c>Value</c>, which loads and activates every owning assembly. Provided
    /// because a small fixed plugin set is sometimes genuinely simpler that way — but prefer
    /// <see cref="Handles"/>, which is what keeps a feature with a dozen plugins from paying for all of them
    /// to show a list.
    /// </summary>
    public IList Resolve(Type contract, WorkspaceRuntime workspace)
    {
        var handles  = Handles(contract, workspace);
        var listType = typeof(List<>).MakeGenericType(contract);
        var list     = (IList)Activator.CreateInstance(listType)!;

        foreach (var h in handles)
            list.Add(h!.GetType().GetProperty(nameof(ISubfeatureHandle<object>.Value))!.GetValue(h));

        return list;
    }

    /// <summary>
    /// The one indirection between an index entry and a live plugin. Metadata is answered from the index;
    /// <see cref="Value"/> is what finally loads the assembly, activates it, and builds the instance.
    /// </summary>
    private sealed class SubfeatureHandle<T>(
        FeatureCatalog catalog,
        string file,
        string typeName,
        FeatureCatalog.SubfeatureEntry meta,
        WorkspaceRuntime workspace) : ISubfeatureHandle<T> where T : class
    {
        private readonly object _lock = new();
        private T? _value;

        public string Owner          => meta.Owner;
        public string Id             => meta.Id;
        public string DisplayName    => meta.DisplayName;
        public string Description    => meta.Description;
        public int    Order          => meta.Order;
        public bool   DefaultEnabled => meta.DefaultEnabled;
        public string AssemblyFile   => file;

        public bool IsLoaded { get { lock (_lock) return _value is not null; } }

        public T Value
        {
            get
            {
                lock (_lock)
                {
                    if (_value is not null) return _value;

                    var type = catalog.ResolveType(file, typeName)
                        ?? throw new InvalidOperationException(
                            $"The subfeature '{meta.Owner}/{meta.Id}' is in the discovery index as "
                            + $"'{typeName}' in '{file}', but that type could not be resolved. The index is "
                            + "stale or the assembly failed to load — rebuild, or delete discovery/catalog.json.");

                    // Same DI, same per-workspace cache and eviction a page registration gets. Note Core does
                    // NOT hand a plugin IShellServices: capability is granted afterwards by the host feature,
                    // which is what lets the host withhold it.
                    _value = (T)FeatureManager.Instance.Instantiate(type, workspace);
                    return _value;
                }
            }
        }
    }
}
