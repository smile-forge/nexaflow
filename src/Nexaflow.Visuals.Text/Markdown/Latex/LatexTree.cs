using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Visuals.Text.Editing;
using XamlMath;

using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using Size = System.Windows.Size;

namespace Nexaflow.Visuals.Text.Markdown.Latex;

/// <summary>
/// A typeset formula's layout tree, and the offset-based questions an editor asks of it: where to draw a
/// caret and what shape to give it, what a click means, what a drag selected, where an arrow key goes,
/// and which command produced the glyph you just pressed backspace behind.
/// <para>
/// Almost nothing is decided here. The rules live in <see cref="LayoutQuery"/>, over
/// <see cref="ILayoutNode"/>, so they are shared with every other kind of embedded rendered content and
/// can be exercised against a hand-built tree. What remains in this class is the translation between
/// source offsets — which is how an editor thinks — and the nodes the query works in, plus the two rules
/// that are genuinely about LaTeX: how far past the edges a click still counts, and that backspace never
/// un-renders the entire formula.
/// </para>
/// <para>
/// It is deliberately free of the typesetter. Producing the tree needs fonts, and fonts need WPF and a
/// desktop; asking it questions needs neither.
/// </para>
/// </summary>
public sealed class LatexTree
{
    /// <summary>Rounding slack — two edges within this of each other are treated as touching.</summary>
    private const double Hair = 0.5;

    private readonly int[] _stops;

    /// <param name="latex">The source the tree refers into.</param>
    /// <param name="root">The formula's whole layout, parents holding children.</param>
    /// <param name="size">The formula's painted size.</param>
    public LatexTree(string latex, ILayoutNode root, Size size, IReadOnlyList<Diagnostic>? trouble = null)
    {
        Latex = latex ?? string.Empty;
        Root = root;
        Size = size;
        Diagnostics = trouble ?? [];
        _stops = [.. root.CaretStops()];
    }

    /// <summary>The source this tree was built from.</summary>
    public string Latex { get; }

    /// <summary>The formula's whole layout.</summary>
    public ILayoutNode Root { get; }

    /// <summary>The formula's painted size in element pixels.</summary>
    public Size Size { get; }

    /// <summary>
    /// The stretches the typesetter could not read. Empty for a formula that parsed cleanly; otherwise
    /// each names a piece that is shown as written rather than understood.
    /// </summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    /// <summary>
    /// Whether this piece was shown rather than read — inside a stretch the parser gave up on, so it
    /// stands for characters rather than for meaning.
    /// </summary>
    public bool IsGuesswork(ILayoutNode node) => Diagnostics.Any(d => d.Covers(node));

    /// <summary>
    /// What this piece is <em>to</em> the construct holding it — the degree of a root, a fraction's
    /// numerator, a script's base — together with that construct. Null when nothing holds it, or when it
    /// came from source the parser could not read.
    /// <para>
    /// The answer comes from the parse tree rather than from the layout, because that is where it is
    /// true: a <c>3</c> is the degree of a root whether or not it was ever drawn. It is what makes copying
    /// a piece able to carry more than the characters — copy the 3 out of <c>\sqrt[3]{x+1}</c> and you
    /// have a 3, and also "the degree of a root", and only the second reading lets pasting it onto
    /// something else produce a cube root of that something.
    /// </para>
    /// </summary>
    public (ILayoutNode Construct, string Role)? RoleOf(ILayoutNode node)
    {
        if (node is not LatexNode { Formula: { } mine }) return null;

        // Recovered text was shown, not read. Whatever the parser wrapped it in while carrying on is an
        // artefact of the recovery rather than anything the writer expressed, so it names no part of
        // anything — and copying it can only ever yield the characters.
        if (IsGuesswork(node)) return null;

        // Climbing rather than taking the nearest ancestor: the typesetter wraps things in boxes of its
        // own, and those carry the same construct or none at all. The first ancestor that actually holds
        // this among its parts is the one that named it.
        foreach (var ancestor in node.Ancestors())
        {
            if (ancestor is not LatexNode { Formula: { } theirs } || ReferenceEquals(theirs, mine)) continue;

            foreach (var slot in theirs.Slots)
                // The construct comes back as its layout node, not as the parse node the role was read
                // off. Spans then have one owner: the layout's, which has claimed each command's
                // backslash, where the parser's span begins at the command's name.
                if (ReferenceEquals(slot.Node, mine)) return (ancestor, slot.Role);
        }

        return null;
    }

