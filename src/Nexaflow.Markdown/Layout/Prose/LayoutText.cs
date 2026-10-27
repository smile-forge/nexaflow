using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Layout.Prose;

/// <summary>Words as pieces of a renderer-free layout tree — a run that is one piece, or a run with a piece per letter.</summary>
public static class LayoutText
{
    /// <summary>What a piece laid out as its own characters is called.</summary>
    public const string SourceKind = "Source";

    /// <summary>
    /// Places a run of text as one piece with a caret position between any two of its letters, and hands back where the piece
    /// went.
    /// </summary>
    /// <param name="degrees">
    /// How far the run is turned about where it starts — nought for the ordinary case. A turned run reaches where its letters land
    /// rather than where they were set, and stands in the letters themselves, so a press on it means the letter under the pointer.
    /// </param>
    public static int Words(LayoutBuilder into, IShapedText text, LayoutPoint at, double room, LayoutAlign align, ISourcePart? part,
                            string kind, bool maps = true, bool writes = false, LayoutInk? ink = null, double degrees = 0)
    {
        text.Bound(room, align);

        var turn = degrees == 0 ? null : new LayoutPaint([into.Medium.Rotation(degrees, default)]);

        // A run that says something about a piece of source rather than showing it is nowhere to put a caret.
        var piece = into.Open(kind, part, at, maps || writes ? LayoutStops.Both : LayoutStops.None, gathers: turn is null, paints: turn);

        into.Draw(new TextMark(text, default, ink));
        into.Words(new LayoutWords(text, default, maps, writes));

        if (turn is not null)
        {
            var letters = new LayoutRect(0, 0, text.Width, text.Height);
            into.Covers(LayoutRect.Transform(letters, turn.Turn![0].Matrix));
            into.Occupies(into.Medium.Rectangle(letters));
        }

        into.Close();
        return piece;
    }

    /// <summary>The kind of piece a hole is drawn as — see <see cref="Hole"/>.</summary>
    public const string HoleKind = "Hole";

    /// <summary>How far a hole runs across for how far up it runs: squat enough to read as a slot, as a formula's does.</summary>
    private const double HoleAspect = 0.55 / 0.62;

    /// <summary>How wide a hole standing among letters like <paramref name="letter"/> is drawn.</summary>
    public static double HoleWidth(IShapedText letter) => letter.Extent * HoleAspect;

    /// <summary>
    /// Places a hole: the hollow box standing where something is still to be written. Stands for no characters — its part is empty,
    /// sitting where they will go — so it is somewhere to put the caret, and what is typed at it lands where it stands. As tall as a
    /// small letter and resting on the line, so it reads as a letter still to come.
    /// </summary>
    /// <param name="letter">A small letter in the type it stands among: its ink is how tall the box is, and its baseline where it sits.</param>
    public static int Hole(LayoutBuilder into, ISourcePart hole, LayoutPoint at, IShapedText letter, LayoutInk ink)
    {
        var height = letter.Extent;
        var width = HoleWidth(letter);
        var hairline = Math.Max(height * 0.07, 0.75);

        var box = into.Medium.Rectangle(new LayoutRect(hairline / 2, letter.Baseline - height + (hairline / 2),
                                                       Math.Max(width - hairline, 0), Math.Max(height - hairline, 0)));

        var piece = into.Open(HoleKind, hole, at);
        into.Draw(new GeometryMark(box, null, into.Medium.Stroke(ink, hairline)));
        into.Covers(new LayoutRect(0, 0, width, letter.Height));
        into.Close();
        return piece;
    }

    /// <summary>
    /// Places one run of text with a piece per character standing for <paramref name="letters"/>, so it can be selected through
    /// — each line set once by <paramref name="medium"/> and every letter on it read off that setting, where the run holds lines.
    /// </summary>
    /// <param name="lined">
    /// Whether a run of several lines has each line set once and its letters read off that, rather than every letter found in the
    /// whole run — false for words that hold no line breaks to speak of, a title over a score.
    /// </param>
    public static int Place(LayoutBuilder into, IShapedText text, LayoutPoint at, double room, LayoutAlign align, ISourcePart? part,
                            string kind, IReadOnlyList<ISourcePart>? letters, ILayoutMedium medium, LayoutFace face, double size,
                            bool lined = true)
    {
        text.Bound(room, align);

        var piece = into.Open(kind, part, at);
        into.Draw(new TextMark(text, default, null));

        if (lined && align == LayoutAlign.Left && text.Text.Contains('\n')) Lined(into, text.Text, letters, kind, medium, face, size);
        else Letters(into, text, letters, kind);

        into.Close();
        return piece;
    }

    private static void Letters(LayoutBuilder into, IShapedText text, IReadOnlyList<ISourcePart>? letters, string kind)
    {
        if (letters is null || letters.Count != text.Text.Length) return;

        for (var i = 0; i < letters.Count; i++)
        {
            var bounds = text.Covers(default, i, 1);
            if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) continue;

            into.Open(kind + "-letter", letters[i], bounds.TopLeft);
            into.Covers(new LayoutRect(0, 0, bounds.Width, bounds.Height));
            into.Close();
        }
    }

    private static void Lined(LayoutBuilder into, string source, IReadOnlyList<ISourcePart>? letters, string kind,
                              ILayoutMedium medium, LayoutFace face, double size)
    {
        if (letters is null || letters.Count != source.Length) return;

        double blank;
        using (var space = medium.Line(" ", face, size)) blank = space.Height;

        var top = 0.0;

        for (var start = 0; start <= source.Length;)
        {
            var end = source.IndexOf('\n', start);
            if (end < 0) end = source.Length;

            var shown = end > start && source[end - 1] == '\r' ? end - 1 : end;
            if (shown == start)
            {
                top += blank;
                start = end + 1;
                continue;
            }

            using var line = medium.Line(source[start..shown], face, size);

            for (var at = start; at < shown; at++)
            {
                var bounds = line.Letter(at - start);
                if (bounds.IsEmpty) continue;

                into.Open(kind + "-letter", letters[at], new LayoutPoint(bounds.X, bounds.Y + top));
                into.Covers(new LayoutRect(0, 0, bounds.Width, bounds.Height));
                into.Close();
            }

            top += line.Height;
            start = end + 1;
        }
    }
}

