using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Barcode;

/// <summary>What a piece of a symbol's text is.</summary>
public enum BarcodeKind
{
    /// <summary>The whole symbol — the root, standing for the whole value.</summary>
    Symbol,

    /// <summary>The line above the bars naming the number the symbol stands for.</summary>
    Caption,

    /// <summary>A run of the printed number, made of the pieces below.</summary>
    Group,

    /// <summary>One printed character that is a character of the value, and can be edited as one.</summary>
    Character,

    /// <summary>
    /// Printed characters worked out from the value rather than taken from it: a check digit, a Codabar
    /// start or stop mark, a scheme's name, a number with its hyphens stripped.
    /// <para>
    /// It carries the stretch of the value it was worked out from, so a reader can point at it, be told
    /// what it stands for, and copy that. It has no characters of its own to point at separately, which
    /// is what keeps a caret out of it.
    /// </para>
    /// </summary>
    EncodedText,
}

/// <summary>
/// What a part of a symbol is to the thing holding it. Open string constants rather than an enum, so a
/// new symbology can name a piece without editing a type every reader switches over.
/// </summary>
public static class BarcodeRole
{
    public const string Element = "element";
    public const string Caption = "caption";
    public const string Label = "label";
    public const string AddOn = "add-on";
}

/// <summary>
/// One piece of a barcode's text as it is understood: what it prints, and which characters of the value
/// it came from.
///
/// <para>
/// This is what the symbol <em>says</em>, not what it looks like. <b>It is deliberately much smaller than
/// the layout built from it.</b> The bars and their guard patterns have no part here at all — no piece of
/// what an author typed is a guard; the guards are how the value is drawn, and drawing is the layout's
/// business. What is here is the text, because text is the only part of a barcode where the question
/// "which characters of the value is this" has an answer.
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
public sealed class BarcodePart : ISourcePart
{
    private readonly List<BarcodePart> _children = [];

    private BarcodePart(BarcodeKind kind, string role, string text, int start, int length)
    {
        Kind = kind;
        Role = role;
        Text = text;
        Start = start;
        Length = length;
    }

    public BarcodeKind Kind { get; }

    /// <summary>What this is to the piece holding it — a <see cref="BarcodeRole"/> constant.</summary>
    public string Role { get; }

    /// <summary>What it prints, or empty for a piece made of others.</summary>
    public string Text { get; }

    /// <summary>Where it begins in the value.</summary>
    public int Start { get; }

    /// <summary>
    /// How many characters of the value it covers. For <see cref="BarcodeKind.EncodedText"/> that is the
    /// stretch it was <em>worked out from</em> rather than the stretch it prints, which is the honest
    /// answer to "what would I be editing, if I could edit this".
    /// </summary>
    public int Length { get; }

    public BarcodePart? Parent { get; private set; }

    public IReadOnlyList<BarcodePart> Children => _children;

    /// <summary>One past the last character of the value it covers.</summary>
    public int End => Start + Length;

    /// <summary>Whether this is a character of the value, and so somewhere a caret can stand.</summary>
    public bool IsSource => Kind == BarcodeKind.Character;

    /// <summary>What this piece puts on the page: its own text, or its children's taken together.</summary>
    public string Printed => Text.Length > 0 ? Text : string.Concat(_children.Select(c => c.Printed));

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
    /// Where that is not the whole value, the rest was not printed — an ISBN's hyphens — or was printed as
    /// something worked out instead.
    /// </summary>
    public string Written() =>
        string.Concat(SelfAndDescendants().Where(p => p.IsSource).OrderBy(p => p.Start).Select(p => p.Text));

    // ── Building ───────────────────────────────────────────────────────────

    public static BarcodePart Leaf(BarcodeKind kind, string role, string text, int start, int length) =>
        new(kind, role, text, start, length);

