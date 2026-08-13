using Nexaflow.IO.Protocol.Expressions;

namespace Nexaflow.IO.Protocol.Wire;

/// <summary>
/// Where an expression sits, which is what decides who can answer it.
///
/// <para>
/// Not every question has an answer in both directions, and which ones do is a property of the site rather
/// than of the expression. <c>room</c> is how much of the enclosing region is unread — a question only a
/// reader can answer, because while writing there is no region yet, only extents still settling.
/// <c>inputs</c> is what the caller supplied — a question only a writer can answer, because a reader has a
/// packet and no caller. A site that runs in both directions may therefore use neither.
/// </para>
/// </summary>
public enum ExprSite
{
    /// <summary>A field's value. Encode only: on decode the octets are the value.</summary>
    Value,

    /// <summary>An opaque span's recovered length. Decode only: on encode the octets exist and their
    /// count is the extent.</summary>
    Length,

    /// <summary>A region's declared bound. Decode only, for the same reason.</summary>
    Bound,

    /// <summary>Whether another structure follows. Decode only: on encode the list says how many.</summary>
    Continuation,

    /// <summary>Where a chain's threaded value starts, evaluated outside the first structure. Both.</summary>
    Seeding,

    /// <summary>The next threaded value, computed inside a structure once it is settled. Both.</summary>
    Carry,

    /// <summary>Which packing. Both, unless the choice also declares a <see cref="Selection"/>.</summary>
    Discriminator,

    /// <summary>Which packing arrived, where the choice also declares a <see cref="Selection"/> — so this
    /// half is the reader's alone and may ask the reader's questions.</summary>
    Recognition,

    /// <summary>The encode-side discriminator, where the two directions decide differently.</summary>
    Selection,

    /// <summary>A rule's condition. Both.</summary>
    Condition,

    /// <summary>What a message does to a state, and what it leaves behind. Read against a scope built
    /// from the decoded captures and nothing else.</summary>
    Moving,

    /// <summary>How a reference to another node is written down. The one place <c>position</c> means
    /// anything — an offset is a rendering of a relationship, and everywhere else it is an invitation to
    /// think in offsets about things that are relationships.</summary>
    Locating,

    /// <summary>An arrangement rule, over one structure and the one before it. Both.</summary>
    Pairing,
}

/// <summary>
/// The walk's own roots, and where each of them can be answered.
///
/// <para>
/// This table exists because three defects in a row were the same defect: a name bound at one scope-
/// construction site and not at another, which evaluates to nothing and makes every comparison against it
/// quietly false. <c>carried</c> was bound for discriminators and not for field expressions. <c>room</c>
/// is bound on the way in and was documented as usable on the way out. A field's value settled as the
/// octets when writing and as the value when reading. None of the three failed loudly; each was found by
/// a capture that happened to need it, years of protocol apart in corpus terms.
/// </para>
///
/// <para>
/// So availability is data and it is checked at document time. An expression that names a root its site
/// cannot answer is an authoring error with a sentence explaining which direction cannot answer it — not
/// a silent <c>Nothing</c> that surfaces as a byte count two short.
/// </para>
/// </summary>
public static class Vocabulary
{
    public const string Fields = "fields";
    public const string Inputs = "inputs";
    public const string Item = "item";
    public const string Previous = "previous";
    public const string Room = "room";
    public const string Peek = "peek";
    public const string Carried = "carried";
    public const string Ordinal = "ordinal";

    /// <summary>Where the pointed-at node landed. Bound at exactly one site.</summary>
    public const string Position = "position";

    /// <summary>
    /// Whether a component that might not be there, is — <c>present.&lt;part&gt;</c>.
    ///
    /// <para>
    /// The one question about optionality that <b>both</b> directions can answer, which is why it is
    /// vocabulary rather than state. A reader knows because the component bound; a writer knows because
    /// the value listed it. The tempting alternative — a state a header sets when it is <i>seen</i> — is a
    /// reader's verb with no writer's counterpart, and would have needed a state written half way through
    /// a decode to answer a question the message already knows about itself.
    /// </para>
    ///
    /// <para>
    /// Bound to every optional part in the message, not only the ones in scope, so it reads false rather
    /// than nothing for one that did not arrive. A misspelling is caught at document time instead.
    /// </para>
    /// </summary>
    public const string Present = "present";

    /// <summary>
    /// What earlier messages left behind — <c>kept.&lt;slot&gt;</c>.
    ///
    /// <para>
    /// A move could say what to put in a slot and not what was already in it, so it could set and never
    /// accumulate. That rules out the whole of reassembly: a message spread over several frames is exactly
    /// "what is there already, and then this", and there was no way to write the first half of that down.
    /// </para>
    ///
    /// <para>
    /// It is the value <i>before</i> this move, so several recordings on one transition all see the same
    /// thing rather than each other's work. A move is one event and its parts do not happen in an order.
    /// </para>
    /// </summary>
    public const string Kept = "kept";

