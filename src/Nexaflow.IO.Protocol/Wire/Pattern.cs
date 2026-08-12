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
/// <param name="key">The discriminator value selecting this arm, or null for the fallback.
///
/// <para>
/// Authoring input only. It is copied onto the <see cref="Offers"/> edge when the graph is built, and the
/// <b>edge</b> is what the engine reads — which value picks this packing is a fact about this choice
/// offering it, not about the packing. Nothing here is consulted afterwards, exactly as nothing consults
/// a rule's declared order once its <see cref="Constrains"/> edges exist.
/// </para>
/// </param>
/// <param name="fields">The fields this packing contributes, in order.</param>
public sealed class Arm(string label, long? key, IReadOnlyList<Field> fields) : Node
{
    public override string Name { get; } = label;

    /// <summary>
    /// The value that selects this packing — <b>any</b> value, not a number.
    ///
    /// <para>
    /// It was a <c>long</c>, which quietly said that a protocol identifies its parts with integers. Half of
    /// them do — a descriptor type, an option code, an extension number — and the other half write the name
    /// out: <c>Content-Length</c>, <c>INVITE</c>. Those are the same notion wearing a different alphabet,
    /// and keying on the narrower type would have forced a second, parallel mechanism for the text half.
    /// </para>
    /// </summary>
    internal ProtoValue? DeclaredKey { get; private init; } = key is { } k ? ProtoValue.Of(k) : null;

    public IReadOnlyList<Field> Fields { get; } = fields;

    /// <summary>
    /// Whether this packing may turn up more than once in the run it belongs to.
    ///
    /// <para>
    /// Authoring input, copied onto the <see cref="Offers"/> edge like the key. It decides two things at
    /// once, and they are the same thing: what a second appearance <i>means</i> — a list rather than a
    /// contradiction — and whether anything outside can name this packing's fields. One of something is
    /// addressable because there is exactly one of it; several are not, and the engine says so at document
    /// time rather than letting an edge point at whichever appearance settled last.
    /// </para>
    /// </summary>
    internal bool Repeats { get; private init; }

    /// <summary>
    /// What has to hold for this packing to be an option at all, over <c>present.&lt;part&gt;</c>.
    ///
    /// <para>
    /// A different question from the key, and the difference is why it is not one. A key says which value
    /// picks this packing; this says whether picking it is available. A body is counted because a length
    /// arrived, not because some field holds a number meaning "counted" — there is no such field.
    /// </para>
    ///
    /// <para>
    /// The reason this can exist when sibling guards were refused: guards over arbitrary expressions cannot
    /// be checked for cover or overlap by anything, which is how an unanticipated message binds no fields
    /// and reports nothing. A condition over presence is different in kind — the parts are declared and
    /// finite, so the assignments are enumerable and cover and disjointness are <i>proved</i>, by the same
    /// enumeration that proves a mask's arms exhaustive.
    /// </para>
    /// </summary>
    internal Expr? Condition { get; private init; }

    /// <summary>A packing selected by a token it carries rather than by a number a sibling holds.</summary>
    public static Arm On(string label, ProtoValue token, IReadOnlyList<Field> fields, bool repeats = false)
        => new(label, null, fields) { DeclaredKey = token, Repeats = repeats };

    /// <summary>A packing that is an option only while something holds.</summary>
    public static Arm While(string label, Expr condition, IReadOnlyList<Field> fields)
        => new(label, null, fields) { Condition = condition };

    /// <summary>The packing for a component this document does not know. Never optional where the tokens
    /// are text: nothing can enumerate every string, so the cover can only be closed by a fallback.</summary>
    public static Arm Otherwise(string label, IReadOnlyList<Field> fields, bool repeats = false)
        => new(label, null, fields) { Repeats = repeats };
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
    /// <param name="Until">
    /// The octets that end the span, where nothing says how long it is.
    ///
    /// <para>
    /// A whole family of protocols has no widths and no length prefixes at all: every extent is set by a
    /// separator, and a field is however many octets there are before the next one. The delimiter itself
    /// is <b>not consumed</b> — it stays a field, so there is something to write it back out with, and so
    /// a document can fix its value and have that checked.
    /// </para>
    /// </summary>
    /// <param name="Ceiling">How far to look. A scan with no bound is a promise about data nobody has
    /// read yet.</param>
    public sealed record Opaque(int? Width, Expr? Length, byte[]? Until = null, int Ceiling = 8192) : Pattern
    {
        /// <summary>A span the declaration sizes.</summary>
        public Opaque(int width) : this(width, null) { }

