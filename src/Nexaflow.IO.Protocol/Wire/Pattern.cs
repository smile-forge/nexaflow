using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Values;

namespace Nexaflow.IO.Protocol.Wire;

/// <summary>
/// Which end of a multi-group encoding carries the most significant part.
///
/// <para>
/// Named as a notion rather than after either family that exhibits it. Most-significant-first reads
/// <c>8f 65</c> as 2021; least-significant-first reads <c>8f 01</c> as 143. There is no defensible
/// default — omitting the parameter is how a general name comes to sit over one family's codec.
/// </para>
/// </summary>
public enum GroupOrder
{
    MostSignificantFirst,
    LeastSignificantFirst,
}

/// <summary>One named run of bits inside a <see cref="Pattern.Bits"/> group.</summary>
/// <param name="Name">Capture name for this run.</param>
/// <param name="Width">Bits, most-significant first within the group.</param>
public readonly record struct BitSlice(string Name, int Width);

/// <summary>
/// One packing of a <see cref="Pattern.Choice"/>.
/// </summary>
/// <param name="Name">What this packing is called. It becomes the choice's decoded value, so a later step
/// can branch on <i>which shape arrived</i> rather than re-testing the raw discriminator — the difference
/// between a structural branch and a coincidence that happens to agree with one.</param>
/// <param name="Key">The discriminator value selecting this arm, or null for the fallback arm.</param>
/// <param name="Fields">The fields this packing contributes, in order.</param>
public sealed record Arm(string Name, long? Key, IReadOnlyList<Field> Fields)
{
    public bool IsFallback => Key is null;
}

/// <summary>
/// A wire shape. These are <b>notions</b> — a fixed-width number, a run of bits, an opaque span, a region,
/// a choice between packings, a repetition — and never a protocol's mechanism. Anything that can only be
/// described by naming a protocol is not a pattern and belongs in a document, as a composition of these
/// plus a transform.
/// </summary>
public abstract record Pattern
{
    /// <summary>A fixed-width integer.</summary>
    /// <param name="Octets">Width in octets, 1..8.</param>
    /// <param name="BigEndian">Byte order. Never defaulted at the document level — which order is correct
    /// is a property of the protocol.</param>
    /// <param name="Signed">Two's-complement when true.</param>
    public sealed record Scalar(int Octets, bool BigEndian, bool Signed = false) : Pattern;

    /// <summary>
    /// Named bit runs packed most-significant-first. The widths must total a whole number of octets — a
    /// group that does not is a document error rather than something silently padded, because an
    /// accidentally byte-misaligned field reads plausible values from the wrong place.
    /// </summary>
    public sealed record Bits(IReadOnlyList<BitSlice> Slices) : Pattern
    {
        public int TotalBits => Slices.Sum(s => s.Width);
    }

    /// <summary>
    /// A span of octets carried without interpretation.
    ///
    /// <para>
    /// Its extent comes from exactly one of two places: <paramref name="Width"/>, fixed by the
    /// declaration, or <paramref name="Length"/>, recovered on decode from something already read. One
    /// shape with one extent key rather than two shapes, because "a run of bytes" is one notion and the
    /// only question is who knows how long it is.
    /// </para>
    ///
    /// <para>
    /// The recovered form carries the same direction-asymmetry as <see cref="Repeat"/>, for the same
    /// reason: on encode the octets exist and their count <i>is</i> the extent, so a preceding length
    /// field reads <c>fields.&lt;id&gt;.extent</c> while the span reads that field back on decode. The two
    /// point at each other in opposite directions and neither derives the other twice.
    /// </para>
    /// </summary>
    public sealed record Opaque(int? Width, Expr? Length) : Pattern
    {
        /// <summary>A span the declaration sizes.</summary>
        public Opaque(int width) : this(width, null) { }

        /// <summary>A span the message sizes.</summary>
        public static Opaque Measured(Expr length) => new(null, length);
    }

