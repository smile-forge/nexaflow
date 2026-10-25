using System.Diagnostics.CodeAnalysis;

namespace Nexaflow.IO.Common.Locations;

/// <summary>
/// The location backends this process knows, one per <see cref="ILocationProvider.Kind"/>. The app fills
/// <see cref="Instance"/> from every <c>Nexaflow.IO.Location.*</c> assembly as it registers archive handlers, and a
/// runtime's session pool reaches a connection through the provider registered for its kind.
/// </summary>
public sealed class LocationProviderRegistry
{
    /// <summary>The process's backends.</summary>
    public static LocationProviderRegistry Instance { get; } = new();

    private ILocationProvider[] _providers = [];

    /// <summary>Every registered backend, in the order they arrived.</summary>
    public IReadOnlyList<ILocationProvider> Providers => Volatile.Read(ref _providers);

    /// <summary>Raised when a backend arrives. May fire on any thread.</summary>
    public event Action? Changed;

    /// <summary>Adds <paramref name="provider"/> for its kind. The first backend for a kind keeps it: false when one
    /// already reaches that kind.</summary>
    public bool Register(ILocationProvider provider)
    {
        if (string.IsNullOrWhiteSpace(provider.Kind))
            throw new ArgumentException("A location backend names the kind it reaches.", nameof(provider));

        while (true)
        {
            var current = Volatile.Read(ref _providers);
            if (current.Any(p => string.Equals(p.Kind, provider.Kind, StringComparison.OrdinalIgnoreCase))) return false;
            if (ReferenceEquals(Interlocked.CompareExchange(ref _providers, [.. current, provider], current), current)) break;
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>The backend reaching <paramref name="kind"/>, compared ignoring case.</summary>
    public bool TryGet(string kind, [NotNullWhen(true)] out ILocationProvider? provider)
    {
        provider = Providers.FirstOrDefault(p => string.Equals(p.Kind, kind, StringComparison.OrdinalIgnoreCase));
        return provider is not null;
    }
}
