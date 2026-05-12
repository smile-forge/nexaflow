namespace Nexaflow.Providers.Common;

/// <summary>
/// Application-wide registry for named <see cref="ILlmProvider"/> instances.
/// Call <see cref="Register"/> once per provider during startup.
/// The first registered provider automatically becomes both the Basic and Conversation provider.
/// </summary>
public static class LlmProviderRegistry
{
    private static readonly Dictionary<string, ILlmProvider> _providers
        = new(StringComparer.OrdinalIgnoreCase);

    private static string? _basicName;
    private static string? _conversationName;

    // ── Registration ──────────────────────────────────────────────────────

    /// <summary>
    /// Registers a named provider. The first registration becomes the default
    /// <see cref="Basic"/> and <see cref="Conversation"/> provider automatically.
    /// </summary>
    public static void Register(string name, ILlmProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _providers[name]  = provider;
        _basicName        ??= name;
        _conversationName ??= name;
    }

    // ── Query ─────────────────────────────────────────────────────────────

    /// <summary>All registered providers keyed by name.</summary>
    public static IReadOnlyDictionary<string, ILlmProvider> All => _providers;

    /// <summary>Names of all registered providers in registration order.</summary>
    public static IReadOnlyList<string> ProviderNames => [.. _providers.Keys];

    /// <summary>
    /// Returns all registered provider names. Intended for use with
    /// <c>[ListSource(typeof(LlmProviderRegistry), nameof(GetProviderNames))]</c>.
    /// </summary>
    public static IEnumerable<string> GetProviderNames() => _providers.Keys;

    /// <summary>
    /// Active provider for stateless one-shot queries.
    /// Falls back to the first registered provider if <see cref="SetBasicProvider"/> has not been called.
    /// </summary>
    public static ILlmProvider Basic =>
        (_basicName is not null && _providers.TryGetValue(_basicName, out var p)) ? p
            : _providers.Values.FirstOrDefault()
              ?? throw new InvalidOperationException(
                     "No LLM provider has been registered. Call Register() during application startup.");

    /// <summary>
    /// Active provider for conversation-aware chat.
    /// Falls back to the first registered provider if <see cref="SetConversationProvider"/> has not been called.
    /// </summary>
    public static ILlmProvider Conversation =>
        (_conversationName is not null && _providers.TryGetValue(_conversationName, out var p)) ? p
            : _providers.Values.FirstOrDefault()
              ?? throw new InvalidOperationException(
                     "No LLM provider has been registered. Call Register() during application startup.");

    // ── Selection ─────────────────────────────────────────────────────────

    /// <summary>
    /// Sets the active basic provider by name. No-op if <paramref name="name"/> is not registered,
    /// protecting against stale persisted values.
    /// </summary>
    public static void SetBasicProvider(string name)
    {
        if (_providers.ContainsKey(name)) _basicName = name;
    }

    /// <summary>
    /// Sets the active conversation provider by name. No-op if <paramref name="name"/> is not registered.
    /// </summary>
    public static void SetConversationProvider(string name)
    {
        if (_providers.ContainsKey(name)) _conversationName = name;
    }
}
