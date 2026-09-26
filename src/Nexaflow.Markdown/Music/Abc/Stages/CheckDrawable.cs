using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Music.Abc.Stages;

/// <summary>
/// Says what is written correctly and still cannot be drawn.
///
/// <para>
/// A different question from "can this be read", which the parser already answered by holding what it
/// could not make sense of. A decoration names a mark the engraver draws only where <see cref="ResolveMarks"/> found one in the
/// vocabulary every notation's marks are said in (<see cref="MusicMark"/>), and every mark in it is drawn; a decoration it found
/// nothing for is spelled correctly and names nothing there is to draw.
/// </para>
/// <para>
/// Marking a piece leaves it the piece it was. A decoration nothing can draw is still a decoration, and a
/// voice overlay is still an overlay; what is added is something to say about it, which is what puts a
/// line under it and a reason in the tooltip.
/// </para>
/// </summary>
public sealed class CheckDrawable : IAstStage
{
    public string Name => "abc:drawable";

    public ContentNode Run(ContentNode tree) => AstRewrite.Each(tree, Check);

    private static ContentNode Check(ContentNode node) => node.Kind switch
    {
        AbcKinds.Note when node.Part(AbcRoles.Letter) is null && node.Width > 0 =>
            node.Saying("this alters nothing — there is no note after it"),

        AbcKinds.Decoration when node is not MusicMarkNode && Named(node.Text) is { } name =>
            node.Saying($"there is no {name} to draw"),

        AbcKinds.Overlay =>
            node.Saying("a voice overlay is read and not engraved"),

        _ => node,
    };

    /// <summary>The name inside a <c>!…!</c> decoration, or null for the one-character shorthands.</summary>
    private static string? Named(string text) =>
        text.Length > 2 && text[0] == '!' && text[^1] == '!' ? text[1..^1] : null;
}
