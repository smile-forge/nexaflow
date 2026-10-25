namespace Nexaflow.IO.Common.Terminal;

/// <summary>
/// An event that holds what is raised before anyone listens and hands it, in order, to the first handler to subscribe —
/// what lets a transport start talking the moment it starts without its consumer, subscribing a moment later, missing
/// the shell's first words. Raising and subscribing are serialized, so handlers see values one at a time and in the
/// order they were raised; a handler is called under that serialization, so it must not block.
/// </summary>
/// <param name="keep">Makes a value safe to hold past the call that raised it — a copy of a buffer the raiser reuses.
/// Values raised while someone listens are passed as they are.</param>
public sealed class HeldEvent<T>(Func<T, T>? keep = null)
{
    private readonly Lock _gate = new();
    private Action<T>? _handlers;
    private List<T>? _held;

    public void Add(Action<T>? handler)
    {
        if (handler is null) return;
        lock (_gate)
        {
            _handlers += handler;
            if (_held is not { } held) return;
            _held = null;
            foreach (var value in held) handler(value);
        }
    }

    public void Remove(Action<T>? handler)
    {
        lock (_gate) _handlers -= handler;
    }

    /// <summary>Raises <paramref name="value"/> to the handlers, or holds it until one subscribes.</summary>
    public void Raise(T value)
    {
        lock (_gate)
        {
            if (_handlers is { } handlers) handlers(value);
            else (_held ??= []).Add(keep is null ? value : keep(value));
        }
    }
}
