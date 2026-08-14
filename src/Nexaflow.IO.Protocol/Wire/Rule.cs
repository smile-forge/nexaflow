using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Values;

namespace Nexaflow.IO.Protocol.Wire;

/// <summary>
/// One value a field may take, or one run of them.
/// </summary>
/// <param name="From">Lowest legal value, inclusive.</param>
/// <param name="To">Highest legal value, inclusive. Equal to <paramref name="From"/> for a single value.</param>
public readonly record struct ValueRange(ProtoValue From, ProtoValue To)
{
    public static ValueRange Exactly(long value) => new(ProtoValue.Of(value), ProtoValue.Of(value));
    public static ValueRange Exactly(string value) => new(ProtoValue.Of(value), ProtoValue.Of(value));
    public static ValueRange Between(long low, long high) => new(ProtoValue.Of(low), ProtoValue.Of(high));

    public bool Admits(ProtoValue value)
    {
        // Through the value's own kind, so a part the document declared case-insensitive is admitted that
        // way here too. Otherwise the rule would have to be remembered at every site that compares, and it
        // would be remembered at some of them.
        if (Equals(From, To)) return ProtoValue.Alike(From, value);

        // A run only means anything over something ordered. Anything else is a set of exact values.
        if (From is not ProtoValue.Int low || To is not ProtoValue.Int high || value is not ProtoValue.Int v)
            return false;

        return v.Value >= low.Value && v.Value <= high.Value;
    }

    public override string ToString() => Equals(From, To) ? From.ToString() : $"{From}..{To}";
}

/// <summary>
/// Why a message can be refused.
///
/// <para>
/// Deliberately several kinds rather than one boolean predicate. A generic assertion would express all of
/// these and remember none of them: the <b>reason</b> a combination is illegal is what a diagnostic needs
/// in order to say something useful, what a reviewer needs in order to agree, and what a model drafting a
/// document needs in order to know which way to fix it. Collapsing "this value is out of range" together
/// with "these two fields contradict each other" together with "these segments came in the wrong order"
/// loses all of that, and the loss is invisible until someone has to act on a failure.
/// </para>
///
/// <para>
/// The kinds are not claimed to be complete. Two more are known and unbuilt, and are named here rather
/// than left to be rediscovered: <b>arrangement</b> (a segment may not follow another when some condition
/// holds — ordering as a constraint rather than as a declaration) and <b>state denial</b> (legal in
/// itself, but not while the conversation is in this state), which needs the state model that does not
/// exist yet.
/// </para>
/// </summary>
public abstract class Rule : Node
{
    public override string Name => ToString() ?? "rule";

    /// <summary>Why this rule exists, in the author's words. Carried into the failure, because "value 3 is
    /// not allowed" is a worse thing to read than the sentence the author would have written.</summary>
    public required string Because { get; init; }

    /// <summary>The nodes this rule is about. The builder turns each into an edge; nothing reads this
    /// afterwards, exactly as nothing reads the nesting once containment edges exist.</summary>
    internal abstract IEnumerable<Node> Applies { get; }

    /// <summary>Where the author wants this among the rules on the same node. Copied onto every edge it
    /// produces; the edge is what gets read.</summary>
    public int Order { get; init; } = int.MaxValue;

    /// <summary>
    /// A field's value is confined to a set of values and runs.
    ///
    /// <para>
    /// The commonest illegal by a distance, and the one a wire shape cannot express on its own: a field is
    /// two bits wide and only three of its four values are legal, or eight bits wide and reserved above
    /// sixteen. Note this is the opposite of a choice — a choice guarantees <i>some</i> arm matches, which
    /// makes an unexpected value harmless rather than refused.
    /// </para>
    ///
    /// <para>
    /// <b>It builds a <see cref="ValueSet"/>, because that is what it always was.</b> This carried a bare
    /// list of ranges while a value set carried the same ranges plus whether the list is the whole story
    /// and whether a value settles which member is meant — so the two said one thing and one of them said
    /// strictly less. The missing halves are not decoration: <see cref="Bounding"/> decides whether an
    /// unlisted value is refused or merely unrecognised, and <see cref="Exclusivity"/> decides whether
    /// asking two structures to differ is even a coherent request. A rule that omitted both was making
    /// those choices by default and not saying so.
    /// </para>
    /// </summary>
    public sealed class Domain : Rule
    {
        /// <summary>The field, or the bit run of one, whose values this confines.</summary>
        public required Field Field { get; init; }

        /// <summary>A named run inside <see cref="Field"/>, when the rule is about one rather than the
        /// whole group. Runs are not nodes of their own; they are part of the shape they sit in.</summary>
        public string? Run { get; init; }

        public required IReadOnlyList<ValueRange> Allowed { get; init; }

        /// <summary>Whether the listed values are all there will ever be. Confining a field to a written
        /// list is a closed statement unless the author says otherwise — an open one refuses nothing and
        /// would not be worth writing here.</summary>
        public Bounding Bounding { get; init; } = Bounding.Closed;

        /// <summary>Whether a value settles which member is meant.</summary>
        public Exclusivity Exclusivity { get; init; } = Exclusivity.Definitive;

        internal override IEnumerable<Node> Applies => [Field];

        public string What => Run ?? Field.Name;

