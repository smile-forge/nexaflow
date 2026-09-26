using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Music.LilyPond.Stages;

/// <summary>
/// Says how each chord's name is spelled on a lead sheet (<see cref="LilyPondEventNode.Chord"/>): its root, its quality and its
/// bass — <c>a1:m7/g</c> is <c>Am7/G</c>. LilyPond's modifiers are mostly that already — <c>:m7</c> reads "m7" — and only the two
/// that are LilyPond's own spelling are rewritten (<see cref="LilyPondText.Quality"/>).
/// </summary>
public sealed class SpellChords : IAstStage
{
    public string Name => "lilypond:chords";

    public ContentNode Run(ContentNode tree) =>
        AstRewrite.Each(tree, node => node.Kind == LilyPondKinds.ChordName
            ? LilyPondEventNode.Of(node).Spelled(Spelled(ContentPart.Of(node)))
            : node);

    private static string Spelled(ContentPart name)
    {
        var (step, alter) = LilyPondTheory.Name(name.Part(LilyPondRoles.NoteName)?.Text ?? "") ?? (0, 0);
        var spelled = $"{Pitch.Letters[step]}{Accidental(alter)}";

        spelled += LilyPondText.Quality(name);

        if (LilyPondTheory.Name(name.Part(LilyPondRoles.Bass)?.Text ?? "") is { } low)
            spelled += $"/{Pitch.Letters[low.Step]}{Accidental(low.Alter)}";

        return spelled;
    }

    private static string Accidental(int alter) => alter switch
    {
        2 => "##",
        1 => "#",
        -1 => "b",
        -2 => "bb",
        _ => "",
    };
}
