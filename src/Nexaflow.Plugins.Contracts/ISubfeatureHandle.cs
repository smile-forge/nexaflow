namespace Nexaflow.Plugins;

/// <summary>
/// A discovered subfeature the host resolves <b>lazily</b>: the metadata is available immediately from the
/// discovery index, and the assembly is loaded, activated and the instance built only when
/// <see cref="Value"/> is first touched.
///
/// <para>
/// This is the type a host feature should ask for. A constructor parameter of
/// <c>IReadOnlyList&lt;ISubfeatureHandle&lt;TContract&gt;&gt;</c> is satisfied by the feature DI with every
/// indexed plugin implementing <c>TContract</c>, ordered by <see cref="Order"/> then <see cref="Id"/>,
/// <b>with no assembly loaded</b>. Asking instead for <c>IReadOnlyList&lt;TContract&gt;</c> is also
/// supported but eager — it loads every one, which defeats the point when a feature has a dozen plugins and
/// the user has enabled two.
/// </para>
///
/// <para>
/// The handle deliberately exposes no capability. Core builds the instance through the same feature DI a
/// page registration gets, and never hands a plugin <c>IShellServices</c>: capability is granted by the
/// <i>host feature</i> afterwards (an <c>Attach(host)</c> call), so the host can withhold it. The framework
/// owns discovery, laziness, ordering and metadata — nothing more.
/// </para>
/// </summary>
/// <typeparam name="T">The contract the host defines and the plugin implements.</typeparam>
public interface ISubfeatureHandle<out T> where T : class
{
    /// <summary>Host feature slug from <see cref="SubfeatureAttribute.Owner"/>.</summary>
    string Owner { get; }

    /// <summary>Plugin id from <see cref="SubfeatureAttribute.Id"/>, unique within <see cref="Owner"/>.</summary>
    string Id { get; }

    /// <summary>Display name, falling back to <see cref="Id"/> when the attribute left it empty.</summary>
    string DisplayName { get; }

    /// <summary>One-line description for the user and the AI.</summary>
    string Description { get; }

    /// <summary>Ordering hint; the list is already sorted by it.</summary>
    int Order { get; }

    /// <summary>The attribute's default; the host's config decides the actual enabled state.</summary>
    bool DefaultEnabled { get; }

    /// <summary>The assembly file this plugin ships in — for diagnostics and the "what is loaded" view.</summary>
    string AssemblyFile { get; }

    /// <summary>
    /// True once <see cref="Value"/> has been resolved. Lets a host report which plugins actually cost
    /// anything this session, and lets a test assert that laziness held.
    /// </summary>
    bool IsLoaded { get; }

    /// <summary>
    /// Loads + activates the owning assembly and builds the instance through the feature DI, caching it.
    /// Throws if the type cannot be resolved or its constructor cannot be satisfied — a plugin that
    /// silently vanished is the failure mode this whole framework exists to prevent.
    /// </summary>
    T Value { get; }
}
