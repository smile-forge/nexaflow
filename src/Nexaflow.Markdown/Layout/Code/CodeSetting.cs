using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Code;
using Nexaflow.Markdown.Layout.Prose;

namespace Nexaflow.Markdown.Layout.Code;

/// <summary>
/// How code is set (<see cref="CodeBuilder"/>): in what face and size, how far apart its lines are, and so how tall a stretch of it
/// comes to — which, since nothing wraps, is said from its lines without laying it.
/// </summary>
public static class CodeSetting
{
    /// <summary>
    /// How tall <paramref name="tree"/>, a stretch of code, comes to laid in <paramref name="style"/>: a line height for each line, and
    /// one for the line after the last line end, unless the next stretch carries on from it.
    /// </summary>
    public static double HeightOf(ContentNode tree, LayoutStyle style, ILayoutMedium medium)
    {
        var lines = CarriedOn(tree) ? 0 : 1;

        foreach (var leaf in tree.Leaves())
            lines += leaf.Text.AsSpan().Count('\n');

        return Math.Max(lines, 1) * LineHeight(style, medium);
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
