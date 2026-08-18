namespace Nexaflow.IO.Protocol.Resolution;

/// <summary>
/// Why resolution stopped.
///
/// <para>
/// Five kinds, not one. The first design reported everything as "a cycle naming the unresolved set", and
/// in four of ten stress protocols the dependency graph was provably acyclic — one had <b>no</b> derivation
/// edges at all. The diagnostic pointed authors at a loop that did not exist. Four of these five are
/// decidable at document-validation time, before any bytes are produced.
/// </para>
/// </summary>
public enum ResolutionFailure
{
    /// <summary>A genuine strongly-connected component in the facet-dependency graph. Decidable statically.</summary>
    Cycle,

    /// <summary>The dependencies are schedulable, but the facet order a pattern declared forbids it —
    /// typically a pattern claiming extent is settleable before the value it is computed from.
    /// Decidable statically.</summary>
    StageOrder,

    /// <summary>A dependency edge exists but cannot yield the facet asked of it — asking a node for a value
    /// it has no way to compute. Decidable statically.</summary>
    Unsatisfiable,

    /// <summary>A repetition, choice or optional never materialised, so the node set is incomplete.
    /// <b>This is the one that used to report success.</b></summary>
    Unrealised,

    /// <summary>Progress was retracted — settling a facet invalidated one already settled. Only reachable
    /// at run time, and it names the decision responsible.</summary>
    NonMonotone,
}

/// <summary>A resolution failure, with enough detail to act on.</summary>
/// <param name="Failure">Which kind.</param>
/// <param name="Blocked">The facets that never settled.</param>
/// <param name="Explanation">Author-facing text naming the specific cause.</param>
public sealed record ResolutionDiagnostic(
    ResolutionFailure Failure,
    IReadOnlyList<FacetRef> Blocked,
    string Explanation)
{
    public override string ToString()
        => $"{Failure}: {Explanation}"
         + (Blocked.Count > 0 ? $"\n  blocked: {string.Join(", ", Blocked)}" : "");
}

/// <summary>Raised when a document cannot be resolved. Carries the diagnostic rather than a bare message,
/// so a caller can distinguish an authoring error from a runtime one.</summary>
public sealed class ResolutionException(ResolutionDiagnostic diagnostic)
    : Exception(diagnostic.ToString())
{
    public ResolutionDiagnostic Diagnostic { get; } = diagnostic;
}