    /// <summary>
    /// An integer spread across octets with a continue flag, so its <b>width is a function of its value</b>.
    ///
    /// <para>
    /// This is the first shape whose extent cannot be known before its value is, and it is what the facet
    /// model was restructured for. Nothing iterates: <c>Extent</c> simply declares a dependency on
    /// <c>Value</c> and settles after it. The proposed alternative — encode, notice the width grew, widen
    /// and re-encode to a fixed point — is unnecessary whenever the measured region excludes the length
    /// field itself, which is every case in the corpus.
    /// </para>
    ///
    /// <para>
    /// <paramref name="Order"/> is required and is <b>not</b> a formality: two unrelated encoding families
    /// exhibit this notion in opposite group orders, and the same three octets mean different numbers under
    /// each. The continue flag follows the order — it always marks "another group follows".
    /// </para>
    /// </summary>
    /// <param name="Minimal">When true, a chain that is not the shortest representation of its value is a
    /// decode error rather than a value. That is what keeps value→octets injective, and it is the reason a
    /// padded chain is <i>rejected</i> instead of needing a remembered width to reproduce it.</param>
    public sealed record Varint(GroupOrder Order, int MaxOctets, bool Minimal = true) : Pattern;

    /// <summary>
    /// A named contiguous region: its children, in order, and nothing else.
    ///
    /// <para>
    /// This is what a length field measures. A frame prefix is <b>not</b> a separate declaration sitting
    /// beside the field list — it is an ordinary field whose value is a region's extent. Declaring framing
    /// twice is how the same two octets end up described by both a <c>frame</c> object and a template
    /// field, with nothing in the specification saying which one owns them and encode plausibly emitting
    /// 10, 12 or 14 octets depending on which reading you take. One declaration, so the question cannot
    /// be asked.
    /// </para>
    /// </summary>
    public sealed record Group(IReadOnlyList<Field> Fields) : Pattern;

    /// <summary>
    /// One of several packings, selected by a discriminator.
    ///
    /// <para>
    /// <paramref name="Key"/> is evaluated <b>the same way in both directions</b>: it reads
    /// <c>fields.&lt;id&gt;.value</c>, which on decode is what was just read off the wire and on encode is
    /// what is about to be written. That is what keeps one declaration serving both, instead of a decode
    /// guard and a separate encode-side variant selector that must be kept in agreement by hand.
    /// </para>
    ///
    /// <para>
    /// Exhaustiveness is <i>checked</i>, not trusted — see <see cref="ReachableKeys"/>. Sibling guards with
    /// complementary conditions cannot be checked for exhaustiveness or overlap by anything, which is how a
    /// message with an unanticipated discriminator decodes to zero bound fields and no error at all.
    /// </para>
    /// </summary>
    public sealed record Choice(Expr Key, IReadOnlyList<Arm> Arms) : Pattern
    {
        public Arm? Fallback => Arms.FirstOrDefault(a => a.IsFallback);

        /// <summary>Selects the arm for a discriminator value, or throws naming what arrived and what was
        /// declared.</summary>
        public Arm Select(long key, string fieldId)
            => Arms.FirstOrDefault(a => a.Key == key)
            ?? Fallback
            ?? throw new ProtoTypeException(
                $"field '{fieldId}': discriminator {key} (0x{key:x}) matches no arm, and none is declared "
              + $"as the fallback. Declared: {string.Join(", ", Arms.Select(a => a.IsFallback ? $"{a.Name}=*" : $"{a.Name}={a.Key}"))}");
    }

    /// <summary>
    /// N copies of one element.
    ///
    /// <para>
    /// The one place the two directions genuinely differ, and it is inherent rather than a shortcut: on
    /// encode the collection exists and its length <i>is</i> the count, while on decode the count has to be
    /// recovered from something already read. So <paramref name="Count"/> is the decode-side derivation
    /// and the field's own value expression is the encode-side source; neither is a restatement of the
    /// other, and there is no second document to keep in step.
    /// </para>
    ///
    /// <para>
    /// Note which way round the dependency falls on encode: a byte-count field ahead of the repetition
    /// reads <c>fields.&lt;repeat&gt;.extent</c>, so the count flows out of the data rather than into it.
    /// Had the count been declared as the shared truth in both directions, that field would depend on the
    /// repetition and the repetition on that field — a genuine cycle, and the reason the asymmetry is
    /// correct rather than merely convenient.
    /// </para>
    /// </summary>
    public sealed record Repeat(Field Element, Expr Count) : Pattern;

