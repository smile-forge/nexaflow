using Nexaflow.Interaction;

namespace Nexaflow.Tests.Visuals.Dropping;

/// <summary>A clipboard of a test's own, so a test that copies or pastes never reaches into the machine's. Answers at once.</summary>
internal sealed class MemoryClipboard : IShellClipboard
{
    private static readonly string[] Announced = [.. DragKinds.Shared, DragKinds.FilesCut];

    /// <summary>What it holds, as it was written; null when it holds nothing.</summary>
    public IDragPayload? Held { get; private set; }

    public IReadOnlySet<string> Kinds => Held is { } held ? Announced.Where(held.Contains).ToHashSet() : new HashSet<string>();

    public event EventHandler? KindsChanged;

    /// <summary>Holds <paramref name="payload"/> as though something had been copied, here or elsewhere.</summary>
    public void Hold(IDragPayload? payload)
    {
        Held = payload;
        KindsChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task<IDragPayload> ReadAsync(IReadOnlyCollection<string> kinds, CancellationToken ct = default)
        => Task.FromResult<IDragPayload>(Held?.Take(kinds, DropStaging.Default) ?? ComposedPayload.Empty);

    public Task<bool> WriteAsync(IDragPayload payload, CancellationToken ct = default)
    {
        Hold(payload);
        return Task.FromResult(true);
    }

    public Task<bool> ClearAsync(CancellationToken ct = default)
    {
        Hold(null);
        return Task.FromResult(true);
    }
}
