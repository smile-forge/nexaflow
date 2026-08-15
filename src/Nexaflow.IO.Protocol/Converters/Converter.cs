using Nexaflow.IO.Protocol.Values;

namespace Nexaflow.IO.Protocol.Converters;

/// <summary>The value kinds a converter accepts or produces. Declared per converter so a type error is a
/// <i>validate-time</i> message — "step 2, field 3: sha256 expects Bytes, got Int" — rather than something
/// discovered when bytes are already on the wire.</summary>
[Flags]
public enum ValueKinds
{
    None = 0,
    Bytes = 1 << 0, Int = 1 << 1, Number = 1 << 2, Bool = 1 << 3, Text = 1 << 4,
    Instant = 1 << 5, Duration = 1 << 6, List = 1 << 7, Record = 1 << 8, Null = 1 << 9,

    Numeric = Int | Number | Bool,
    Any = Bytes | Int | Number | Bool | Text | Instant | Duration | List | Record | Null,
}

/// <summary>
/// What a converter is <i>for</i>, which decides how decode treats it.
///
/// <para>
/// The distinction is borrowed from a protocol framework that does bidirectional generation, and it is the
/// observation that halves the problem: <b>a derived value is never inverted — it is re-derived and
/// compared.</b> A length, a count, a checksum, an offset and a back-reference all have a value the encoder
/// computes and the decoder can recompute; none of them ever needs an inverse. Only genuine encodings do.
/// </para>
///
/// <para>
/// It also turns a decoded checksum from a field we read and ignore into a validation. Parser generators
/// routinely read a CRC field and compute nothing, because for parsing there is no reason to — which is
/// exactly the gap that lets a corrupted capture decode cleanly.
/// </para>
/// </summary>
public enum ConverterRole
{
    /// <summary>
    /// A reversible encoding, verified by the round-trip laws over a declared domain: hex, base-128
    /// digits, minimal-width integers, fixed-point. Must declare a mutual inverse.
    /// </summary>
    Bijection,

    /// <summary>
    /// A computation with no inverse and no need of one — a digest, a checksum, a length, a count. On
    /// encode it is computed; on decode it is <b>recomputed and compared</b>, and disagreement is a decode
    /// failure rather than something quietly carried.
    /// </summary>
    Derivation,

    /// <summary>
    /// A lossy or narrowing operation that is neither: shifting, slicing, clamping. Legal in an expression,
    /// never legal as the transform on a <c>carries</c> edge, because a value that cannot survive the round
    /// trip cannot define one.
    /// </summary>
    Lossy,
}

/// <summary>
/// One member of the closed converter set.
///
/// <para>
/// The set being closed is a design commitment, not an oversight: extending it is a deliberate code change
/// with a test, which is what stops "the model needed one more transform" from becoming an unbounded
/// surface. Every converter is <b>pure</b> — a deterministic function of its arguments — so a dry run
/// genuinely predicts what a live run will emit. Where the wire needs ambient information (an NTP era, for
/// instance) it is passed as an explicit argument rather than read from a clock.
/// </para>
/// </summary>
/// <param name="Name">The name a document writes.</param>
/// <param name="Accepts">Kinds the piped-in source value may have.</param>
/// <param name="Produces">Kind of the result.</param>
/// <param name="Inverse">The converter that undoes this one, if any — required for use in <c>via</c>.</param>
/// <param name="Apply">The implementation. First argument is the pipeline source.</param>
/// <param name="Summary">One line, surfaced in validator errors and to the model.</param>
public sealed record Converter(
    string Name,
    ValueKinds Accepts,
    ValueKinds Produces,
    string? Inverse,
    Func<ProtoValue, IReadOnlyList<ProtoValue>, ProtoValue> Apply,
    string Summary)
{
    /// <summary>
    /// A converter that is part of the declared closed set but not implemented in this build. Kept in the
    /// table on purpose: a document naming it gets "declared but not yet implemented", which is a true and
    /// actionable statement, instead of "unknown converter", which would send the author looking for a typo.
    /// </summary>
    public static Converter NotYet(string name, ValueKinds accepts, ValueKinds produces, string summary)
        => new(name, accepts, produces, null,
               (_, _) => throw new ProtoTypeException(
                   $"converter '{name}' is part of the closed set but is not implemented in this build"),
               summary);

    /// <summary>
    /// One argument a converter takes besides the value it works on: what it is called, and what kind of
    /// value may go there.
    /// </summary>
    /// <remarks>
    /// Both halves are needed and for a while only the first was. A name alone checks that <c>repeat</c>
    /// takes a <c>count</c> and never that the count is a number — which is the check reading as safety
    /// while the failure it was meant to catch still happens, one layer further in.
    /// </remarks>
    public readonly record struct Parameter(string Name, ValueKinds Kind)
    {
        public override string ToString() => $"{Name}: {Kind}";
    }

    /// <summary>
    /// What this takes besides the value it converts, in order, by name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The names are how a graph says which edge feeds which argument — <c>repeat</c> takes a
    /// <c>count</c>, <c>fit</c> a <c>width</c> and a <c>fill</c>. A description names one on the edge and
    /// the value it carries lands at that position, so an argument may be a constant, a field read a
    /// moment ago, or the answer to another calculation. It used to be a literal written beside the
    /// converter's name, which made a computed argument inexpressible and put a second channel beside the
    /// edges for something the edges already do.
    /// </para>
    /// <para>
    /// Empty means the converter takes the value and nothing else, and an edge naming a parameter of one
    /// is refused rather than ignored.
    /// </para>
    /// </remarks>
    public IReadOnlyList<Parameter> Parameters { get; init; } = [];

    public bool IsImplemented => !ReferenceEquals(Apply, null) && Summary.Length > 0;

    /// <summary>
    /// What decode does with this converter. Derived from the declaration rather than stored, so the two
    /// can never disagree: a mutual inverse means an encoding to reverse, its absence means a value to
    /// recompute and check.
    /// </summary>
    public ConverterRole Role => Inverse is not null ? ConverterRole.Bijection
                               : Produces is ValueKinds.Int or ValueKinds.Bytes ? ConverterRole.Derivation
                               : ConverterRole.Lossy;
}
