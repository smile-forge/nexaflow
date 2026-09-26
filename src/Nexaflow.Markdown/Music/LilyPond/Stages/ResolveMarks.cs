using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Music.LilyPond.Stages;

/// <summary>
/// Says what each mark written after a note means (<see cref="MusicMarkNode"/>) — the punctuation of <c>-.</c> and <c>-&gt;</c>,
/// and the commands <c>\staccato</c>, <c>\fermata</c>, <c>\trill</c> — and what the words a script puts above or below a note say
/// (<see cref="MusicAnnotationNode"/>): <c>^"dolce"</c>, <c>_\markup { … }</c>.
///
/// <para>
/// LilyPond spells a mark several ways, and they are one mark, the same one ABC's spellings of it are. Which note a mark goes on
/// is the note played before it, which is the builder's walk.
/// </para>
/// </summary>
public sealed class ResolveMarks : IAstStage
{
    public string Name => "lilypond:marks";

    public ContentNode Run(ContentNode tree) =>
        AstRewrite.Each(tree, node => node.Kind switch
        {
            LilyPondKinds.Articulation when Shorthand(node.Text) is { } mark => new MusicMarkNode(node, mark),
            LilyPondKinds.Command when node is not (LilyPondEventNode or LilyPondCommandNode)
                                      && Named(node.Part(Roles.Name)?.Text ?? "") is { } mark => new MusicMarkNode(node, mark),
            LilyPondKinds.Script => Scripted(node),
            _ => node,
        });

    /// <summary>A script whose words are text above or below the note, as those words; any other script as written.</summary>
    private static ContentNode Scripted(ContentNode script)
    {
        var part = ContentPart.Of(script);
        var direction = part.Part(Roles.Name)?.Text;
        var target = part.Children.LastOrDefault(c => c.Role != Roles.Name);

        var text = target?.Kind switch
        {
            LilyPondKinds.Quoted => LilyPondText.Said(target),
            LilyPondKinds.Command when target.Part(Roles.Name)?.Text is @"\markup" or @"\markuplist" => ResolveCommands.Text(target),
            _ => null,
        };

        return text is { Length: > 0 }
            ? new MusicAnnotationNode(script, text, direction == "_" ? AnnotationPlacement.Below : AnnotationPlacement.Above)
            : script;
    }

    /// <summary>The marks LilyPond spells as punctuation after a <c>-</c>, <c>^</c> or <c>_</c>.</summary>
    private static MusicMark? Shorthand(string written) => written.Length != 2 ? null : written[1] switch
    {
        '.' or '!' => MusicMark.Staccato,
        '>' => MusicMark.Accent,
        '-' or '_' => MusicMark.Tenuto,
        '^' => MusicMark.Marcato,
        '+' => MusicMark.Mordent,
        _ => null,
    };

    /// <summary>The marks LilyPond names.</summary>
    private static MusicMark? Named(string command) => command switch
    {
        @"\staccato" or @"\staccatissimo" => MusicMark.Staccato,
        @"\tenuto" or @"\portato" => MusicMark.Tenuto,
        @"\accent" => MusicMark.Accent,
        @"\marcato" => MusicMark.Marcato,
        @"\fermata" or @"\shortfermata" or @"\longfermata" or @"\verylongfermata" => MusicMark.Fermata,
        @"\trill" => MusicMark.Trill,
        @"\turn" or @"\reverseturn" => MusicMark.Turn,
        @"\prall" or @"\prallprall" or @"\upprall" or @"\downprall" => MusicMark.Mordent,
        @"\mordent" or @"\lineprall" => MusicMark.LowerMordent,
        @"\upbow" => MusicMark.UpBow,
        @"\downbow" => MusicMark.DownBow,
        @"\segno" => MusicMark.Segno,
        @"\coda" or @"\varcoda" => MusicMark.Coda,
        _ => null,
    };
}