    /// <summary>A piece made of others, covering whatever they between them cover.</summary>
    public static BarcodePart Branch(BarcodeKind kind, string role, IEnumerable<BarcodePart> children)
    {
        var inside = children.ToList();

        // Its span is its children's, taken together. One holding nothing that came from the source covers
        // nothing, rather than covering the gap where its children would have been.
        var claimed = inside.Where(c => c.Length > 0).ToList();
        var start = claimed.Count == 0 ? 0 : claimed.Min(c => c.Start);
        var length = claimed.Count == 0 ? 0 : claimed.Max(c => c.End) - start;

        var part = new BarcodePart(kind, role, string.Empty, start, length);
        foreach (var child in inside) part.Adopt(child);
        return part;
    }

    /// <summary>The root: the whole symbol, standing for the whole value however little of it is printed.</summary>
    public static BarcodePart Symbol(string value, IEnumerable<BarcodePart> children)
    {
        var part = new BarcodePart(BarcodeKind.Symbol, BarcodeRole.Element, value, 0, value.Length);
        foreach (var child in children) part.Adopt(child);
        return part;
    }

    /// <summary>
    /// Reads one printed run against the value, given where the value was found inside the whole printed
    /// number.
    ///
    /// <para>
    /// Most of these formats print the value with something of their own on one end or both: Codabar puts
    /// a start and a stop mark around it, a UPC-E given six digits puts a number system in front and works
    /// out a check digit to go behind, an EAN-13 given twelve adds the thirteenth. What is printed is then
    /// one string to look at and three to reason about — a piece nobody typed, the value, and another
    /// piece nobody typed — and only the middle one is a thing an edit can be applied to.
    /// </para>
    /// <para>
    /// So a run is cut against that window rather than judged whole. A run inside it is characters of the
    /// value; one outside it is <see cref="BarcodeKind.EncodedText"/>; and one that straddles an edge —
    /// an EAN-13's last group, which is five typed digits and then the check digit — becomes both, in the
    /// order they are printed.
    /// </para>
    /// </summary>
    /// <param name="run">What this run prints.</param>
    /// <param name="at">Where the run begins in the whole printed number.</param>
    /// <param name="window">
    /// Where the value sits inside the printed number, and how long the value is. <c>At</c> is negative
    /// when the value is nowhere in it — an ISBN's de-hyphenated digits, a Code 39's upper-cased letters,
    /// a Pharmacode's dropped leading zero — and then nothing printed is the value, though what is printed
    /// still stands for the whole of it.
    /// </param>
    public static BarcodePart Read(string role, string run, int at, (int At, int Length) window)
    {
        if (run.Length == 0) return Leaf(BarcodeKind.Group, role, run, 0, 0);

        // Where this run overlaps the value, in the printed number's own indices.
        var from = Math.Max(at, window.At);
        var to = Math.Min(at + run.Length, window.At + window.Length);

        if (window.At < 0 || to <= from)
            return Leaf(BarcodeKind.EncodedText, role, run, 0, window.Length);

        var pieces = new List<BarcodePart>();

        Generated(at, from);

        // The value's own characters. One node each, because a character of the value is the smallest
        // thing a caret can stand beside and a selection can take.
        for (var p = from; p < to; p++)
            pieces.Add(Leaf(BarcodeKind.Character, BarcodeRole.Element, run[p - at].ToString(), p - window.At, 1));

        Generated(to, at + run.Length);

        return Branch(BarcodeKind.Group, role, pieces);

        // A stretch of the run that nobody typed. It stands for the whole value, because that is what it
        // was worked out from — a check digit is a fact about all of the digits, not about the one it
        // happens to be printed next to.
        void Generated(int start, int end)
        {
            if (end <= start) return;
            pieces.Add(Leaf(BarcodeKind.EncodedText, role, run[(start - at)..(end - at)], 0, window.Length));
        }
    }

    private void Adopt(BarcodePart child)
    {
        child.Parent = this;
        _children.Add(child);
    }

    public override string ToString() =>
        Kind + ":" + Role + "[" + Start + "," + Length + "]" + (Text.Length > 0 ? " " + Text : "");
}
