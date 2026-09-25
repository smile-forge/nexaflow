using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// A block laid out as it is written, because what it says could not be drawn: its parse tree printed back to the characters
/// it was read from, a piece over each part the builder could not make sense of — standing for that part — and why, written
/// beneath in the error colour.
///
/// <para>
/// <strong>A builder never touches the source.</strong> It works in parse-tree parts and layout pieces, so the most it can say
/// is which parts it was given that it could not draw, and why. Turning those parts back into characters is this helper's: it
/// prints the tree, walking it as the print does to find where each blamed part's characters fall, so no builder ever needs to
/// know which stretch of text a part came from. The waves under the blamed parts are the host's, drawn from the diagnostics
/// handed back, which name the parts themselves.
/// </para>
/// </summary>
public static class SourceShown
{
    /// <summary>The whole block shown as it is written.</summary>
    public const string Block = "Written";

    /// <summary>A stretch of the printed source standing for a part that could not be drawn.</summary>
    public const string Unread = "Unread";

    /// <summary>Why, written beneath the source.</summary>
    public const string Reason = "Reason";



    private const double ReasonSize = 12;
    /// <summary>
    /// How far down from the source's last line why is written: a little up into it, since the last line's box runs on below
    /// its letters and the reason's starts above its own — so what is said reads as said of the lines just over it.
    /// </summary>
    private const double ReasonGap = -3;
    private const double ReasonRoom = 260;

    /// <summary>
    /// The tree laid out as the characters it prints to, each blamed part standing over its own and each reason written
    /// beneath — every distinct reason once, in the order first given.
    /// </summary>
    /// <param name="tree">The parse tree the block was read into.</param>
    /// <param name="blamed">Each part that could not be drawn, and why; the tree itself where the whole of it could not.</param>
    /// <param name="characters">How the block sets raw characters: its typeface and size.</param>
    public static Laid Lay(ContentPart tree, IReadOnlyList<(ContentPart Part, string Reason)> blamed, Func<string, FormattedText> characters,
                           StyleFormat style, double room = double.PositiveInfinity) =>
        Shown(tree, [.. blamed.Select(blame => Diagnostic.Of(blame.Part, blame.Reason))],
              [.. Runs(tree, blamed.Select(blame => blame.Part).ToHashSet())], characters, style, room);

    /// <summary>
    /// The tree laid out as the characters it prints to, with what was said about content read from inside it marked where it
    /// stands in it — how a host shows a block whose own content could not be drawn, fences and all.
    /// </summary>
    /// <param name="trouble">What the content inside said: each part it blamed, and why.</param>
    public static Laid Lay(ContentPart tree, IReadOnlyList<Diagnostic> trouble, Func<string, FormattedText> characters,
                           StyleFormat style, double room = double.PositiveInfinity) =>
        Shown(tree, trouble,
              [.. trouble.Select(said => (said.Part ?? new SourceSpan(said.Start, said.Length), said.Start - tree.Start, said.Length))],
              characters, style, room);

    private static Laid Shown(ContentPart tree, IReadOnlyList<Diagnostic> trouble, IReadOnlyList<(ISourcePart Part, int From, int Length)> runs,
                              Func<string, FormattedText> characters, StyleFormat style, double room)
    {
        // Up to the last line's end, not past it: a block's closing line break is where the next block starts, not a line of its own.
        var printed = tree.Print().TrimEnd('\r', '\n');
        var text = characters(printed.Length == 0 ? " " : printed);

        // What is said first, since how wide it runs is part of how wide the block is.
        var reasons = trouble.Select(said => said.Message).Where(reason => reason.Length > 0).Distinct()
            .Select(reason => new FormattedText(reason, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, style.Face(), ReasonSize, style.Danger, LayoutText.Density)
            {
                MaxTextWidth = Math.Max(1, Math.Min(Math.Max(text.WidthIncludingTrailingWhitespace, ReasonRoom), room)),
            })
            .ToList();

        var width = reasons.Select(said => said.Width).Append(text.WidthIncludingTrailingWhitespace).Max();
        var size = new Size(width, text.Height + reasons.Sum(said => ReasonGap + said.Height));

        // Only where what is drawn is what was printed does a letter stand for a character a caret can go beside.
        var letters = text.Text.Length == printed.Length
            ? (IReadOnlyList<ISourcePart>)[.. Enumerable.Range(tree.Start, printed.Length).Select(letter => (ISourcePart)new SourceSpan(letter, 1))]
            : null;

        // One piece for the whole block, holding its characters, the parts blamed in them and why. It stands for nothing itself:
        // its characters are the piece within it that does.
        var build = new LayoutBuilder();
        build.Open(Block, part: null, stops: Stops.None);
        LayoutText.Place(build, text, default, Math.Max(text.WidthIncludingTrailingWhitespace, 1), TextAlignment.Left,
                         new SourceSpan(tree.Start, printed.Length), LayoutText.SourceKind, letters);

        // A piece over each blamed part's characters, so pressing them means the part. It is drawn in nothing: the characters
        // under it are the source's, and the wave under them the host's.
        foreach (var (part, from, length) in runs)
        {
            if (length <= 0 || from < 0 || from + length > text.Text.Length || text.BuildHighlightGeometry(default, from, length) is not { } covered) continue;
            covered.Freeze();

            build.Open(Unread, part, stops: Stops.None);
            build.Draw(new GeometryMark(covered, Brushes.Transparent, null, 0));
            build.Occupies(covered);
            build.Close();
        }

        var top = text.Height;
        foreach (var said in reasons)
        {
            top += ReasonGap;
            build.Open(Reason, part: null, new Point(0, top), stops: Stops.None);
            build.Draw(new TextMark(said, default, style.Danger));
            build.Close();
            top += said.Height;
        }

        build.Close();
        return new Laid(build.Seal(), size, trouble);
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
