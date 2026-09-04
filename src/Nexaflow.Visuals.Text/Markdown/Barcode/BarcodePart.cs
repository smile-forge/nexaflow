using System.Collections.Generic;
using System.Linq;

namespace Nexaflow.Visuals.Text.Markdown.Barcode;

/// <summary>What a piece of a drawn symbol is.</summary>
public enum BarcodeKind
{
    /// <summary>The whole symbol — the root, standing for the whole value.</summary>
    Symbol,

    /// <summary>The line above the bars naming the number the symbol stands for.</summary>
    Caption,

    /// <summary>The modules themselves.</summary>
    Bars,

    /// <summary>A stretch of bar running down past the digits: a retail symbol's start, centre and end.</summary>
    Guard,

    /// <summary>A printed run of the number, sitting in the well its half of the symbol leaves for it.</summary>
    Group,

    /// <summary>One printed character that is a character of the value, and can be edited as one.</summary>
    Character,

    /// <summary>
    /// Printed characters worked out from the value rather than taken from it: a computed check digit, a
    /// number with its hyphens stripped, a scheme's name.
    /// <para>
    /// It carries the stretch of the value it was worked out from, so a reader can point at it, be told
    /// what it stands for, and copy that. It has no children, because there is nothing inside it to point
    /// at separately — and that is what keeps a caret out of it, the shared query offering a stop only
    /// where something holds a place in the source.
    /// </para>
    /// </summary>
    EncodedText,

    /// <summary>The small symbol printed beside the main one — a price or an issue number.</summary>
    AddOn,
}

/// <summary>
/// What a part of a symbol is to the thing holding it. Open string constants rather than an enum, so a
/// new symbology can name a piece without editing a type every reader switches over.
/// </summary>
public static class BarcodeRole
{
    public const string Element = "element";
    public const string Caption = "caption";
    public const string Scheme = "scheme";
    public const string Number = "number";
    public const string Bars = "bars";
    public const string Guard = "guard";
    public const string Label = "label";
    public const string AddOn = "add-on";
}

/// <summary>
/// One piece of a barcode as it is understood: what it prints, which characters of the value it came
/// from, and which modules encode it.
///
/// <para>
/// This is the symbol's structure, not its picture. It says that an EAN-13 prints its number in three
/// groups with guards between them, that the thirteenth digit is worked out rather than typed, and that
/// a publication's caption is a scheme's name followed by the number as written. Where any of that lands
/// on the page is the builder's business.
/// </para>
/// <para>
/// <b>Positions are into the value</b> — what an author typed, and what an edit would splice back — never
/// into the encoded text and never into pixels. A piece that prints something the value does not contain
/// is <see cref="BarcodeKind.EncodedText"/>, and says which stretch of the value produced it rather than
/// claiming characters it has not got. That distinction is the whole point of the type: for most of these
/// formats what is drawn and what was written are different strings, and a piece that pretends otherwise
/// is a piece an edit cannot round-trip through.
/// </para>
/// </summary>
public sealed class BarcodePart
{
    private readonly List<BarcodePart> _children = [];

    private BarcodePart(BarcodeKind kind, string role, string text,
                        int start, int length, (int Start, int Length) modules)
    {
        Kind = kind;
        Role = role;
        Text = text;
        Start = start;
        Length = length;
        Modules = modules;
    }

    public BarcodeKind Kind { get; }

    /// <summary>What this is to the piece holding it — a <see cref="BarcodeRole"/> constant.</summary>
    public string Role { get; }

    /// <summary>What it prints, or empty for a piece that is structure rather than text.</summary>
    public string Text { get; }

    /// <summary>Where it begins in the value.</summary>
    public int Start { get; }

    /// <summary>
    /// How many characters of the value it covers. For <see cref="BarcodeKind.EncodedText"/> that is the
    /// stretch it was <em>worked out from</em> rather than the stretch it prints, which is the honest
    /// answer to "what would I be editing, if I could edit this".
    /// </summary>
    public int Length { get; }

    /// <summary>Which modules encode it, or a zero length when it does not sit on the bars at all.</summary>
    public (int Start, int Length) Modules { get; }

    /// <summary>
    /// Where the format prints this run against the bars. A property of the symbology rather than of the
    /// picture — an EAN-13's first digit sits outside the bars because there is no stretch of bar encoding
    /// it, and that is true wherever the symbol is drawn and at whatever size.
    /// </summary>
    public BarcodeTextPlacement Placement { get; private set; } = BarcodeTextPlacement.Below;

    public BarcodePart? Parent { get; private set; }

    public IReadOnlyList<BarcodePart> Children => _children;

    /// <summary>One past the last character of the value it covers.</summary>
    public int End => Start + Length;

    /// <summary>Whether this is a character of the value, and so somewhere a caret can stand.</summary>
    public bool IsSource => Kind == BarcodeKind.Character;

    public IEnumerable<BarcodePart> Ancestors()
    {
        for (var up = Parent; up is not null; up = up.Parent) yield return up;
    }

    public IEnumerable<BarcodePart> SelfAndDescendants()
    {
        yield return this;
        foreach (var child in _children)
            foreach (var deeper in child.SelfAndDescendants())
                yield return deeper;
    }