        /// <summary>A span the message sizes.</summary>
        public static Opaque Measured(Expr length) => new(null, length);

        /// <summary>A span the next separator sizes.</summary>
        public static Opaque Before(params byte[] delimiter) => new(null, null, delimiter);

        public static Opaque Before(string delimiter)
            => new(null, null, [.. delimiter.Select(c => (byte)c)]);

        /// <summary>How many of the three ways of saying how long it is were given. Exactly one is the
        /// rule; the count is what says so.</summary>
        internal int Extents => (Width is null ? 0 : 1) + (Length is null ? 0 : 1) + (Until is null ? 0 : 1);
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
    /// A small value carried in one octet, escaping to a counted run of octets when it will not fit.
    ///
    /// <para>
    /// The other way a width comes from a value, and a genuinely different notion from
    /// <see cref="Varint"/>: there is no continue flag and no regrouping into seven-bit digits. One marker
    /// octet either <i>is</i> the value, or says how many octets follow that carry it.
    /// </para>
    ///
    /// <para>
    /// <paramref name="InlineLimit"/> is where the escape begins and is required — it is the whole
    /// parameterisation, and defaulting it would put one encoding family's threshold in the engine. At 128
    /// the marker's top bit is the escape and its low seven bits are the octet count, which is the form
    /// most encodings of this shape use; nothing about the notion requires that particular number.
    /// </para>
    /// </summary>
    /// <param name="Minimal">On decode, a value carried in more octets than it needs — or in the escaped
    /// form when it would have fitted inline — is an error rather than a value, for the same reason as
    /// <see cref="Varint.Minimal"/>: it is what keeps value→octets injective.</param>
    public sealed record EscapedInline(long InlineLimit, int MaxOctets, bool Minimal = true) : Pattern;

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
    /// <param name="Extent">
    /// The decode-side bound, where the region has one.
    ///
    /// <para>
    /// A region measures its children on the way out; on the way in it is transparent unless something
    /// already read says how far it runs. Declaring that turns the region into a <b>boundary</b>, which is
    /// what gives "is there room for another" a meaning — and what lets a structure that runs to the end
    /// of its container terminate at all. The same direction-asymmetry as everywhere else: one side has
    /// the data, the other has to be told.
    /// </para>
    /// </param>
    public sealed record Group(IReadOnlyList<Field> Fields, Expr? Extent = null) : Pattern;

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
    /// <param name="Selects">
    /// The encode-side discriminator, where the two directions decide the same thing by asking different
    /// questions.
    ///
    /// <para>
    /// The same direction-asymmetry as <see cref="Opaque.Length"/> and <see cref="Chain.Continues"/>, and
    /// it took two unrelated protocols to notice it was missing here: a section that is present only
    /// sometimes is recognised on the way in by looking at the region — <c>room &gt; 0</c>, or what the
    /// next octet's tag says — and on the way out by whether the caller supplied one. Neither question
    /// can be asked in the other direction. With this given, <paramref name="Key"/> becomes the reader's
    /// question alone and may use the reader's vocabulary.
    /// </para>
    ///
    /// <para>
    /// Both must land on the same arm keys, so exhaustiveness is still proved once and covers both.
    /// This is a second <i>reading</i> of one discriminator, not a second discriminator.
    /// </para>
    /// </param>
    /// <param name="Key">
    /// Null where the arms carry conditions instead.
    ///
    /// <para>
    /// Some branches have no discriminator anywhere on the wire. HTTP's body is counted, chunked or
    /// runs to the close of the connection, and which one is decided by <i>which headers turned up</i> —
    /// there is no field holding a number that means "chunked". Writing one as a key expression would be
    /// inventing a discriminator to satisfy the shape.
    /// </para>
    /// </param>
    public sealed record Choice(Expr? Key, IReadOnlyList<Arm> Arms, Expr? Selects = null) : Pattern
    {
        /// <summary>Whether the arms decide for themselves rather than being picked by a value.</summary>
        public bool Conditioned => Key is null;

        /// <summary>Which expression decides, in the direction being walked.</summary>
        public Expr? Deciding(bool encoding) => encoding && Selects is not null ? Selects : Key;
    }