    /// <summary>Where a caret is allowed to rest, ascending.</summary>
    public IReadOnlyList<int> CaretStops => _stops;

    /// <summary>
    /// The holes in this formula, in reading order — the arguments left empty, which the typesetter
    /// drew a box for. Tab walks these.
    /// </summary>
    public IReadOnlyList<ILayoutNode> Placeholders =>
        _placeholders ??= [.. Root.SelfAndDescendants().Where(n => n.IsPlaceholder()).OrderBy(n => n.SourceStart)];

    private IReadOnlyList<ILayoutNode>? _placeholders;

    // ── Point → source ──────────────────────────────────────────────────────

    /// <summary>
    /// The caret stop <paramref name="point"/> means. The deepest piece under the pointer wins, and which
    /// half of it was hit decides whether the caret lands before or after.
    /// </summary>
    /// <summary>
    /// The piece of the formula <paramref name="point"/> is on — what a drag is really between. Past
    /// either side it is the piece at that end, as it is in text.
    /// </summary>
    public ILayoutNode? NodeAt(Point point)
    {
        if (point.X > Size.Width) return Root.Ink().LastOrDefault();
        if (point.X < 0) return Root.Ink().FirstOrDefault();
        return Root.NodeAt(point);
    }

    public int OffsetAt(Point point)
    {
        // Clicking past either side means the end, as it does in text. Letting the nearest node answer
        // would instead land just inside whatever construct happens to finish last — after the y of
        // `\sqrt{y}` rather than after the radical — which is the same pixel but a different place to type.
        if (point.X > Size.Width) return Latex.Length;
        if (point.X < 0) return 0;

        var hit = Root.NodeAt(point);
        if (hit is null) return 0;

        var offset = point.X < hit.Bounds.X + hit.Bounds.Width / 2 ? hit.SourceStart : hit.SourceEnd();
        return NearestStop(offset);
    }

    // ── Source → geometry ───────────────────────────────────────────────────

    /// <summary>
    /// Where and how tall to draw the caret at <paramref name="offset"/> — the piece it abuts decides,
    /// which is what makes it shrink and rise inside an exponent and take the numerator's height inside
    /// a fraction.
    /// </summary>
    public Rect CaretRect(int offset) => Root.CaretRect(offset);

    /// <summary>
    /// The rectangles to wash for the source range — one per run, already merged, so a translucent
    /// selection never paints the same pixel twice and darkens it.
    /// </summary>
    public IReadOnlyList<Rect> RangeRects(int start, int length)
    {
        if (length <= 0) return [];
        var end = start + length;

        // Wash whole nodes, not the glyphs inside them. A node's bounds cover the parts of it that no
        // character produced — a fraction's bar, a radical's hook — which washing the leaves alone would
        // leave clear, so a fully selected fraction would read as two selected numbers with a gap.
        // Holes included, which is why this asks whether a node stands for a place rather than whether
        // it covers any characters. A hole covers none by definition, so a selection sweeping across a
        // half-written fraction would wash everything except the part still missing — the one piece the
        // reader most needs to see they have picked up.
        var covered = Root.SelfAndDescendants()
            .Where(n => n.Stands() && n.SourceStart >= start && n.SourceEnd() <= end)
            .ToHashSet();

        return Merge([.. covered.Where(n => !n.Ancestors().Any(covered.Contains)).Select(n => n.Bounds)]);
    }

    /// <summary>
    /// Collapses the rectangles of one contiguous source range into as few as possible: anything sharing
    /// a vertical band becomes one run.
    /// <para>
    /// Horizontal gaps are closed deliberately rather than preserved. The selection is a contiguous run
    /// of source, so whatever sits in the gap is inside it — the glue TeX puts around a binary operator,
    /// say — and leaving those unpainted would break one selection into a row of disconnected blocks.
    /// Stacked bands (a sum's limits above and below it) stay separate unless something spans them.
    /// </para>
    /// </summary>
    private static List<Rect> Merge(List<Rect> rects)
    {
        var merged = true;
        while (merged)
        {
            merged = false;
            for (var i = 0; i < rects.Count && !merged; i++)
                for (var j = i + 1; j < rects.Count && !merged; j++)
                {
                    var a = rects[i];
                    var b = rects[j];
                    if (a.Top >= b.Bottom - Hair || b.Top >= a.Bottom - Hair) continue;   // different bands

                    a.Union(b);
                    rects[i] = a;
                    rects.RemoveAt(j);
                    merged = true;
                }
        }
        return rects;
    }