    /// <summary>
    /// The value as this tree accounts for it: every character it claims came from the source, in order.
    /// Where that is not the whole value, the rest was not printed — an ISBN's hyphens — or was printed
    /// as something worked out instead.
    /// </summary>
    public string Written() =>
        string.Concat(SelfAndDescendants().Where(p => p.IsSource).OrderBy(p => p.Start).Select(p => p.Text));

    // ── Building ───────────────────────────────────────────────────────────

    /// <summary>A piece with nothing inside it.</summary>
    public static BarcodePart Leaf(BarcodeKind kind, string role, string text,
                                   int start, int length, (int Start, int Length) modules = default) =>
        new(kind, role, text, start, length, modules);

    /// <summary>A piece made of others, covering whatever they between them cover.</summary>
    public static BarcodePart Branch(BarcodeKind kind, string role, IEnumerable<BarcodePart> children,
                                     (int Start, int Length) modules = default)
    {
        var inside = children.ToList();

        // Its span is its children's, taken together. One holding nothing that came from the source covers
        // nothing, rather than covering the gap where its children would have been.
        var claimed = inside.Where(c => c.Length > 0).ToList();
        var start = claimed.Count == 0 ? 0 : claimed.Min(c => c.Start);
        var length = claimed.Count == 0 ? 0 : claimed.Max(c => c.End) - start;

        var part = new BarcodePart(kind, role, string.Empty, start, length, modules);
        foreach (var child in inside) part.Adopt(child);
        return part;
    }

    /// <summary>The root: the whole symbol, standing for the whole value however little of it is printed.</summary>
    public static BarcodePart Symbol(string value, IEnumerable<BarcodePart> children)
    {
        var part = new BarcodePart(BarcodeKind.Symbol, BarcodeRole.Element, value, 0, value.Length, default);
        foreach (var child in children) part.Adopt(child);
        return part;
    }

    /// <summary>
    /// A piece made of others that covers a stretch of the value in its own right, whatever its children
    /// do or do not claim.
    /// <para>
    /// The bars are the case this exists for. They encode the whole value, and the guards inside them
    /// encode none of it — they are the format's own punctuation — so taking the span from the children
    /// would leave the one part of the symbol that really does stand for everything standing for nothing,
    /// and a press on a bar would resolve to no source at all.
    /// </para>
    /// </summary>
    public static BarcodePart Spanning(BarcodeKind kind, string role, int start, int length,
                                       IEnumerable<BarcodePart> children,
                                       (int Start, int Length) modules = default)
    {
        var part = new BarcodePart(kind, role, string.Empty, start, length, modules);
        foreach (var child in children) part.Adopt(child);
        return part;
    }

    /// <summary>
    /// Reads one printed run against the value it is supposed to have come from.
    /// <para>
    /// This is the one place the correspondence between what is drawn and what was typed is decided, and
    /// it is <em>checked</em> rather than assumed: a run is characters of the value only when it really is
    /// <c>value.Substring(at, run.Length)</c>. Anything else — a check digit nobody typed, a number with
    /// its hyphens taken out, a scheme's name — comes back as one <see cref="BarcodeKind.EncodedText"/>
    /// standing for the whole value.
    /// </para>
    /// <para>
    /// So a format cannot quietly come to claim that it is editable. One added later that transforms its
    /// input fails this on its first run and is drawn as encoded, with nobody having to remember to put it
    /// on a list.
    /// </para>
    /// </summary>
    /// <param name="at">Where in the value this run is expected to start, or negative for "nowhere".</param>
    public static BarcodePart Read(string role, string run, string value, int at,
                                   (int Start, int Length) modules = default,
                                   BarcodeTextPlacement placement = BarcodeTextPlacement.Below)
    {
        if (run.Length == 0) return Placed(Leaf(BarcodeKind.Group, role, run, 0, 0, modules), placement);

        var verbatim = at >= 0
                    && at + run.Length <= value.Length
                    && string.CompareOrdinal(value, at, run, 0, run.Length) == 0;

        if (!verbatim)
            return Placed(Leaf(BarcodeKind.EncodedText, role, run, 0, value.Length, modules), placement);

        // One node per character, because a character of the value is the smallest thing a caret can stand
        // beside and a selection can take. The modules are shared out evenly between them, which is true of
        // every symbology here that prints a digit over the bars encoding it.
        var each = (double)modules.Length / run.Length;
        var characters = Enumerable.Range(0, run.Length).Select(i => Leaf(
            BarcodeKind.Character,
            BarcodeRole.Element,
            run[i].ToString(),
            at + i,
            1,
            ((int)(modules.Start + i * each), (int)each)));

        return Placed(Branch(BarcodeKind.Group, role, characters, modules), placement);
    }

    /// <summary>Says where a run and everything in it is printed.</summary>
    private static BarcodePart Placed(BarcodePart part, BarcodeTextPlacement placement)
    {
        foreach (var piece in part.SelfAndDescendants()) piece.Placement = placement;
        return part;
    }

    private void Adopt(BarcodePart child)
    {
        child.Parent = this;
        _children.Add(child);
    }

    public override string ToString() =>
        Kind + ":" + Role + "[" + Start + "," + Length + "]" + (Text.Length > 0 ? " " + Text : "");
}
