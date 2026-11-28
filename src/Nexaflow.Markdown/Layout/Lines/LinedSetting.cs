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
    /// each row they break onto (<see cref="LineRows"/>), a tab reaching to the next of the stops <paramref name="tabWidth"/> characters
    /// apart. Counted from its characters where they are held, so none of them is copied to say it.
    /// </summary>
    /// <param name="wrapsIn">The room its lines wrap in, or null where they are kept whole.</param>
    public static double HeightOf(ContentNode tree, LayoutCharacters characters, ILayoutMedium medium, double? wrapsIn, int tabWidth, double pixel)
    {
        var carriedOn = CarriedOn(tree);
        var rows = wrapsIn is { } room ? Rows(tree, room, Advance(characters, medium), tabWidth, carriedOn) : Lines(tree, carriedOn);

        return Math.Max(rows, 1) * LineHeight(characters, medium, pixel);
    }

    /// <summary>What a mark standing for a space, a tab or a line end is called, and the ink it is drawn in is asked by.</summary>
    public const string WhitespaceKind = "whitespace";

    /// <summary>What the mark after a line a fold starts on is called.</summary>
    public const string FoldKind = "fold";

    /// <summary>
    /// How far a tab standing <paramref name="along"/> across a line reaches: to the next tab stop, the stops standing
    /// <paramref name="tabWidth"/> characters of <paramref name="advance"/> apart from the line's start.
    /// </summary>
    public static double TabAcross(double along, double advance, int tabWidth)
    {
        var stop = Math.Max(1, tabWidth) * advance;
        if (stop <= 0) return 0;

        // A place a hair short of a stop, from adding up advances, is at it.
        var stops = Math.Floor((along / stop) + 1e-9);

        return ((stops + 1) * stop) - along;
    }

    /// <summary>
    /// How far apart lines set in <paramref name="characters"/> stand: the face's own line height, to the nearest whole number of
    /// <paramref name="pixel"/>s, and never less than one — so the lines of a long stretch are drawn evenly down the screen, every one the
    /// same number of pixels below the last.
    /// </summary>
    public static double LineHeight(LayoutCharacters characters, ILayoutMedium medium, double pixel)
    {
        var tall = medium.Shape(" ", characters.Face, characters.Size, characters.Ink).Height;
        return pixel > 0 ? Math.Max(1, Math.Round(tall / pixel, MidpointRounding.AwayFromZero)) * pixel : tall;
    }

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
    /// advance and a tab reaching to the next tab stop, which is what a monospaced face sets them at, said without measuring each — and the
    /// line after the last line end, unless the next stretch carries on from it.
    /// </summary>
    private static int Rows(ContentNode tree, double room, double advance, int tabWidth, bool carriedOn)
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
                    line.Next(character, character == '\t' ? TabAcross(line.Along, advance, tabWidth)
                                       : character == '\r' || char.IsLowSurrogate(character) ? 0 : advance);
                }

        return carriedOn ? rows : rows + 1 + line.Breaks;
    }

    /// <summary>How far along the face <paramref name="characters"/> are set in moves for a character.</summary>
    private static double Advance(LayoutCharacters characters, ILayoutMedium medium) =>
        medium.Measuring(characters.Face, characters.Size).Of(" ").Advance;
}