    // ── Ranges and stepping ─────────────────────────────────────────────────

    /// <summary>
    /// The whole constructs a raw range covers, as a source range. Dragging across <c>x^2</c> selects the
    /// script rather than stopping mid-command at <c>x^{2</c>, and dragging from a fraction's numerator to
    /// its denominator selects the fraction rather than the <c>1}{x</c> the offsets alone would give.
    /// <para>
    /// Both fall out of promotion: the answer is made of whole nodes, and a node's source range is what
    /// the parser built it from, so it cannot be a half-open brace.
    /// </para>
    /// </summary>
    public (int Start, int Length) SnapRange(int start, int length)
    {
        var from = Math.Clamp(Math.Min(start, start + length), 0, Latex.Length);
        var to = Math.Clamp(Math.Max(start, start + length), 0, Latex.Length);
        if (from == to) return (from, 0);

        // Whatever lies wholly inside the range was dragged over — every node, not only the ink, so a
        // drag from before a root's sign to past its contents takes the root itself and not merely what
        // is under the bar. Anything only half inside is left to promotion, which is what stops a range
        // that clipped a brace of `^{n}` from coming back as `{n`.
        var touched = Root.SelfAndDescendants()
            .Where(n => n.SourceLength > 0 && n.SourceStart >= from && n.SourceEnd() <= to)
            .ToList();

        if (touched.Count == 0)
        {
            // The range covers only characters nothing was drawn for — a lone brace, say, or half of a
            // command's name. Snap to whatever is nearest, so a selection is always of something visible.
            var nearest = Root.Ink()
                .OrderBy(n => Math.Min(Math.Abs(n.SourceStart - from), Math.Abs(n.SourceEnd() - to)))
                .FirstOrDefault();
            if (nearest is null) return (from, to - from);
            touched.Add(nearest);
        }

        var promoted = LayoutQuery.Promote(touched);
        if (promoted.Count == 0) return (from, to - from);

        return (promoted.Min(n => n.SourceStart), promoted.Max(n => n.SourceEnd()) - promoted.Min(n => n.SourceStart));
    }

    /// <summary>
    /// The next caret stop in <paramref name="forward"/>'s direction, or null at the formula's edge —
    /// which is the host's cue to move the caret out into the surrounding text.
    /// </summary>
    public int? Step(int offset, bool forward) => Root.Step(offset, forward);

    /// <summary>
    /// The nearest caret stop on the line above or below — how the caret crosses a fraction bar or drops
    /// out of an exponent. Null when there is nothing that way.
    /// </summary>
    public int? StepVertical(int offset, bool up) => Root.StepVertical(offset, up);

    /// <summary>Moves <paramref name="offset"/> to the nearest place a caret may rest.</summary>
    public int NearestStop(int offset)
    {
        var best = _stops.Length > 0 ? _stops[0] : 0;
        var bestDistance = int.MaxValue;
        foreach (var stop in _stops)
        {
            var distance = Math.Abs(stop - offset);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = stop;
        }
        return best;
    }

