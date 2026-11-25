namespace Nexaflow.Session.Services;

/// <summary>
/// One runtime's feature instances: every handler, registration and <see cref="Features.Common.FeatureServiceAttribute"/>
/// service the feature DI built for it, each built once and handed out again. Owned by its
/// <see cref="Models.WorkspaceRuntime"/>: <see cref="Reset"/> lets them go when the runtime's services are replaced, and
/// <see cref="Close"/> when the runtime ends, after which nothing more is built for it.
/// </summary>
public sealed class FeatureScope
{
    private readonly Dictionary<Type, object> _instances = [];

    /// <summary>The feature services being built, so one that needs itself is a defect rather than endless recursion.</summary>
    private readonly HashSet<Type> _servicesBuilding = [];

    /// <summary>Held while anything is built for the runtime, so a build that needs another of its services builds that once.
    /// A monitor, so the build of a service inside another build re-enters it.</summary>
    private readonly object _gate = new();

    private readonly string _owner;
    private bool _closed;

    /// <param name="owner">What the scope belongs to, named when something is asked of it after it closed.</param>
    public FeatureScope(string owner) => _owner = owner;

    /// <summary>The instance of <paramref name="type"/> built for this runtime, built by <paramref name="build"/> the first time.</summary>
    /// <exception cref="InvalidOperationException">The runtime has ended.</exception>
    internal object GetOrBuild(Type type, Func<object> build)
    {
        lock (_gate)
        {
            if (_closed)
                throw new InvalidOperationException(
                    $"{type.FullName} was asked of the workspace runtime '{_owner}' after it ended: nothing is built for a runtime "
                    + "that has let its services go, or it would hold them again with nothing left to release them.");
            if (_instances.TryGetValue(type, out var built)) return built;

            var instance = build();
            _instances[type] = instance;
            return instance;
        }
    }

    /// <summary>Marks <paramref name="service"/> as being built; false when it already is, so it needs itself.</summary>
    internal bool BeginService(Type service)
    {
        lock (_gate) return _servicesBuilding.Add(service);
    }

    internal void EndService(Type service)
    {
        lock (_gate) _servicesBuilding.Remove(service);
    }

    /// <summary>Lets go of every instance: the runtime's services were replaced, and what captured them is built afresh on the
    /// next request. A feature service is disposed — a store's pending writes written.</summary>
    internal void Reset() => Release(close: false);

    /// <summary>Lets go of every instance and builds nothing more: the runtime has ended.</summary>
    internal void Close() => Release(close: true);

    private void Release(bool close)
    {
        List<KeyValuePair<Type, object>> released;
        lock (_gate)
        {
            _closed |= close;
            released = [.. _instances];
            _instances.Clear();
        }

        foreach (var (type, instance) in released)
            if (type.IsDefined(typeof(Features.Common.FeatureServiceAttribute), inherit: false) && instance is IDisposable disposable)
                try { disposable.Dispose(); } catch { /* teardown must not break a workspace closing */ }
    }
}