    /// <summary>
    /// A structure that may be followed by another of the same shape.
    ///
    /// <para>
    /// Deliberately <b>not</b> a repetition. A repetition says "N of the same thing, addressed by index",
    /// and no protocol on the wire means that: space is scarce, so what looks like a repeat is a second
    /// structure with the same shape and different values, and every instance is an entry someone wants
    /// to name — this register, the grant for this topic, the value at this identifier. Index is packing,
    /// not identity.
    /// </para>
    ///
    /// <para>
    /// The mechanical consequence is what makes the distinction worth drawing. Termination stops being a
    /// count computed before anything is read and becomes a question asked before each instance: <i>is
    /// there another?</i> That is the only form in which "as many as fit in the region" can be stated at
    /// all — once instances vary in length, their number is not a function of the byte count — and a fixed
    /// count is then just one way of answering it.
    /// </para>
    ///
    /// <para>
    /// <paramref name="Continues"/> is evaluated in the chain's own scope, with <c>ordinal</c> bound to the
    /// number of complete instances so far and <c>room</c> to the unread octets left in the enclosing
    /// region. So <c>room &gt; 0</c> runs to the end of the container and
    /// <c>ordinal &lt; fields.count.value</c> honours a declared count, from one construct.
    /// </para>
    ///
    /// <para>
    /// Each instance gets its own field scope, which is what lets an instance carry its own length prefix:
    /// <c>fields.body.extent</c> inside instance 2 means instance 2's, not instance 1's and not an
    /// ambiguous document-global span. Names not belonging to the instance resolve outward, so a field can
    /// still read the message metadata around it.
    /// </para>
    /// </summary>
    /// <param name="Seed">
    /// The starting value of <c>carried</c>, where the chain threads one.
    ///
    /// <para>
    /// Some structures cannot be named without the ones before them: an entry that records how far its
    /// identifier has moved since the last entry rather than what its identifier is. Nothing about a
    /// single structure recovers that, so a value is threaded along the chain and each structure updates
    /// it. This is the fold across siblings that a per-field walk cannot express.
    /// </para>
    /// </param>
    /// <param name="Carry">The next <c>carried</c>, evaluated in the structure's own scope once it has
    /// been read, so it can be computed from what that structure said.</param>
    public sealed record Chain(Field Element, Expr Continues, Expr? Seed = null, Expr? Carry = null) : Pattern
    {
        public bool Threads => Seed is not null && Carry is not null;
    }

    /// <summary>
    /// A run of components, each of which says which of several declared kinds it is.
    ///
    /// <para>
    /// <b>Not a chain of choices, though it is spelled almost the same.</b> A chain repeats one structure,
    /// so its instances are told apart by counting and its names have to be scoped per instance. A choice
    /// picks one packing, once. This is the product of the two, and the product has a property neither
    /// factor has: because each kind is <i>separately declared</i>, one of them is a node something else
    /// can point at. That is the whole reason it exists. A body sized by the component called
    /// <c>Content-Length</c> has nothing to reference under a chain — the length lives in the third
    /// instance of one declared element here and the first instance there, and which is a fact about the
    /// data. Under this it lives in a node, and the edge is ordinary.
    /// </para>
    ///
    /// <para>
    /// So the components do <b>not</b> open a name scope, unlike a chain's instances — with the exception
    /// of the ones declared to repeat, which reopen exactly the problem scoping solves and are therefore
    /// scoped and unaddressable. Cardinality decides addressability, and it is declared.
    /// </para>
    ///
    /// <para>
    /// Order is preserved and is not significant. The engine writes the components in the order the value
    /// lists them, which is what lets a message decode and re-encode to the same octets even though the
    /// protocol would have accepted any arrangement.
    /// </para>
    /// </summary>
    /// <param name="Token">What each component leads with, and what says which kind it is. Read before
    /// anything else, because nothing after it is decodable until the kind is known. It carries no value
    /// expression of its own — what gets written is the selected kind's key.</param>
    /// <param name="Sorts">The kinds this document knows, each with the token that identifies it, plus a
    /// fallback for the ones it does not.</param>
    /// <param name="Continues">Is there another component? The reader's question, exactly as a chain's is —
    /// on the way out the value says how many there are and in what order.</param>
    public sealed record Assorted(
        Field Token,
        IReadOnlyList<Arm> Sorts,
        Expr Continues) : Pattern
    {
        /// <summary>
        /// The member of a component's record that says which kind it is. Present in what a decode produces
        /// and in what an encode consumes, so a decoded run re-encodes as it arrived.
        /// </summary>
        public const string Which = "sort";

        // There is deliberately nothing here for the punctuation that brackets a component — the separator
        // after the token, the newline that ends the line. Both were parameters for a while, and both were
        // the engine asserting that every kind of component is wrapped the same way. That is true of one
        // protocol at a time and not of the notion: a header can fold onto the next line, a body can be
        // chunked, and a length can stand in for a terminator. So the punctuation is declared where it
        // applies, as ordinary constant fields of the kind it belongs to, and a kind that ends differently
        // simply says so. A few more nodes; no assumption.
    }

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
        Chain c => [c.Element],
        Assorted a => [a.Token, .. a.Sorts.SelectMany(s => s.Fields)],
        _ => [],
    };

