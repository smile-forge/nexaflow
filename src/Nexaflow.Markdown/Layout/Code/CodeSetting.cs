using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Code;
using Nexaflow.Markdown.Layout.Prose;

namespace Nexaflow.Markdown.Layout.Code;

/// <summary>
/// How code is set (<see cref="CodeBuilder"/>): in what face and size, how far apart its lines are, and so how tall a stretch of it
/// comes to — said without laying it: from its lines, and where they wrap (<see cref="LayoutHost.WrapsLines"/>), from the rows they
/// break onto with every character set at the face's advance.
/// </summary>
public static class CodeSetting
{
    /// <summary>
    /// How tall <paramref name="tree"/>, a stretch of code, comes to laid in <paramref name="style"/>: a line height for each line, and
    /// one for the line after the last line end, unless the next stretch carries on from it — or, where its lines wrap, one for each row
    /// they break onto (<see cref="CodeRows"/>). Counted from its characters where they are held, so none of them is copied to say it.
    /// </summary>
    /// <param name="wrapsIn">The room its lines wrap in, or null where they are kept whole.</param>
    public static double HeightOf(ContentNode tree, LayoutStyle style, ILayoutMedium medium, double? wrapsIn)
    {
        var carriedOn = CarriedOn(tree);
        var rows = wrapsIn is { } room ? Rows(tree, room, Advances(style, medium), carriedOn) : Lines(tree, carriedOn);

        return Math.Max(rows, 1) * LineHeight(style, medium);
    }

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
        var line = new CodeRows(room);
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

    /// <summary>How far along the face code is set in moves for a character, and for a tab.</summary>
    private static (double Character, double Tab) Advances(LayoutStyle style, ILayoutMedium medium)
    {
        var mono = Mono(style);
        var measure = medium.Measuring(mono.Face, mono.Size);

        return (measure.Of(" ").Advance, measure.Of("\t").Advance);
    }

    /// <summary>The face and size code is set in.</summary>
    internal static LayoutCharacters Mono(LayoutStyle style) => new(style.Face(style.MonoFont), Math.Max(1, style.TextSize * 0.94), style.Text);

    /// <summary>How far apart code's lines are set.</summary>
    internal static double LineHeight(LayoutStyle style, ILayoutMedium medium)
    {
        var mono = Mono(style);

        return medium.Shape(" ", mono.Face, mono.Size, style.Text).Height;
    }

    /// <summary>Whether <paramref name="tree"/> is a stretch of code the next stretch carries on from (<see cref="CodeKinds.CarriedOn"/>).</summary>
    internal static bool CarriedOn(ContentNode tree) => tree.Children.Any(child => child.IsDerived && child.Kind == CodeKinds.CarriedOn);
}