    /// <summary>Octets this pattern occupies, where that is fixed. Null means it depends on the value —
    /// which is exactly the case the resolver's facet ordering exists to handle.</summary>
    public int? StaticWidth => this switch
    {
        Scalar s => s.Octets,
        Bits b => b.TotalBits / 8,
        Opaque o => o.Width,
        Group g when g.Fields.All(f => f.Pattern.StaticWidth is not null)
            => g.Fields.Sum(f => f.Pattern.StaticWidth!.Value),
        _ => null,
    };

    /// <summary>The fields nested inside this pattern, if any. One place that knows the shape of the tree,
    /// so a walk cannot quietly miss an arm.</summary>
    public IReadOnlyList<Field> Nested => this switch
    {
        Group g => g.Fields,
        Choice c => [.. c.Arms.SelectMany(a => a.Fields)],
        Repeat r => [r.Element],
        _ => [],
    };

    /// <summary>Document-time checks. Cheap, and they catch the errors that are otherwise invisible until
    /// a capture decodes into plausible nonsense.</summary>
    public IReadOnlyList<string> Validate(string fieldId) => this switch
    {
        Scalar s when s.Octets is < 1 or > 8 =>
            [$"field '{fieldId}': a scalar must be 1..8 octets, got {s.Octets}"],

        Bits b when b.Slices.Count == 0 =>
            [$"field '{fieldId}': a bit group needs at least one slice"],

        Bits b when b.TotalBits % 8 != 0 =>
            [$"field '{fieldId}': bit slices total {b.TotalBits} bits, which is not a whole number of "
           + "octets. A misaligned group reads plausible values from the wrong place, so this is an error "
           + "rather than something padded silently."],

        Bits b when b.Slices.Any(s => s.Width is < 1 or > 32) =>
            [$"field '{fieldId}': each bit slice must be 1..32 bits wide"],

        // The spec's "exactly one extent key" rule, and it is worth being strict about: a span with two
        // answers to how long it is silently prefers one of them, and a span with none reads to the end
        // of whatever happens to be next.
        Opaque o when (o.Width is null) == (o.Length is null) =>
            [$"field '{fieldId}': an opaque span needs exactly one extent — a declared width or a length "
           + "recovered from the message, not both and not neither"],

        Opaque o when o.Width < 0 => [$"field '{fieldId}': an opaque span cannot be negative"],

        Varint v when v.MaxOctets is < 1 or > 10 =>
            [$"field '{fieldId}': a continuation-encoded integer must be bounded at 1..10 octets, got "
           + $"{v.MaxOctets}. The bound is what stops a chain of continue flags from being an allocation "
           + "request before anything has judged the message."],

        // An EMPTY region is deliberately legal. It measures zero, which is exactly what a length field
        // over an absent body must emit — a liveness probe with no payload is a real message, and
        // rejecting the empty case would force a second framing declaration for it.
        Choice c => ValidateChoice(c, fieldId),

        Repeat r when r.Element.Pattern.Nested.Count > 0 =>
            [$"field '{fieldId}': a repeated element must be a single wire shape. A composite element needs "
           + "per-element naming for its fields, which does not exist yet — expressions inside it would "
           + "resolve against whichever iteration ran last. Left unexpressible rather than guessed at."],

        _ => [],
    };