    /// <summary>
    /// The construct drawn immediately before <paramref name="offset"/>, when it took more than one
    /// character to write it. This is what backspace un-renders: the caret sits after an <c>α</c> whose
    /// source is the six characters <c>\alpha</c>, and deleting one of those six is the wrong move.
    /// </summary>
    /// <summary>
    /// The construct's argument ending exactly at <paramref name="offset"/>, when the writer left its
    /// braces off — what typing there has to extend rather than walk out of.
    /// <para>
    /// LaTeX lets a one-token argument go unbraced, so <c>x^2</c> is x to the 2. Type a 3 meaning
    /// twenty-three and the characters say something else entirely: <c>x^23</c> is x squared followed
    /// by a 3, because the exponent was one token and that token has been used. Naming the case here is
    /// what lets a caller re-brace it, which is the only way the obvious keystroke means the obvious
    /// thing.
    /// </para>
    /// <para>
    /// Only the roles that are genuinely an argument the writer wrote after a command. A construct's
    /// <c>base</c> is not one — <c>x</c> in <c>x^2</c> — and neither is an <c>element</c> of a row: the
    /// <c>1</c> in <c>a + 1</c> plays a part in the row that holds it, so it has a role, but a 2 typed
    /// after it is twelve and bracing it would say nothing at all.
    /// </para>
    /// </summary>
    /// <summary>
    /// Writes <paramref name="text"/> in at <paramref name="caret"/> as a change to the construct that
    /// owns that position, rather than to the characters either side of it.
    /// <para>
    /// This is the difference between editing a formula and editing a string that happens to be one.
    /// A 3 typed after <c>x^2</c> means twenty-three; spliced in it says <c>x^23</c>, which is x
    /// squared followed by a 3, because LaTeX lets a one-token argument go unbraced and that token has
    /// been used. The tree knows which construct the caret is in and what its argument may hold, so it
    /// is the tree that re-braces — and the same call also covers writing at the <em>front</em> of an
    /// argument, inside one already braced, and outside any construct at all, none of which the caller
    /// should have to tell apart.
    /// </para>
    /// <para>
    /// Returns null when the position belongs to no construct in particular. Then the text is simply
    /// inserted, which the caller does itself: typing a character carries rules of its own — a
    /// backslash opens a command that is shown as it is spelled — and those are about the source,
    /// not the structure.
    /// </para>
    /// </summary>
    public LatexWrite? Write(int caret, string text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        caret = Math.Clamp(caret, 0, Latex.Length);
        if (ArgumentAt(caret) is not { } argument) return null;

        var (start, end) = (argument.SourceStart, argument.SourceEnd());
        if (start < 0 || end > Latex.Length || caret < start || caret > end) return null;

        // Already wrapped, or still one token: nothing structural to do, and the caller's own rules
        // should have the keystroke.
        if (IsBraced(argument) || IsOneToken(Latex[start..caret] + text + Latex[caret..end])) return null;

        return Place(Latex, caret, text, (start, end), braced: false);
    }

    /// <summary>
    /// Moves the stretches in <paramref name="ranges"/> to <paramref name="to"/> — a term dragged to a
    /// new place in the formula.
    /// <para>
    /// Merged into where it lands rather than dropped there. It is the same question typing asks, with
    /// more riding on it: a term dragged into an unbraced exponent has to brace it, or <c>x^2</c> and a
    /// dropped 3 would read as x squared beside a 3; and a command dragged against a letter has to keep
    /// a space, or <c>\alpha</c> next to <c>x</c> silently becomes the unknown command
    /// <c>\alphax</c>. Both are facts about the structure, so both are settled here.
    /// </para>
    /// <para>
    /// Null when there is nothing to move, or when the drop is inside what is being moved — a term
    /// dropped on itself has not gone anywhere, and cutting it first would leave nowhere to put it.
    /// </para>
    /// </summary>
    public LatexWrite? Move(IReadOnlyList<(int Start, int Length)> ranges, int to)
    {
        if (ranges is null) return null;

        var ordered = ranges.Where(r => r.Length > 0 && r.Start >= 0 && r.Start + r.Length <= Latex.Length)
                            .OrderBy(r => r.Start)
                            .ToList();
        if (ordered.Count == 0) return null;
        if (ordered.Any(r => to > r.Start && to < r.Start + r.Length)) return null;

        var moved = string.Concat(ordered.Select(r => Latex.Substring(r.Start, r.Length)));

        // Cut last first, so removing one stretch never moves the offsets of those still to go — the
        // same reason a disjoint selection can be deleted at all.
        var remainder = Latex;
        foreach (var range in Enumerable.Reverse(ordered)) remainder = remainder.Remove(range.Start, range.Length);

        // Everything cut from in front of the drop shifts it back by that much.
        var drop = to - ordered.Where(r => r.Start + r.Length <= to).Sum(r => r.Length);
        drop = Math.Clamp(drop, 0, remainder.Length);

        // The destination is read off the formula as it stands, which is the one the reader dropped
        // onto, then shifted into the source the cut left behind.
        var argument = ArgumentAt(to);
        var span = argument is null
            ? ((int Start, int End)?)null
            : (Shift(argument.SourceStart, ordered), Shift(argument.SourceEnd(), ordered));

        return Place(remainder, drop, moved, span, argument is not null && IsBraced(argument));

        static int Shift(int offset, List<(int Start, int Length)> cut) =>
            offset - cut.Where(r => r.Start + r.Length <= offset).Sum(r => r.Length);
    }

