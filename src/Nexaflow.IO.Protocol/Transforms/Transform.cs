using Nexaflow.IO.Protocol.Converters;
using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Values;

namespace Nexaflow.IO.Protocol.Transforms;

/// <summary>
/// A value transform written in a <b>document</b> rather than in the engine.
///
/// <para>
/// This is where a protocol's arithmetic belongs. A first-arc merge, a base-128 digit regrouping, a
/// minimal-width integer — each is one encoding family's rule, and every one of them that lives in engine
/// code is a protocol specific with a general-sounding name. Moving them into documents is what makes the
/// no-protocol-specifics rule structural rather than a name scan: what remains in the engine is arithmetic,
/// bit operations, byte-sequence operations and bounded iteration, none of which can embody a protocol
/// even in principle.
/// </para>
///
/// <para><b>Four properties, all enforced:</b></para>
/// <list type="bullet">
///   <item><description><b>Pure</b> — no clock, no I/O, no ambient anything. Values in, values out.</description></item>
///   <item><description><b>Contained</b> — the body may name only its own parameters. No positional
///   vocabulary, no captures, no graph access. If a transform needs to know <i>where</i> it is, that is the
///   graph's job, and letting it ask directly is how two mechanisms end up able to express one thing.</description></item>
///   <item><description><b>Total</b> — the language has no recursion, no first-class functions and no
///   unbounded loop, so termination is syntactic rather than something to prove.</description></item>
///   <item><description><b>Invertible, over a stated domain</b> — see <see cref="Domain"/>. This is the
///   property a closed converter set could not express at all, and the reason three of the engine's
///   declared inverses were false with nothing able to notice.</description></item>
/// </list>
/// </summary>
public sealed record Transform
{
    /// <summary>The name a document refers to it by.</summary>
    public required string Name { get; init; }

    /// <summary>The value being transformed. Named, so both directions can refer to it.</summary>
    public string Subject { get; init; } = "value";

    /// <summary>Configuration visible to both directions — a group order, a zero rule, a polynomial.
    /// These are what stop a transform being one protocol's mechanism.</summary>
    public IReadOnlyList<string> Parameters { get; init; } = [];

    /// <summary>Subject + parameters → the encoded value.</summary>
    public required Expr Forward { get; init; }

    /// <summary>
    /// The encoded value + parameters → the subject. Null means this is a <b>derivation</b>, not a
    /// bijection: a length, a count, a checksum. Those are never inverted — on decode they are recomputed
    /// and compared, which is both cheaper and stricter than inverting them would be.
    /// </summary>
    public Expr? Inverse { get; init; }

    /// <summary>
    /// A predicate over subject and parameters saying where the round-trip law actually holds.
    ///
    /// <para>
    /// Not optional in spirit, and the reason the whole construct exists. A first-arc merge is
    /// <c>40x + y</c>, which is <b>not injective</b> over all pairs — <c>(1,40)</c> and <c>(2,0)</c> both
    /// give 80 — so it round-trips only where the first arc is at most 2 and the second is bounded unless
    /// it is. Without somewhere to write that down, a round-trip property test is testing a false
    /// proposition and will either pass by luck or fail meaninglessly.
    /// </para>
    /// </summary>
    public Expr? Domain { get; init; }

    /// <summary>
    /// True when <c>forward(inverse(y)) == y</c> byte-for-byte. False marks a quotient — the transform
    /// accepts several encodings of one value and emits one of them — and then byte-exact re-encode is
    /// claimed only up to the canonical form. Saying nothing is how a byte-exact claim becomes quietly false.
    /// </summary>
    public bool Canonical { get; init; } = true;

    public string Summary { get; init; } = "";

    /// <summary>Bijection when it declares an inverse, derivation otherwise — the same rule the engine's
    /// own converters follow, so a document-authored transform is indistinguishable downstream.</summary>
    public ConverterRole Role => Inverse is not null ? ConverterRole.Bijection : ConverterRole.Derivation;

    private IReadOnlySet<string> Bindable => new HashSet<string>(Parameters.Append(Subject), StringComparer.Ordinal);

    /// <summary>
    /// Static checks, run before a transform may be used. Everything here is decidable — which is the
    /// point of a total language, and what makes an AI-authored transform reviewable rather than merely
    /// runnable.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        List<string> issues = [];
        var allowed = Bindable;

        Contained(Forward, "forward", allowed, issues);
        if (Inverse is not null) Contained(Inverse, "inverse", allowed, issues);
        if (Domain is not null) Contained(Domain, "domain", allowed, issues);

        if (Inverse is null && Canonical is false)
            issues.Add($"transform '{Name}': a derivation has no inverse, so canonicality is meaningless");

        if (Inverse is not null && Domain is null)
            issues.Add($"transform '{Name}': a bijection must declare a domain. Most round-trip laws are "
                     + "false over all inputs and true over the values a protocol actually uses; without a "
                     + "domain the law cannot be stated, let alone tested.");

        return issues;
    }

    private void Contained(Expr body, string which, IReadOnlySet<string> allowed, List<string> issues)
    {
        var free = body.FreeRootNames().Where(n => !allowed.Contains(n)).OrderBy(n => n).ToList();
        if (free.Count == 0) return;

        issues.Add(
            $"transform '{Name}' ({which}) references {string.Join(", ", free.Select(n => $"'{n}'"))}, which "
          + $"is not among its parameters ({string.Join(", ", allowed.OrderBy(n => n))}). A transform sees "
          + "values, never the graph — if it needs to know where it is, that is the graph's job.");
    }

    /// <summary>Applies the forward direction.</summary>
    public ProtoValue Apply(ProtoValue subject, IReadOnlyDictionary<string, ProtoValue>? parameters = null,
                            Evaluator? evaluator = null)
        => (evaluator ?? new Evaluator()).Eval(Forward, ScopeFor(subject, parameters));

    /// <summary>Applies the inverse. Throws for a derivation, which has none by construction.</summary>
    public ProtoValue Undo(ProtoValue encoded, IReadOnlyDictionary<string, ProtoValue>? parameters = null,
                           Evaluator? evaluator = null)
        => Inverse is null
            ? throw new ProtoTypeException(
                $"transform '{Name}' is a derivation, not a bijection — on decode it is recomputed and "
              + "compared rather than inverted")
            : (evaluator ?? new Evaluator()).Eval(Inverse, ScopeFor(encoded, parameters));

    /// <summary>True if the round-trip law is claimed for this input. No domain means "everywhere".</summary>
    public bool InDomain(ProtoValue subject, IReadOnlyDictionary<string, ProtoValue>? parameters = null,
                         Evaluator? evaluator = null)
        => Domain is null || (evaluator ?? new Evaluator()).Eval(Domain, ScopeFor(subject, parameters)).AsBool();

    private EvalScope ScopeFor(ProtoValue subject, IReadOnlyDictionary<string, ProtoValue>? parameters)
    {
        var scope = new EvalScope().Set(Subject, subject);
        if (parameters is not null)
            foreach (var (k, v) in parameters) scope.Set(k, v);
        return scope;
    }
}
