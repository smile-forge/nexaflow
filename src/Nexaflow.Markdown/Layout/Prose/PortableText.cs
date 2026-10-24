using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Layout.Prose;

/// <summary>Words as pieces of a renderer-free layout tree — a run that is one piece, or a run with a piece per letter.</summary>
public static class PortableText
{
    /// <summary>What a piece laid out as its own characters is called.</summary>
    public const string SourceKind = "Source";

    /// <summary>
    /// Places a run of text as one piece with a caret position between any two of its letters, and hands back where the piece
    /// went.
    /// </summary>
    public static int Words(PortableBuilder into, IShapedText text, LayoutPoint at, double room, LayoutAlign align, ISourcePart? part,
                            string kind, bool maps = true, bool writes = false, LayoutInk? ink = null)
    {
        text.Bound(room, align);

        var piece = into.Open(kind, part, at, maps || writes ? PortableStops.Both : PortableStops.None);

        into.Draw(new PortableTextMark(text, default, ink));
        into.Words(new PortableWords(text, default, maps, writes));

        into.Close();
        return piece;
    }

    /// <summary>
    /// Places one run of text with a piece per character standing for <paramref name="letters"/>, so it can be selected through
    /// — each line set once by <paramref name="medium"/> and every letter on it read off that setting, where the run holds lines.
    /// </summary>
    public static int Place(PortableBuilder into, IShapedText text, LayoutPoint at, double room, LayoutAlign align, ISourcePart? part,
                            string kind, IReadOnlyList<ISourcePart>? letters, ILayoutMedium medium, LayoutFace face, double size)
    {
        text.Bound(room, align);

        var piece = into.Open(kind, part, at);
        into.Draw(new PortableTextMark(text, default, null));

        if (align == LayoutAlign.Left && text.Text.Contains('\n')) Lined(into, text.Text, letters, kind, medium, face, size);
        else Letters(into, text, letters, kind);

        into.Close();
        return piece;
    }

    private static void Letters(PortableBuilder into, IShapedText text, IReadOnlyList<ISourcePart>? letters, string kind)
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

    private static void Lined(PortableBuilder into, string source, IReadOnlyList<ISourcePart>? letters, string kind,
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
public static class PortableSourceShown
{
    public const string Block = "Written";
    public const string Unread = "Unread";
    public const string Reason = "Reason";

    private const double ReasonSize = 12;
    private const double ReasonGap = -3;
    private const double ReasonRoom = 260;

    /// <summary>The tree laid out as the characters it prints to, with what content inside it said marked where it stands.</summary>
    public static PortableLaid Lay(ContentPart tree, IReadOnlyList<PortableDiagnostic> trouble, ILayoutMedium medium, LayoutFace face,
                                   double size, LayoutInk ink, PortableStyle style, double room = double.PositiveInfinity)
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

        var build = new PortableBuilder(medium);
        build.Open(Block, part: null, stops: PortableStops.None);
        PortableText.Place(build, text, default, Math.Max(text.WidthWithSpace, 1), LayoutAlign.Left,
                           new SourceSpan(tree.Start, printed.Length), PortableText.SourceKind, letters, medium, face, size);

        foreach (var said in trouble)
        {
            var part = said.Part ?? new SourceSpan(said.Start, said.Length);
            var (from, length) = (said.Start - tree.Start, said.Length);
            if (length <= 0 || from < 0 || from + length > text.Text.Length) continue;

            var covered = text.Covers(default, from, length);
            if (covered.IsEmpty) continue;

            var shape = medium.Rectangle(covered);

            build.Open(Unread, part, stops: PortableStops.None);
            build.Draw(new PortableGeometryMark(shape, medium.Ink(default), null));
            build.Occupies(shape);
            build.Close();
        }

        var top = text.Height;
        foreach (var said in reasons)
        {
            top += ReasonGap;
            build.Open(Reason, part: null, new LayoutPoint(0, top), stops: PortableStops.None);
            build.Draw(new PortableTextMark(said, default, style.Danger));
            build.Close();
            top += said.Height;
        }

        build.Close();
        return new PortableLaid(build.Seal(), width, height, trouble);
    }
}