    /// <summary>
    /// Puts <paramref name="text"/> into <paramref name="source"/> at <paramref name="at"/>, wrapping
    /// the argument it lands in when that argument can no longer hold it bare.
    /// </summary>
    private static LatexWrite Place(string source, int at, string text,
                                    (int Start, int End)? argument, bool braced)
    {
        var written = Separated(source, at, text);

        // The caret always ends up just past what was written, so where that landed follows from it —
        // including when wrapping the argument shifted the whole lot along by a brace.
        if (argument is not { } span || braced
            || IsOneToken(source[span.Start..at] + written + source[at..span.End]))
            return Wrote(source.Insert(at, written), at + written.Length, written.Length);

        var content = source[span.Start..at] + written + source[at..span.End];
        return Wrote(
            source[..span.Start] + "{" + content + "}" + source[span.End..],
            span.Start + 1 + (at - span.Start) + written.Length,
            written.Length);
    }

    private static LatexWrite Wrote(string latex, int caret, int length) =>
        new(latex, caret, new LatexRange(caret - length, length));

    /// <summary>
    /// <paramref name="text"/> with a space added at either end where the join would otherwise change
    /// what the neighbouring characters say — a control word runs on until something that is not a
    /// letter ends its name, so <c>\alpha</c> written against <c>x</c> would become <c>\alphax</c>.
    /// </summary>
    private static string Separated(string source, int at, string text)
    {
        if (text.Length == 0) return text;

        var before = EndsWithControlWord(source[..at]) && char.IsLetter(text[0]) ? " " : string.Empty;
        var after = EndsWithControlWord(text) && at < source.Length && char.IsLetter(source[at]) ? " " : string.Empty;
        return before + text + after;
    }

    private static bool EndsWithControlWord(string text)
    {
        var i = text.Length;
        while (i > 0 && char.IsLetter(text[i - 1])) i--;
        return i < text.Length && i > 0 && text[i - 1] == '\\';
    }

    private ILayoutNode? ArgumentAt(int caret)
    {
        ILayoutNode? best = null;
        foreach (var node in Root.SelfAndDescendants())
        {
            if (node.SourceLength <= 0) continue;

            // Edges included: writing at either end of an argument is writing in it. That is the whole
            // question — the position after the 2 of x^2 is both "the end of the exponent" and "the end
            // of the formula", and only the construct that owns it can say which.
            if (caret < node.SourceStart || caret > node.SourceEnd()) continue;
            if (RoleOf(node) is not { } role || !IsArgument(role.Role)) continue;

            // The innermost, because an argument can hold constructs with arguments of their own.
            if (best is null || node.Ancestors().Count() > best.Ancestors().Count()) best = node;
        }
        return best;
    }

    /// <summary>Roles a construct gives to something written as its argument.</summary>
    private static bool IsArgument(string role) =>
        role is "superscript" or "subscript" or "numerator" or "denominator"
             or "degree" or "radicand" or "over" or "under";

    /// <summary>Whether the writer already wrapped this argument, in which case it can hold anything.</summary>
    private bool IsBraced(ILayoutNode node) =>
        node.SourceStart > 0 && Latex[node.SourceStart - 1] == '{'
        && node.SourceEnd() < Latex.Length && Latex[node.SourceEnd()] == '}';

    /// <summary>
    /// Whether <paramref name="content"/> is one token, and so can stand as an argument bare. A single
    /// character is; so is a whole control word, which is why <c>x^\alpha</c> needs no braces.
    /// </summary>
    private static bool IsOneToken(string content) =>
        content.Length == 1
        || (content.Length > 1 && content[0] == '\\' && content.Skip(1).All(char.IsLetter));

