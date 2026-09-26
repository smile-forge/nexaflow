using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Music.Abc.Stages;

/// <summary>
/// Says what each decoration and each double-quoted run written in the music means: a decoration as the mark it names
/// (<see cref="MusicMarkNode"/>), and a quoted run as text placed where its first character says, or — where that character
/// places nothing — the name of a chord (<see cref="MusicAnnotationNode"/>).
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
                AbcKinds.Decoration when Named(node.Text) is { } mark => new MusicMarkNode(node, mark),
                AbcKinds.Annotation when node.IsLeaf => Quoted(node),
                _ => node,
            });

    /// <summary>
    /// A double-quoted run: bare, it names a chord; led by a placement character, it is text put where that character says.
    /// </summary>
    private static ContentNode Quoted(ContentNode annotation)
    {
        var quoted = annotation.Text;
        if (quoted.Length < 2) return annotation;

        var text = quoted[1..^1];
        if (text.Length == 0) return annotation;

        AnnotationPlacement? where = text[0] switch
        {
            '^' or '@' => AnnotationPlacement.Above,
            '_' => AnnotationPlacement.Below,
            '<' => AnnotationPlacement.Left,
            '>' => AnnotationPlacement.Right,
            _ => null,
        };

        return new MusicAnnotationNode(annotation, where is null ? text : text[1..], where);
    }

    /// <summary>The mark a decoration names, however it is spelled — or null for one ABC has no mark for.</summary>
    internal static MusicMark? Named(string written)
    {
        var name = written.Length > 2 && written[0] == '!' && written[^1] == '!'
            ? written[1..^1].Trim().ToLowerInvariant()
            : written;

        return name switch
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
}
