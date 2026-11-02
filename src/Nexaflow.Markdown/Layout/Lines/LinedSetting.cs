using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Markdown.Lines;

namespace Nexaflow.Markdown.Layout.Lines;

/// <summary>
/// How content laid line by line is set (<see cref="LinedBuilder"/>) — code, text shown as typed: in a monospaced face, line under line,
/// so how tall a stretch of it comes to is said without laying it — from its lines, and where they wrap (<see cref="LayoutHost.WrapsLines"/>),
/// from the rows they break onto with every character set at the face's advance.
/// </summary>
public static class LinedSetting
{
    /// <summary>The face and size content shown as typed is set in, in <paramref name="style"/>: its monospaced face, a little under its text.</summary>
    public static LayoutCharacters Typed(LayoutStyle style) => new(style.Face(style.MonoFont), Math.Max(1, style.TextSize * 0.94), style.Text);

    /// <summary>
    /// How tall <paramref name="tree"/>, a stretch laid line by line, comes to set in <paramref name="characters"/>: a line height for each
    /// line, and one for the line after the last line end, unless the next stretch carries on from it — or, where its lines wrap, one for
    /// each row they break onto (<see cref="LineRows"/>). Counted from its characters where they are held, so none of them is copied to say it.
    /// </summary>
    /// <param name="wrapsIn">The room its lines wrap in, or null where they are kept whole.</param>
    public static double HeightOf(ContentNode tree, LayoutCharacters characters, ILayoutMedium medium, double? wrapsIn)
    {
        var carriedOn = CarriedOn(tree);
        var rows = wrapsIn is { } room ? Rows(tree, room, Advances(characters, medium), carriedOn) : Lines(tree, carriedOn);

        return Math.Max(rows, 1) * LineHeight(characters, medium);
    }

    /// <summary>How far apart lines set in <paramref name="characters"/> are.</summary>
    public static double LineHeight(LayoutCharacters characters, ILayoutMedium medium) =>
        medium.Shape(" ", characters.Face, characters.Size, characters.Ink).Height;

    /// <summary>Whether <paramref name="tree"/> is a stretch the next stretch carries on from (<see cref="LineKinds.CarriedOn"/>).</summary>
    public static bool CarriedOn(ContentNode tree) => tree.Children.Any(child => child.IsDerived && child.Kind == LineKinds.CarriedOn);

    /// <summary>How many lines <paramref name="tree"/> holds: one more than it has line ends, unless the next stretch carries on from its last.</summary>
    private static int Lines(ContentNode tree, bool carriedOn)
    {
        var lines = carriedOn ? 0 : 1;

        foreach (var leaf in tree.Leaves())
            lines += leaf.TimesHeld('\n');

        return lines;
    }

    /// <summary>
    /// How many rows the lines of <paramref name="tree"/> break onto in <paramref name="room"/>: every character set at the face's
    /// advance and a tab at its own, which is what a monospaced face sets them at, said without measuring each — and the line after the
    /// last line end, unless the next stretch carries on from it.
    /// </summary>
    private static int Rows(ContentNode tree, double room, (double Character, double Tab) advance, bool carriedOn)
    {
        var line = new LineRows(room);
        var rows = 0;

        foreach (var leaf in tree.Leaves())
            foreach (var run in leaf.CharacterRuns())
                foreach (var character in run.Span)
                {
                    if (character == '\n')
                    {
                        rows += 1 + line.Breaks;
                        line.NewLine();
                        continue;
                    }

                    // A return before a line feed is the line end's, and the second half of a pair stands for no character of its own.
                    line.Next(character, character == '\t' ? advance.Tab : character == '\r' || char.IsLowSurrogate(character) ? 0 : advance.Character);
                }

        return carriedOn ? rows : rows + 1 + line.Breaks;
    }

    /// <summary>How far along the face <paramref name="characters"/> are set in moves for a character, and for a tab.</summary>
    private static (double Character, double Tab) Advances(LayoutCharacters characters, ILayoutMedium medium)
    {
        var measure = medium.Measuring(characters.Face, characters.Size);

        return (measure.Of(" ").Advance, measure.Of("\t").Advance);
    }
}