    /// <summary>
    /// The values a discriminator can take, when that is knowable.
    ///
    /// <para>
    /// A mask is the case that matters, because it is how a flag bit packed into a type octet is almost
    /// always written: <c>x &amp; 0x80</c> can only ever be 0 or 0x80, so "do these arms cover everything?"
    /// becomes decidable instead of a matter of trust. Where the keyset cannot be computed the engine says
    /// so and demands a fallback, rather than assuming the author thought of everything.
    /// </para>
    /// </summary>
    public static IReadOnlySet<long>? ReachableKeys(Expr key)
    {
        long? mask = key switch
        {
            Expr.Binary("&", _, Expr.Literal { Value: ProtoValue.Int m }) => m.Value,
            Expr.Binary("&", Expr.Literal { Value: ProtoValue.Int m }, _) => m.Value,
            Expr.Pipeline(_, Expr.Call("band", var args))
                when args.Count == 1 && args[0] is Expr.Literal { Value: ProtoValue.Int m } => m.Value,
            _ => null,
        };

        if (mask is not { } bits || bits <= 0) return null;

        List<int> positions = [];
        for (int i = 0; i < 64; i++)
            if ((bits & (1L << i)) != 0) positions.Add(i);

        // Past a handful of bits the enumeration is larger than any real arm list, and demanding a
        // fallback is both cheaper and more honest than listing 4096 keys.
        if (positions.Count > 12) return null;

        HashSet<long> values = [0];
        foreach (var position in positions)
            foreach (var value in values.ToList())
                values.Add(value | (1L << position));

        return values;
    }

    private static IReadOnlyList<string> ValidateChoice(Choice choice, string fieldId)
    {
        List<string> issues = [];

        if (choice.Arms.Count == 0)
        {
            issues.Add($"field '{fieldId}': a choice needs at least one arm");
            return issues;
        }

        foreach (var duplicate in choice.Arms.GroupBy(a => a.Name, StringComparer.Ordinal).Where(g => g.Count() > 1))
            issues.Add($"field '{fieldId}': two arms are both named '{duplicate.Key}' — the name is what a "
                     + "later step branches on, so it has to identify one shape");

        foreach (var duplicate in choice.Arms.Where(a => !a.IsFallback).GroupBy(a => a.Key!.Value)
                                             .Where(g => g.Count() > 1))
            issues.Add($"field '{fieldId}': discriminator {duplicate.Key} selects "
                     + $"{duplicate.Count()} arms — which one applies is not decidable");

        if (choice.Arms.Count(a => a.IsFallback) > 1)
            issues.Add($"field '{fieldId}': only one arm may be the fallback");

        var reachable = ReachableKeys(choice.Key);

        if (reachable is null)
        {
            if (choice.Fallback is null)
                issues.Add($"field '{fieldId}': the engine cannot compute which values this discriminator "
                         + "can take, so it cannot prove the arms are exhaustive. Declare a fallback arm "
                         + "(key null). An unanticipated discriminator otherwise binds no fields and "
                         + "reports no error, which is worse than either outcome you would have chosen.");
            return issues;
        }

        foreach (var arm in choice.Arms.Where(a => !a.IsFallback))
            if (!reachable.Contains(arm.Key!.Value))
                issues.Add($"field '{fieldId}': arm '{arm.Name}' is keyed {arm.Key} (0x{arm.Key:x}), which "
                         + "this discriminator can never produce — a dead arm is a mistake about the mask, "
                         + "not a harmless extra");

        var covered = choice.Arms.Where(a => !a.IsFallback).Select(a => a.Key!.Value).ToHashSet();
        var missing = reachable.Where(k => !covered.Contains(k)).OrderBy(k => k).ToList();

        if (missing.Count > 0 && choice.Fallback is null)
            issues.Add($"field '{fieldId}': the arms are not exhaustive — nothing handles "
                     + string.Join(", ", missing.Select(k => $"0x{k:x}"))
                     + ". Add the arms, or declare a fallback.");

        if (missing.Count == 0 && choice.Fallback is not null)
            issues.Add($"field '{fieldId}': arm '{choice.Fallback.Name}' is the fallback, but the other "
                     + "arms already cover every value the discriminator can take, so it can never be "
                     + "selected");

        return issues;
    }
}