        public bool Admits(ProtoValue value) => Allowed.Any(r => r.Admits(value));

        /// <summary>This rule as the set it describes, so one notion has one node behind it.</summary>
        public ValueSet AsSet() => new()
        {
            Id = $"{What} allowed",
            Members = [.. Allowed.Select(r => new SetMember(r))],
            Bounding = Bounding,
            Exclusivity = Exclusivity,
            Because = Because,
        };

        public override string ToString() => $"{What} ∈ {{{string.Join(", ", Allowed)}}}";
    }

    /// <summary>
    /// One condition obliges another — the <b>forced</b> multipart rule.
    ///
    /// <para>
    /// Satisfying part of it is an error, not an absence. That is the whole distinction from a choice: a
    /// section guarded by <c>X &amp;&amp; Y</c> simply does not appear when only X holds, and for some
    /// protocols that is correct, while for others setting X and omitting Y is a malformed message the
    /// receiver must reject. Both readings are reasonable and the document has to be able to say which.
    /// </para>
    /// </summary>
    public sealed class Requires : Rule
    {
        /// <summary>The node whose scope the conditions are read in — the message, or one structure of a
        /// chain, in which case the rule is checked once per structure.</summary>
        public required Node Within { get; init; }

        public required Expr When { get; init; }
        public required Expr Then { get; init; }

        internal override IEnumerable<Node> Applies => [Within];

        public override string ToString() => $"{When.Render()} requires {Then.Render()}";
    }

    /// <summary>
    /// Two conditions that must never hold together.
    ///
    /// <para>
    /// The dual of <see cref="Requires"/> and kept separate from it on purpose: written as an implication
    /// with a negated consequent it reads backwards, and a reviewer checking "can these ever combine?"
    /// should not have to invert anything to answer.
    /// </para>
    /// </summary>
    public sealed class Excludes : Rule
    {
        public required Node Within { get; init; }

        public required Expr One { get; init; }
        public required Expr Other { get; init; }

        internal override IEnumerable<Node> Applies => [Within];

        public override string ToString() => $"{One.Render()} excludes {Other.Render()}";
    }

    /// <summary>
    /// Something that must hold wherever its scope is — the degenerate case of an obligation, and common
    /// enough to be worth its own name.
    ///
    /// <para>
    /// Written as <see cref="Requires"/> with a condition of <c>true</c> it reads as a riddle, and the
    /// point of separating the kinds is that a reader should not have to decode which one they are looking
    /// at.
    /// </para>
    /// </summary>
    public sealed class Invariant : Rule
    {
        public required Node Within { get; init; }
        public required Expr Must { get; init; }

        internal override IEnumerable<Node> Applies => [Within];

        public override string ToString() => $"always {Must.Render()}";
    }

    /// <summary>
    /// How consecutive structures of a chain must sit relative to each other.
    ///
    /// <para>
    /// A packing exclusion rather than a value one: it is about arrangement, and it constrains the
    /// relationship between two structures rather than anything inside either. <c>previous</c> is the
    /// structure before this one; the rule is not checked for the first, which has none.
    /// </para>
    /// </summary>
    public sealed class Arrangement : Rule
    {
        /// <summary>The chain whose structures this orders.</summary>
        public required Field Chain { get; init; }

        public required Expr Must { get; init; }

        internal override IEnumerable<Node> Applies => [Chain];

        public override string ToString() => $"each structure after the first: {Must.Render()}";
    }

    /// <summary>
    /// No two structures of a chain may carry the same value.
    ///
    /// <para>
    /// A packing exclusion over the whole run rather than over a pair, which is why
    /// <see cref="Arrangement"/> cannot express it: <c>previous</c> reaches one structure back and a
    /// duplicate can be any distance away.
    /// </para>
    ///
    /// <para>
    /// Its legality depends on the <see cref="ValueSet"/> the field draws from, and that is the whole
    /// reason a set carries <see cref="Exclusivity"/>. Over a <see cref="Exclusivity.Definitive"/> set a
    /// value denotes one thing, so two structures carrying it are two claims about that thing and the
    /// second is a contradiction. Over an <see cref="Exclusivity.Indicative"/> one the same value need not
    /// mean the same thing twice, so requiring distinctness would refuse messages that are perfectly
    /// well formed — the engine says so at document time rather than enforcing a rule that is not true.
    /// </para>
    /// </summary>
    public sealed class Distinct : Rule
    {
        /// <summary>The chain whose structures this separates.</summary>
        public required Field Chain { get; init; }

        /// <summary>The field inside the structure whose value must differ. It must draw from a set, since
        /// the set is what says whether repeating a value means anything.</summary>
        public required Field Of { get; init; }

        internal override IEnumerable<Node> Applies => [Chain];

        public override string ToString() => $"no two structures of {Chain.Name} share a {Of.Name}";
    }

    /// <summary>Names the rule reads, so a reference to a field that is not in scope is an authoring error
    /// rather than a silent pass.</summary>
    internal IEnumerable<Expr> Expressions => this switch
    {
        Requires r => [r.When, r.Then],
        Excludes e => [e.One, e.Other],
        Invariant i => [i.Must],
        Arrangement a => [a.Must],
        _ => [],
    };
}
