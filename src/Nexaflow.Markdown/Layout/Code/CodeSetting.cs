using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Code;
using Nexaflow.Markdown.Layout.Lines;
using Nexaflow.Markdown.Layout.Prose;

namespace Nexaflow.Markdown.Layout.Code;

/// <summary>
/// How code is set (<see cref="CodeBuilder"/>): as everything laid line by line is (<see cref="LinedSetting"/>), in the face text shown as
/// typed is set in.
/// </summary>
public static class CodeSetting
{
    /// <summary>
    /// How tall <paramref name="tree"/>, a stretch of code, comes to laid in <paramref name="style"/> — where its lines wrap, in
    /// <paramref name="wrapsIn"/> — said without laying it (<see cref="LinedSetting.HeightOf"/>).
    /// </summary>
    public static double HeightOf(ContentNode tree, LayoutStyle style, ILayoutMedium medium, double? wrapsIn) =>
        LinedSetting.HeightOf(tree, Mono(style), medium, wrapsIn);

    /// <summary>The face and size code is set in: the one text shown as typed is set in.</summary>
    internal static LayoutCharacters Mono(LayoutStyle style) => LinedSetting.Typed(style);

    /// <summary>How far apart code's lines are set.</summary>
    internal static double LineHeight(LayoutStyle style, ILayoutMedium medium) => LinedSetting.LineHeight(Mono(style), medium);

    /// <summary>Whether <paramref name="tree"/> is a stretch of code the next stretch carries on from (<see cref="CodeKinds.CarriedOn"/>).</summary>
    internal static bool CarriedOn(ContentNode tree) => LinedSetting.CarriedOn(tree);
}
