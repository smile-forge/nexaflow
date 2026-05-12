namespace Nexaflow.Providers.Common;

/// <summary>
/// Application-wide registry for the active <see cref="ILlmProvider"/>.
/// Call <see cref="Register"/> once at startup before any component reads
/// <see cref="Current"/>.
/// </summary>
public static class LlmProviderRegistry
{
    private static ILlmProvider? _current;

    /// <summary>The registered provider, or throws if none has been registered yet.</summary>
    public static ILlmProvider Current =>
        _current ?? throw new InvalidOperationException(
            "No LLM provider has been registered. " +
            "Call LlmProviderRegistry.Register() during application startup.");

    /// <summary>Registers <paramref name="provider"/> as the active provider.</summary>
    public static void Register(ILlmProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _current = provider;
    }
}
