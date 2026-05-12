namespace Nexaflow.Features.Common;

/// <summary>
/// Singleton registry of all feature tab factories.  Features call
/// <see cref="Register"/> at startup; the shell calls <see cref="CreateTab"/>
/// and subscribes to <see cref="TabOpenRequested"/> for cross-feature navigation.
/// </summary>
public sealed class FeatureManager
{
    public static FeatureManager Instance { get; } = new();

    private readonly Dictionary<string, ITabRegistration> _registrations
        = new(StringComparer.OrdinalIgnoreCase);

    private FeatureManager() { }

    // ── Registration ──────────────────────────────────────────────────────

    public void Register(ITabRegistration registration)
        => _registrations[registration.PageKind] = registration;

    public bool IsRegistered(string pageKind)
        => _registrations.ContainsKey(pageKind);

    public IEnumerable<string> RegisteredPageKinds => _registrations.Keys;

    // ── Tab creation ──────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="TabEntry"/> for <paramref name="pageKind"/>,
    /// or returns <c>null</c> if no registration exists for that kind.
    /// </summary>
    public TabEntry? CreateTab(string pageKind, Dictionary<string, string>? pageParams = null)
        => _registrations.TryGetValue(pageKind, out var reg)
            ? reg.CreateTab(pageParams)
            : null;

    // ── Query handlers ────────────────────────────────────────────────────

    private readonly List<IQueryHandler> _queryHandlers = [];

    /// <summary>
    /// Registers a global <see cref="IQueryHandler"/> that will be scored on every shell input.
    /// Features call this at startup for handlers that operate independently of the active tab.
    /// </summary>
    public void RegisterQueryHandler(IQueryHandler handler) => _queryHandlers.Add(handler);

    /// <summary>All globally registered query handlers.</summary>
    public IReadOnlyList<IQueryHandler> QueryHandlers => _queryHandlers.AsReadOnly();

    // ── Cross-feature tab requests ────────────────────────────────────────

    /// <summary>
    /// Raised when a feature wants the shell to open a tab for another page kind
    /// (e.g. Projects asking to open a ProjectDetail tab).
    /// The shell subscribes to this and calls <see cref="CreateTab"/> + OpenTab.
    /// </summary>
    public event Action<string, Dictionary<string, string>?>? TabOpenRequested;

    /// <summary>
    /// Called by feature code (e.g. inside a view-model event handler) to ask the
    /// shell to open or focus a tab without having a direct reference to the shell.
    /// </summary>
    public void RequestTab(string pageKind, Dictionary<string, string>? pageParams = null)
        => TabOpenRequested?.Invoke(pageKind, pageParams);
}
