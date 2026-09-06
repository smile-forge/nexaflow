using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Music.Abc.Stages;

/// <summary>
/// Says what is written correctly and still cannot be drawn.
///
/// <para>
/// A different question from "can this be read", which the parser already answered by holding what it
/// could not make sense of. This one is asked of whatever is going to engrave the tune, because what can
/// be drawn is a fact about a builder and this is a reader — the same split the LaTeX side settled by
/// asking the builder rather than the symbol tables, after a command the builder set perfectly well was
/// shown in red because the tables had never heard of it.
/// </para>
/// <para>
/// Marking a piece leaves it the piece it was. A decoration nothing can draw is still a decoration, and a
/// voice overlay is still an overlay; what is added is something to say about it, which is what puts a
/// line under it and a reason in the tooltip.
/// </para>
/// </summary>
/// <param name="draws">
/// Whether the engraver has a drawing for a named decoration, given the name as written between its
/// exclamation marks. Everything is drawable when nothing is asked.
/// </param>
public sealed class CheckDrawable(Func<string, bool>? draws = null) : IAstStage
{
    public string Name => "abc:drawable";

    public ContentNode Run(ContentNode tree) => AstRewrite.Each(tree, Check);

    private ContentNode Check(ContentNode node) => node.Kind switch
    {
        AbcKinds.Note when node.Part(AbcRoles.Letter) is null && node.Width > 0 =>
            node.Saying("this alters nothing — there is no note after it"),

        AbcKinds.Decoration when Named(node.Text) is { } name && draws is not null && !draws(name) =>
            node.Saying($"there is no {name} to draw"),

        AbcKinds.Overlay =>
            node.Saying("a voice overlay is read and not engraved"),

        _ => node,
    };

    /// <summary>The name inside a <c>!…!</c> decoration, or null for the one-character shorthands.</summary>
    private static string? Named(string text) =>
        text.Length > 2 && text[0] == '!' && text[^1] == '!' ? text[1..^1] : null;
}