    /// <summary>
    /// The nested fields that belong to the <b>enclosing</b> name scope, rather than to one of this
    /// pattern's own.
    ///
    /// <para>
    /// A chain contributes nothing: its instances are separate structures and their names have to be too.
    /// An assortment contributes everything except the kinds declared to repeat — one of something is
    /// nameable because there is exactly one of it, and several are not. Everything else contributes all of
    /// it, because a region and an arm are places in one structure rather than structures of their own.
    /// </para>
    /// </summary>
    public IReadOnlyList<Field> InScope => this switch
    {
        Chain => [],
        Assorted a => [a.Token, .. a.Sorts.Where(s => !s.Repeats).SelectMany(s => s.Fields)],
        _ => Nested,
    };

    /// <summary>The nested fields that get a scope of their own, and the node each scope belongs to.</summary>
    public IEnumerable<(Node Owner, IReadOnlyList<Field> Fields)> Scoped => this switch
    {
        Chain c => [(c.Element, [c.Element])],
        Assorted a => a.Sorts.Where(s => s.Repeats).Select(s => ((Node)s, s.Fields)),
        _ => [],
    };

    /// <summary>Document-time checks. Cheap, and they catch the errors that are otherwise invisible until
    /// a capture decodes into plausible nonsense.</summary>
    /// <param name="inScope">The other fields visible where this one is declared, so a discriminator that
    /// reads a bit slice can be checked against how wide that slice actually is.</param>
    public IReadOnlyList<string> Validate(string fieldId, IReadOnlyDictionary<string, Pattern>? inScope = null)
        => this switch
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
        Opaque o when o.Extents != 1 =>
            [$"field '{fieldId}': an opaque span needs exactly one extent — a declared width, a length "
           + "recovered from the message, or the separator it runs up to. It has {o.Extents}."],

        Opaque { Until.Length: 0 } =>
            [$"field '{fieldId}': an empty separator ends every span at once"],

        Opaque o when o.Width < 0 => [$"field '{fieldId}': an opaque span cannot be negative"],

        EscapedInline e when e.InlineLimit is < 1 or > 255 =>
            [$"field '{fieldId}': the escape threshold must be 1..255, got {e.InlineLimit} — the marker is "
           + "one octet, so a value at or above 256 could never be carried inline"],

        EscapedInline e when e.MaxOctets is < 1 or > 8 || e.InlineLimit + e.MaxOctets > 255 =>
            [$"field '{fieldId}': at a threshold of {e.InlineLimit} the marker can count at most "
           + $"{255 - e.InlineLimit} octet(s), and no more than 8 in any case — {e.MaxOctets} is not "
           + "expressible"],

        Varint v when v.MaxOctets is < 1 or > 10 =>
            [$"field '{fieldId}': a continuation-encoded integer must be bounded at 1..10 octets, got "
           + $"{v.MaxOctets}. The bound is what stops a chain of continue flags from being an allocation "
           + "request before anything has judged the message."],

        // An EMPTY region is deliberately legal. It measures zero, which is exactly what a length field
        // over an absent body must emit — a liveness probe with no payload is a real message, and
        // rejecting the empty case would force a second framing declaration for it.
        //
        // A choice is checked in MessageDef instead: its arm keys live on the Offers edges now, and a
        // pattern cannot see the graph. That is the right way round — a shape should not be the authority
        // on a relationship between two nodes.
        Choice { Arms.Count: 0 } => [$"field '{fieldId}': a choice needs at least one arm"],

        // Two ways of deciding is one of them being ignored, and which is a matter of reading the engine.
        Choice { Key: not null } c when c.Arms.Any(a => a.Condition is not null) =>
            [$"field '{fieldId}': it has a discriminator and its arms also say when they are options. "
           + "Either a value picks the packing or the packings say when they apply, not both."],

        Choice { Key: null } c when c.Arms.Any(a => a.DeclaredKey is not null) =>
            [$"field '{fieldId}': it has no discriminator, so an arm keyed on one can never be selected. "
           + "Give the arms conditions, or give the choice something to decide on."],

