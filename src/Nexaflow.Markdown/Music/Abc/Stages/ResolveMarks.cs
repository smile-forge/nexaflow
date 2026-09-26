using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Music.Abc.Stages;

/// <summary>
/// Says what each decoration and each double-quoted run written in the music means: a decoration as the mark it names
/// (<see cref="MusicMarkNode"/>), and a quoted run as text placed where its placement character says, or — where it has none —
/// the name of a chord (<see cref="MusicAnnotationNode"/>).
///
/// <para>
/// ABC spells a mark several ways — <c>.</c> and <c>!staccato!</c>, <c>T</c> and <c>!trill!</c> — and they are one mark. A
/// decoration naming no mark ABC's vocabulary has is left as written, which is what lets <see cref="CheckDrawable"/> say there is
/// nothing to draw for it.
/// </para>
/// </summary>
public sealed class ResolveMarks : IAstStage
{
    public string Name => "abc:marks";

    public ContentNode Run(ContentNode tree) =>
        tree.Kind != AbcKinds.Tune
            ? tree
            : AstRewrite.Each(tree, node => node.Kind switch
            {
                AbcKinds.Decoration when Named(node) is { } mark => new MusicMarkNode(node, mark),
                AbcKinds.Annotation => Quoted(node),
                _ => node,
            });

    /// <summary>
    /// A double-quoted run: bare, it names a chord; led by a placement character, it is text put where that character says.
    /// </summary>
    private static ContentNode Quoted(ContentNode annotation)
    {
        var text = annotation.Part(Roles.Body)?.Text ?? "";

        AnnotationPlacement? where = annotation.Part(AbcRoles.Placement)?.Text switch
        {
            "^" or "@" => AnnotationPlacement.Above,
            "_" => AnnotationPlacement.Below,
            "<" => AnnotationPlacement.Left,
            ">" => AnnotationPlacement.Right,
            _ => null,
        };

        return where is null && text.Length == 0 ? annotation : new MusicAnnotationNode(annotation, text, where);
    }

    /// <summary>
    /// The mark a decoration names, however it is spelled — or null for one ABC has no mark for. A name between bangs is read in
    /// any case; a one-character shorthand is the character it is, so <c>T</c> is a trill and <c>t</c> is nothing.
    /// </summary>
    private static MusicMark? Named(ContentNode decoration) =>
        (decoration.IsLeaf ? decoration.Text : decoration.Part(Roles.Name)?.Text.ToLowerInvariant()) switch
        {
            "." or "staccato" => MusicMark.Staccato,
            "tenuto" => MusicMark.Tenuto,
            "L" or "accent" or "emphasis" or ">" => MusicMark.Accent,
            "marcato" or "^" => MusicMark.Marcato,
            "H" or "fermata" => MusicMark.Fermata,
            "T" or "trill" => MusicMark.Trill,
            "~" or "roll" or "turn" => MusicMark.Turn,
            "P" or "uppermordent" or "pralltriller" => MusicMark.Mordent,
            "M" or "lowermordent" or "mordent" => MusicMark.LowerMordent,
            "u" or "upbow" => MusicMark.UpBow,
            "v" or "downbow" => MusicMark.DownBow,
            "S" or "segno" => MusicMark.Segno,
            "O" or "coda" => MusicMark.Coda,
            _ => null,
        };
}
