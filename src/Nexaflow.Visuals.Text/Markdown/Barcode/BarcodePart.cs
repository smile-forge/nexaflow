using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Markdown.Ast;

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
    /// Printed characters worked out from the value rather than taken from it (a check digit, a Codabar
    /// start/stop mark, a scheme name, hyphens stripped). Carries the value stretch it was derived from,
    /// but no characters of its own — keeps it out of the caret's stops.
    /// </summary>
    EncodedText,
}

/// <summary>What a part of a symbol is to the thing holding it. String constants, not an enum, so a new symbology can name a piece without editing a type every reader switches over.</summary>
public static class BarcodeRole
{
    public const string Element = "element";
    public const string Caption = "caption";
    public const string Label = "label";
    public const string AddOn = "add-on";
}

/// <summary>
/// One piece of a barcode's text: what it prints, and which characters of the value it came from. Deliberately
/// smaller than the layout — bars/guards carry no part, since drawing is the layout's business and text is
/// the only place "which characters of the value is this" has an answer.
///
/// <para>
/// Positions are into the value — the <em>n</em>th character of it, which is the <em>n</em>th character piece of the tree it was
/// read from (<see cref="BarcodeBlock.Characters"/>) — never into the encoded text or pixels.
/// A piece printing something the value doesn't contain is <see cref="BarcodeKind.EncodedText"/>, tagged
/// with the value stretch it was derived from rather than claiming characters it hasn't got — for most
/// formats, drawn text and written text are different strings, and a piece that pretends otherwise can't
/// round-trip an edit.
/// </para>
/// </summary>
public sealed class BarcodePart
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

    /// <summary>How many characters of the value it covers. For <see cref="BarcodeKind.EncodedText"/> this is the stretch it was worked out from, not the stretch it prints.</summary>
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

    /// <summary>The value as this tree accounts for it — every character claimed as source, in order. Falls short of the whole value where characters (e.g. ISBN hyphens) weren't printed, or were printed as something worked out instead.</summary>
    public string Written() =>
        string.Concat(SelfAndDescendants().Where(p => p.IsSource).OrderBy(p => p.Start).Select(p => p.Text));

    // ── Building ───────────────────────────────────────────────────────────

    public static BarcodePart Leaf(BarcodeKind kind, string role, string text, int start, int length) =>
        new(kind, role, text, start, length);

    /// <summary>A piece made of others, covering whatever they between them cover.</summary>
    public static BarcodePart Branch(BarcodeKind kind, string role, IEnumerable<BarcodePart> children)
    {
        var inside = children.ToList();

        // A branch with no source-derived children covers nothing, not the gap where they'd have been.
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
    /// Reads one printed run against the value's window (where a stretch of the value was found in what's printed).
    /// Most formats add something of their own to one or both ends (a Codabar start/stop mark, UPC-E's
    /// number system digit, EAN-13's check digit), so a run is cut against the window: characters inside
    /// it are <see cref="BarcodeKind.Character"/>, outside it <see cref="BarcodeKind.EncodedText"/>, and
    /// one straddling an edge (e.g. EAN-13's last group: five typed digits then the check digit) becomes both.
    /// </summary>
    /// <param name="run">What this run prints.</param>
    /// <param name="at">Where the run begins in the whole printed number.</param>
    /// <param name="window">
    /// Where a stretch of the value sits in the printed number (<c>At</c>), where it begins in the value
    /// (<c>From</c>), and its length. <c>At</c> is negative when none of the value is in it (e.g. ISBN's
    /// stripped hyphens, Pharmacode's dropped leading zero).
    /// </param>
    /// <param name="whole">How long the whole value is — what a piece nobody typed was worked out from.</param>
    public static BarcodePart Read(string role, string run, int at, (int At, int From, int Length) window, int whole)
    {
        if (run.Length == 0) return Leaf(BarcodeKind.Group, role, run, 0, 0);

        // Where this run overlaps the value, in the printed number's own indices.
        var from = Math.Max(at, window.At);
        var to = Math.Min(at + run.Length, window.At + window.Length);

        if (window.At < 0 || to <= from)
            return Leaf(BarcodeKind.EncodedText, role, run, 0, whole);

        var pieces = new List<BarcodePart>();

        Generated(at, from);

        // One node per character — the smallest thing a caret can stand beside.
        for (var p = from; p < to; p++)
            pieces.Add(Leaf(BarcodeKind.Character, BarcodeRole.Element, run[p - at].ToString(),
                            window.From + (p - window.At), 1));

        Generated(to, at + run.Length);

        return Branch(BarcodeKind.Group, role, pieces);

        // Generated text stands for the whole value (e.g. a check digit is a fact about all the digits).
        void Generated(int start, int end)
        {
            if (end <= start) return;
            pieces.Add(Leaf(BarcodeKind.EncodedText, role, run[(start - at)..(end - at)], 0, whole));
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
