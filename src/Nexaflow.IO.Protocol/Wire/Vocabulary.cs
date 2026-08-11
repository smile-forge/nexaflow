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

    /// <summary>Everything the walk binds. A root outside this set is not vocabulary at all.</summary>
    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.Ordinal)
            { Fields, Inputs, Item, Previous, Room, Peek, Carried, Ordinal };

    private static readonly Dictionary<ExprSite, HashSet<string>> Answerable = new()
    {
        [ExprSite.Value] = [Fields, Inputs, Item, Carried, Ordinal],
        [ExprSite.Length] = [Fields, Room, Peek, Carried, Ordinal],
        [ExprSite.Bound] = [Fields, Room, Peek, Carried, Ordinal],
        [ExprSite.Continuation] = [Fields, Room, Peek, Carried, Ordinal],
        [ExprSite.Seeding] = [Fields, Carried, Ordinal],
        [ExprSite.Carry] = [Fields, Carried, Ordinal],
        [ExprSite.Discriminator] = [Fields, Carried, Ordinal],
        [ExprSite.Recognition] = [Fields, Room, Peek, Carried, Ordinal],
        [ExprSite.Selection] = [Fields, Inputs, Item, Carried, Ordinal],
        [ExprSite.Condition] = [Fields, Carried, Ordinal],
        [ExprSite.Pairing] = [Item, Previous],
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
        ExprSite.Pairing => "an arrangement rule",
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

            Inputs or Item when site is ExprSite.Length or ExprSite.Bound or ExprSite.Continuation =>
                $"`{root}` is what the caller supplied, and {here} is evaluated while decoding, where "
              + "there is no caller — only a packet",

            Inputs or Item =>
                $"`{root}` is what the caller supplied and {here} is evaluated in both directions; a "
              + "reader has a packet and no caller",

            Previous =>
                $"`{root}` is the structure before this one, which only an arrangement rule has",

            _ => $"`{root}` is not available in {here}",
        };
    }

    /// <summary>
    /// Every root an expression names <b>free</b> — a name the walk has to answer. A root outside
    /// <see cref="All"/> is reported separately: a misspelled <c>fields</c> is otherwise an expression
    /// that quietly reads nothing.
    /// </summary>
    /// <remarks>
    /// A <c>let</c>-bound name is a root node too, and it is emphatically not the walk's business. Taking
    /// every root would report `let n = … in n &lt; 13` as naming an unknown root `n`, which is the
    /// check being wrong about the language rather than the document being wrong about the protocol.
    /// </remarks>
    public static IEnumerable<string> RootsOf(Expr expression)
    {
        var bound = expression.Descendants().OfType<Expr.Let>().Select(l => l.Name)
                              .ToHashSet(StringComparer.Ordinal);

        return expression.Descendants().OfType<Expr.Root>().Select(r => r.Name)
                         .Where(name => !bound.Contains(name))
                         .Distinct(StringComparer.Ordinal);
    }
}
