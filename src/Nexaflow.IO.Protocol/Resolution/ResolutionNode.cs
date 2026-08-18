namespace Nexaflow.IO.Protocol.Resolution;

/// <summary>
/// One node the resolver schedules — a wire segment, a group, a reservation.
///
/// <para>
/// Deliberately an abstraction over "thing with facets" rather than anything protocol-shaped: the resolver
/// is exercised entirely with synthetic nodes and no pattern library, no document and no socket, which is
/// what lets the hard cases (a length covering a later region, a digest over a span containing itself, a
/// repetition that never expanded) be tested in isolation.
/// </para>
/// </summary>
public sealed class ResolutionNode
{
    public required object Id { get; init; }

    /// <summary>
    /// Facets this node's facet needs settled first. <b>Declared per node</b>, because the order differs
    /// per shape — a fixed-width scalar treats extent as axiomatic, while a minimal-width or escaping
    /// encoding computes extent <i>from</i> value.
    /// </summary>
    public required Func<Facet, IReadOnlyList<FacetRef>> DependenciesFor { get; init; }

    /// <summary>
    /// Settles one facet, given the settled values it depends on. May return newly-realised child nodes —
    /// which is how a repetition or a choice expands, and why <see cref="Facet.Realised"/> exists.
    /// </summary>
    public required Func<Facet, IReadOnlyDictionary<FacetRef, object?>, FacetResult> Settle { get; init; }

    /// <summary>Facets that do not apply to this node and are treated as settled on sight. A node that
    /// emits nothing has no extent to compute.</summary>
    public IReadOnlySet<Facet> NotApplicable { get; init; } = new HashSet<Facet>();

    public override string ToString() => Id.ToString() ?? "?";
}

/// <summary>The outcome of settling one facet.</summary>
/// <param name="Value">What it settled to.</param>
/// <param name="Realises">Nodes brought into existence by this step — repetition elements, chosen branches.</param>
public readonly record struct FacetResult(object? Value, IReadOnlyList<ResolutionNode>? Realises = null)
{
    public static FacetResult Of(object? value) => new(value);
    public static FacetResult Expanding(object? value, params ResolutionNode[] realises) => new(value, realises);
}