        Choice { Key: null, Selects: not null } =>
            [$"field '{fieldId}': it has no discriminator to have two readings of. A condition answers in "
           + "both directions already — a reader knows what arrived and a writer knows what it was given."],

        Assorted { Sorts.Count: 0 } =>
            [$"field '{fieldId}': an assortment needs at least one kind of component"],

        // The token decides which kind arrived, so a document that also writes it by hand has said the same
        // thing twice and the two can disagree — which would emit a component announcing one kind and
        // packed as another.
        Assorted a when a.Token.Value is not null =>
            [$"field '{fieldId}': its token '{a.Token.Id}' has a value of its own. What gets written there "
           + "is the selected kind's key; a second answer to the same question is one of them being ignored."],

        Assorted a when a.Token.Pattern is Group or Choice or Chain or Assorted =>
            [$"field '{fieldId}': its token '{a.Token.Id}' is a composite. A token has to be one value, "
           + "because it is compared against the key that picks a kind."],

        // The quiet one. A token with nothing to turn octets into text reads as octets, no text key ever
        // equals it, and every component takes the fallback — a message that decodes without complaint and
        // has understood none of itself.
        Assorted a when a.Sorts.Any(s => s.DeclaredKey is ProtoValue.Text)
                     && a.Token is { Via: null, Through: null } =>
            [$"field '{fieldId}': its kinds are named by text and its token '{a.Token.Id}' has no converter, "
           + "so what it reads is octets and no key can ever match one. Every component would take the "
           + "fallback and nothing would say so."],

        Chain c when (c.Seed is null) != (c.Carry is null) =>
            [$"field '{fieldId}': a chain that threads a value needs both where it starts and how each "
           + "structure moves it on — one without the other says nothing"],

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
    public static IReadOnlySet<ProtoValue>? ReachableKeys(Expr key, IReadOnlyDictionary<string, Pattern>? inScope = null)
        => Numbers(key, inScope)?.Select(ProtoValue.Of).ToHashSet();

    /// <summary>
    /// The numbers a discriminator can take, when that is knowable.
    ///
    /// <para>
    /// Only ever numbers. A token drawn from text has no computable range — nothing enumerates every
    /// string — so an assortment keyed on names is exactly the case that <i>must</i> declare a fallback,
    /// which is also what its protocol requires: a component nobody has heard of has to be carried through,
    /// not dropped.
    /// </para>
    /// </summary>
    private static IReadOnlySet<long>? Numbers(Expr key, IReadOnlyDictionary<string, Pattern>? inScope)
    {
        // A named run of bits is the other case where the range is known, and it is how presence is
        // usually written: a section exists because one bit says so. Requiring `band 0x01` on a one-bit
        // value to tell the validator its range would be noise about something it can look up.
        //
        // A comparison answers one of two things, whatever it compares, so its cover can be proved.
        // NOTE: this once claimed that made "is there anything left?" a usable discriminator, and it does
        // not — `room` is bound on the way in and has no meaning on the way out, where extents are still
        // being computed. An optional trailing section is a genuine direction-asymmetry, the same one
        // `Chain.Continues` and `Opaque.Length` have: on decode you ask the region, on encode you have
        // the value or you do not. A choice key is the one place that asymmetry was asserted not to
        // apply, and the assertion was wrong. Found by reading the generated prose against RFC 5246 §7.4,
        // where a hello with no extension block at all is the ordinary pre-2003 case.
        if (key is Expr.Binary("==" or "!=" or "<" or "<=" or ">" or ">=" or "&&" or "||", _, _)
                or Expr.Unary("!", _))
            return new HashSet<long> { 0, 1 };

        if (SliceWidth(key, inScope) is { } width)
            return width <= 12 ? Enumerable.Range(0, 1 << width).Select(v => (long)v).ToHashSet() : null;

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

    /// <summary>The width of the bit run a discriminator reads, if that is what it reads —
    /// <c>fields.&lt;id&gt;.value.&lt;slice&gt;</c> against a field in scope that is a bit group.</summary>
    private static int? SliceWidth(Expr key, IReadOnlyDictionary<string, Pattern>? inScope)
    {
        if (inScope is null) return null;

        if (key is not Expr.Member(Expr.Member(Expr.Member(Expr.Root("fields"), var fieldId), "value"), var slice))
            return null;

        return inScope.TryGetValue(fieldId, out var pattern) && pattern is Bits bits
            ? bits.Slices.FirstOrDefault(s => string.Equals(s.Name, slice, StringComparison.Ordinal)) is { Width: > 0 } found
                ? found.Width
                : null
            : null;
    }

}