/// <summary>A block laid out as it is written, because what it says could not be drawn — see the WPF layout's own.</summary>
public static class SourceShown
{
    public const string Block = "Written";
    public const string Unread = "Unread";
    public const string Reason = "Reason";

    private const double ReasonSize = 12;
    private const double ReasonGap = -3;
    private const double ReasonRoom = 260;

    /// <summary>
    /// The tree laid out as the characters it prints to, each blamed part standing over its own and each reason written beneath — every
    /// distinct reason once, in the order first given.
    /// </summary>
    /// <param name="blamed">Each part that could not be drawn, and why; the tree itself where the whole of it could not.</param>
    public static Laid Lay(ContentPart tree, IReadOnlyList<(ContentPart Part, string Reason)> blamed, ILayoutMedium medium,
                                   LayoutFace face, double size, LayoutInk ink, LayoutStyle style, double room = double.PositiveInfinity) =>
        Shown(tree, [.. blamed.Select(blame => LayoutDiagnostic.Of(blame.Part, blame.Reason))],
              Runs(tree, blamed.Select(blame => blame.Part).ToHashSet()), medium, face, size, ink, style, room);

    /// <summary>The tree laid out as the characters it prints to, with what content inside it said marked where it stands.</summary>
    public static Laid Lay(ContentPart tree, IReadOnlyList<LayoutDiagnostic> trouble, ILayoutMedium medium, LayoutFace face,
                                   double size, LayoutInk ink, LayoutStyle style, double room = double.PositiveInfinity) =>
        Shown(tree, trouble, [.. trouble.Select(said => (said.Part ?? new SourceSpan(said.Start, said.Length), said.Start - tree.Start, said.Length))],
              medium, face, size, ink, style, room);

    private static Laid Shown(ContentPart tree, IReadOnlyList<LayoutDiagnostic> trouble,
                                      IReadOnlyList<(ISourcePart Part, int From, int Length)> runs, ILayoutMedium medium, LayoutFace face,
                                      double size, LayoutInk ink, LayoutStyle style, double room)
    {
        var printed = tree.Print().TrimEnd('\r', '\n');
        var text = medium.Shape(printed.Length == 0 ? " " : printed, face, size, ink);

        var reasons = new List<IShapedText>();
        foreach (var reason in trouble.Select(said => said.Message).Where(reason => reason.Length > 0).Distinct())
        {
            var said = medium.Shape(reason, style.Face(style.TextFont), ReasonSize, style.Danger);
            said.Bound(Math.Max(1, Math.Min(Math.Max(text.WidthWithSpace, ReasonRoom), room)), LayoutAlign.Left);
            reasons.Add(said);
        }

        var width = reasons.Select(said => said.Width).Append(text.WidthWithSpace).Max();
        var height = text.Height + reasons.Sum(said => ReasonGap + said.Height);

        var letters = text.Text.Length == printed.Length
            ? (IReadOnlyList<ISourcePart>)[.. Enumerable.Range(tree.Start, printed.Length).Select(letter => (ISourcePart)new SourceSpan(letter, 1))]
            : null;

        var build = new LayoutBuilder(medium);
        build.Open(Block, part: null, stops: LayoutStops.None);
        LayoutText.Place(build, text, default, Math.Max(text.WidthWithSpace, 1), LayoutAlign.Left,
                           new SourceSpan(tree.Start, printed.Length), LayoutText.SourceKind, letters, medium, face, size);

        foreach (var (part, from, length) in runs)
        {
            if (length <= 0 || from < 0 || from + length > text.Text.Length) continue;

            if (medium.Covering(text, default, from, length) is not { } shape) continue;

            build.Open(Unread, part, stops: LayoutStops.None);
            build.Draw(new GeometryMark(shape, medium.Ink(default), null));
            build.Occupies(shape);
            build.Close();
        }

        var top = text.Height;
        foreach (var said in reasons)
        {
            top += ReasonGap;
            build.Open(Reason, part: null, new LayoutPoint(0, top), stops: LayoutStops.None);
            build.Draw(new TextMark(said, default, style.Danger));
            build.Close();
            top += said.Height;
        }

        build.Close();
        return new Laid(build.Seal(), width, height, trouble);
    }

    /// <summary>Where each blamed part's characters fall in what the tree prints — found by walking it as the print does.</summary>
    private static List<(ISourcePart Part, int From, int Length)> Runs(ContentPart tree, IReadOnlySet<ContentPart> blamed)
    {
        var runs = new List<(ISourcePart, int, int)>();
        var at = 0;

        void Walk(ContentPart part)
        {
            if (part.Derived) return;

            if (blamed.Contains(part))
            {
                var length = part.Print().Length;
                runs.Add((part, at, length));
                at += length;
                return;
            }

            if (part.Node.IsLeaf)
            {
                at += part.Node.Text.Length;
                return;
            }

            foreach (var child in part.Children) Walk(child);
        }

        Walk(tree);
        return runs;
    }
}
