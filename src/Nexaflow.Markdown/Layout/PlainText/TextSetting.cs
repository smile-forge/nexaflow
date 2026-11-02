using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Layout.Lines;
using Nexaflow.Markdown.Layout.Prose;

namespace Nexaflow.Markdown.Layout.PlainText;

/// <summary>How plain text is set (<see cref="TextBuilder"/>): as everything laid line by line is (<see cref="LinedSetting"/>), in the face text shown as typed is set in.</summary>
public static class TextSetting
{
    /// <summary>The face and size text is set in.</summary>
    public static LayoutCharacters Characters(LayoutStyle style) => LinedSetting.Typed(style);

    /// <summary>
    /// How tall <paramref name="tree"/>, a stretch of text, comes to laid in <paramref name="style"/> — where its lines wrap, in
    /// <paramref name="wrapsIn"/> — said without laying it (<see cref="LinedSetting.HeightOf"/>).
    /// </summary>
    public static double HeightOf(ContentNode tree, LayoutStyle style, ILayoutMedium medium, double? wrapsIn) =>
        LinedSetting.HeightOf(tree, Characters(style), medium, wrapsIn);

    /// <summary>How far apart text's lines are set.</summary>
    public static double LineHeight(LayoutStyle style, ILayoutMedium medium) => LinedSetting.LineHeight(Characters(style), medium);
}