    /// <summary>Everything the walk binds. A root outside this set is not vocabulary at all.</summary>
    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.Ordinal)
            { Fields, Inputs, Item, Previous, Room, Peek, Carried, Ordinal, Position, Present, Kept };

    /// <remarks>
    /// <c>inputs</c> is available at <b>every</b> site, and it used to be banned from the reader's ones
    /// with the explanation that while decoding there is no caller, only a packet. That was wrong, and the
    /// signature was the evidence: <c>Decode</c> takes a scope. A value an earlier exchange left behind is
    /// as available to a reader as anything on the wire, and a short-header packet whose connection
    /// identifier has no length in it — the length was agreed during a handshake — is not an exotic case,
    /// it is how a connection-oriented protocol saves the octets. The ban made state-dependent parsing
    /// inexpressible in the one place it matters, in the table built to stop exactly this class of
    /// mistake.
    ///
    /// <para>
    /// <c>item</c> is the genuine writer-only one and stays that way: it is the structure being written,
    /// and while reading there is no such thing — the structure is what is being worked out.
    /// </para>
    /// </remarks>
    private static readonly Dictionary<ExprSite, HashSet<string>> Answerable = new()
    {
        [ExprSite.Value] = [Fields, Inputs, Item, Carried, Ordinal, Present],
        [ExprSite.Length] = [Fields, Inputs, Room, Peek, Carried, Ordinal, Present],
        [ExprSite.Bound] = [Fields, Inputs, Room, Peek, Carried, Ordinal, Present],
        [ExprSite.Continuation] = [Fields, Inputs, Room, Peek, Carried, Ordinal, Present],
        [ExprSite.Seeding] = [Fields, Inputs, Carried, Ordinal, Present],
        [ExprSite.Carry] = [Fields, Inputs, Carried, Ordinal, Present],
        [ExprSite.Discriminator] = [Fields, Inputs, Carried, Ordinal, Present],
        [ExprSite.Recognition] = [Fields, Inputs, Room, Peek, Carried, Ordinal, Present],
        [ExprSite.Selection] = [Fields, Inputs, Item, Carried, Ordinal, Present],
        [ExprSite.Condition] = [Fields, Inputs, Carried, Ordinal, Present],
        [ExprSite.Locating] = [Fields, Inputs, Position, Carried, Ordinal],
        [ExprSite.Pairing] = [Item, Previous],

        // A move is read against the decoded captures and nothing else. Not even a region's extent: the
        // scope carries values, so `.extent` reads as nothing and every comparison against it is quietly
        // false — which is the failure this table exists for, in the one place it had never reached.
        [ExprSite.Moving] = [Fields, Kept],
    };

    public static IReadOnlySet<string> Available(ExprSite site) => Answerable[site];

    /// <summary>What this site is called in a diagnostic — the author's words for it, not the enum's.</summary>
    public static string Describe(ExprSite site) => site switch
    {
        ExprSite.Value => "a value",
        ExprSite.Length => "a span's length",
        ExprSite.Bound => "a region's bound",
        ExprSite.Continuation => "a continuation",
        ExprSite.Seeding => "a chain's seed",
        ExprSite.Carry => "a carry",
        ExprSite.Discriminator => "a discriminator",
        ExprSite.Recognition => "a discriminator's reading of the wire",
        ExprSite.Selection => "an encode-side selection",
        ExprSite.Condition => "a rule",
        ExprSite.Locating => "a reference",
        ExprSite.Pairing => "an arrangement rule",
        ExprSite.Moving => "a move",
        _ => site.ToString(),
    };

    /// <summary>
    /// Why this root cannot answer here. The sentence matters more than the refusal: an author who is told
    /// "room is not available" writes it again somewhere else, and one who is told what room <i>is</i>
    /// reaches for the construct that does work.
    /// </summary>
    public static string Why(string root, ExprSite site)
    {
        string here = Describe(site);

        return root switch
        {
            Room or Peek when site is ExprSite.Value or ExprSite.Selection =>
                $"`{root}` asks about octets that have not been written — {here} is evaluated while "
              + "encoding, where there is no region yet, only extents still settling",

            Room or Peek =>
                $"`{root}` can only be answered by a reader, and {here} is evaluated in both directions. "
              + "Where the two directions genuinely decide differently, say so: a choice may declare a "
              + "separate encode-side selection, and a length or a continuation is a reader's question "
              + "already",

            Kept =>
                $"`{root}` is what earlier messages left behind, and {here} is not read against a state — "
              + "only a move is",

            Inputs when site is ExprSite.Moving =>
                $"`{root}` is a value from outside the message, and {here} is read against what the "
              + "message itself said and nothing else. What a move needs to know has to have arrived in "
              + "it, or be kept by an earlier move",

            Item =>
                $"`{root}` is the structure being written, and {here} is evaluated while reading, where "
              + "there is no such thing — the structure is what is being worked out",

            Previous =>
                $"`{root}` is the structure before this one, which only an arrangement rule has",

            Position =>
                $"`{root}` is where a pointed-at node landed, and {here} points at nothing. An offset is "
              + "how a reference gets written down, not a fact a node carries — declare what this "
              + "continues at, and the offset follows",

            _ => $"`{root}` is not available in {here}",
        };
    }

    /// <summary>
    /// Every root an expression names <b>free</b> — a name the walk has to answer. A root outside
    /// <see cref="All"/> is reported separately: a misspelled <c>fields</c> is otherwise an expression
    /// that quietly reads nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <c>let</c>-bound name is a root node too, and it is emphatically not the walk's business. Taking
    /// every root would report `let n = … in n &lt; 13` as naming an unknown root `n`, which is the
    /// check being wrong about the language rather than the document being wrong about the protocol.
    /// </para>
    /// <para>
    /// This once did that reasoning itself, over <c>let</c> alone, and so was wrong about the other binder:
    /// a lambda parameter was reported as an unknown root, which made <c>map</c>, <c>fold</c> and
    /// <c>filter</c> unusable at every site this table governs. Nothing noticed because bounded iteration
    /// had only ever been written inside a transform, where the containment rule is checked by
    /// <see cref="Expr.FreeRootNames"/> — the correct version of this, which now simply <i>is</i> this.
    /// Two answers to "what does this expression need", and only one of them knew about lambdas.
    /// </para>
    /// </remarks>
    public static IEnumerable<string> RootsOf(Expr expression) => expression.FreeRootNames();
}