    /// <summary>
    /// The one thing immediately before <paramref name="offset"/> — what backspace acts on.
    /// <para>
    /// The deepest piece ending exactly there, which is the symbol the reader is looking at rather than
    /// whatever encloses it. After the 2 of <c>x^2</c> that is the 2, not the script; after the closing
    /// brace of a fraction there is nothing deeper, so it is the fraction.
    /// </para>
    /// </summary>
    public ILayoutNode? SymbolBefore(int offset)
    {
        ILayoutNode? best = null;
        foreach (var node in Root.SelfAndDescendants())
        {
            if (!node.Stands() || node.SourceEnd() != offset) continue;

            // Never a run of things. A row is not an item — it is however many items, each of which is
            // one — so it is never "the thing before the caret" however exactly it happens to end
            // there. Without this, a caret sitting after something that drew nothing (a thin space,
            // say) found no symbol ending where it stood and climbed until it reached whatever
            // contained them all: in a two-line align block, backspace un-rendered both equations.
            if (IsSequence(node)) continue;

            // Never the formula as a whole: backspace behind the last symbol would otherwise take
            // everything the reader had written, which is not what one keystroke should mean.
            if (node.SourceStart <= 0 && node.SourceEnd() >= Latex.Length) continue;
            if (best is null || node.Ancestors().Count() > best.Ancestors().Count()) best = node;
        }

        return best is null ? null : Owning(best);
    }

    /// <summary>
    /// The thing a piece belongs to, when it is not a thing in its own right.
    /// <para>
    /// A delimiter is drawn by the fence that holds it rather than being a part of it — the same
    /// category as a fraction's bar, and it names no role for the same reason: it is not a place
    /// content goes. But unlike a bar it is not decoration either. A bracket carries meaning only as a
    /// pair; one without its partner cannot be read at all, so nothing may point at, take, carry or
    /// delete it alone. Pointing at one means the group.
    /// </para>
    /// </summary>
    public ILayoutNode Owning(ILayoutNode node)
    {
        var owner = node;

        // Only ever into something that is a construct and claims parts of its own. Climbing on the
        // strength of the piece naming no role is not enough: where nothing names a role — a tree with
        // no parse behind it — every step would qualify and this would walk to the top and hand back
        // the whole formula.
        while (RoleOf(owner) is null && owner.Parent is { } parent
               && IsComposite(parent) && !IsSequence(parent))
            owner = parent;

        return owner;
    }

    /// <summary>
    /// Whether this piece is made of parts — a construct rather than a symbol.
    /// <para>
    /// The question backspace turns on. A construct can be taken back to the source it was written as,
    /// because there is source to go back to: <c>\frac{a}{b}</c> is a command and three braces the
    /// reader typed and can no longer see. A symbol has nothing hidden behind it — an α is an α however
    /// many letters spelled it — so backspace simply takes it, which is what pressing backspace over
    /// one thing has always meant.
    /// </para>
    /// </summary>
    public static bool IsComposite(ILayoutNode node) =>
        // The node and whatever wraps it without covering any more of the source. The typesetter boxes
        // a construct inside boxes of its own, and it is not always the innermost of them that carries
        // the parse node — but they all stand for the same characters, so they are all the same thing
        // as far as the reader is concerned, and any of them knowing it has parts settles it.
        SameSpan(node).Any(n => n is LatexNode { Formula: { } parsed } && parsed.Slots.Count > 0);

    /// <summary>
    /// Whether this piece is a run of things rather than one thing.
    /// <para>
    /// A row names every part <c>element</c>, because that is the only thing a sequence can say about
    /// what is in it — where a construct names its parts <c>numerator</c>, <c>radicand</c>,
    /// <c>superscript</c>, each meaning something to the construct holding it. So the roles already
    /// carry the distinction between "one thing made of parts" and "several things in a row", and it
    /// does not have to be guessed at from spans or sizes.
    /// </para>
    /// </summary>
    private static bool IsSequence(ILayoutNode node) =>
        SameSpan(node).Any(n => n is LatexNode { Formula: { } parsed }
                                && parsed.Slots.Count > 0
                                && parsed.Slots.All(slot => slot.Role == "element"));

    private static IEnumerable<ILayoutNode> SameSpan(ILayoutNode node) =>
        new[] { node }.Concat(node.Ancestors()
            .TakeWhile(a => a.SourceStart == node.SourceStart && a.SourceLength == node.SourceLength));
}
