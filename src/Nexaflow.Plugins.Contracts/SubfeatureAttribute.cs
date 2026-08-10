namespace Nexaflow.Plugins;

/// <summary>
/// Marks a concrete type as a lazily-loadable <b>subfeature</b> of some host feature — a pluggable backend
/// that ships in its own assembly, implements a contract the host defines, and is discovered without the
/// host (or Core) naming it.
///
/// <para>
/// The attribute carries only <i>metadata</i>. It is read during the <c>FeatureCatalog</c> scan
/// <b>without instantiating the type</b> and recorded in the on-disk discovery index, so the host can list,
/// order and enable/disable its subfeatures while their assemblies are still unloaded. The instance is not
/// built until <see cref="ISubfeatureHandle{T}.Value"/> is touched.
/// </para>
///
/// <para>
/// Nothing here names a particular feature: the same machinery serves discovery probes, codec backends,
/// transports and anything else a feature wants to make pluggable.
/// </para>
/// </summary>
/// <param name="owner">
/// Host feature slug — the feature this plugin extends, e.g. <c>"network"</c>. Lower-case, stable; it is
/// what groups plugins in the host's settings UI, not a display string.
/// </param>
/// <param name="id">Unique within <paramref name="owner"/>, e.g. <c>"arp"</c>. Stable — it keys the
/// user's enable/disable choice and any persisted per-plugin settings, so renaming one resets them.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SubfeatureAttribute(string owner, string id) : Attribute
{
    /// <summary>Host feature slug, e.g. <c>"network"</c>.</summary>
    public string Owner { get; } = owner;

    /// <summary>Unique within <see cref="Owner"/>, e.g. <c>"arp"</c>.</summary>
    public string Id { get; } = id;

    /// <summary>Shown in the host's plugin list. Falls back to <see cref="Id"/> when empty.</summary>
    public string DisplayName { get; init; } = "";

    /// <summary>One line, written for the user <i>and</i> the AI — the host surfaces it in both.</summary>
    public string Description { get; init; } = "";

    /// <summary>
    /// False for costly, aggressive or experimental plugins, which stay off until the user opts in.
    /// This is only the <i>default</i>: enablement itself belongs to the host feature's config, not to the
    /// framework, because what "enabled" means differs per feature.
    /// </summary>
    public bool DefaultEnabled { get; init; } = true;

    /// <summary>
    /// Ordering hint within <see cref="Owner"/>, ascending. For Network this is the discovery layer number,
    /// so cheap link-layer probes run before expensive management-plane ones. Ties break on <see cref="Id"/>
    /// so the order is total and stable across runs — never dependent on <c>Directory.GetFiles</c> order,
    /// which is the flaw in the original archive-handler registry this generalises.
    /// </summary>
    public int Order { get; init; }
}
