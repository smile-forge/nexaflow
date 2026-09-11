using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Music.LilyPond.Stages;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Pipeline.Stages;

namespace Nexaflow.Markdown.Music.LilyPond;

/// <summary>
/// The actors LilyPond runs between the parser and the builder, and the order they run in.
///
/// <para>
/// Only what is true of a note wherever it is played is worked out here: how long it is written and how long
/// it lasts, and what it sounds. Both follow from what was written before it, in the order it was written,
/// which is what a stage walks.
/// </para>
/// <para>
/// Where the bars fall, which notes beam together and which accidentals print are <em>not</em> here, and that
/// is not an omission. In LilyPond all three follow from the meter and the key in force when a note is
/// played — and a definition's notes are played wherever the definition is used, each time in whatever meter
/// that place is in. A tree has one copy of those notes, so there is nowhere in it to hang an answer that
/// depends on which use is being asked about. The builder reads them as it plays the music through.
/// </para>
/// </summary>
public static class LilyPondPipeline
{
    /// <summary>
    /// The tree to build music from: what was written, with how long each event lasts and what each note
    /// sounds hung underneath it.
    /// </summary>
    /// <param name="editing">
    /// A stretch somebody is in the middle of typing, shown rather than read for as long as they are. Runs last
    /// on purpose: a half-written note is invalid almost by definition.
    /// </param>
    public static ContentNode Read(string source, (int Start, int Length)? editing = null) =>
        Of(editing).Run(LilyPondParser.Parse(source));

    /// <summary>The pipeline itself, for anything that wants to run the stages over a tree it already has.</summary>
    public static AstPipeline Of((int Start, int Length)? editing = null) =>
        new AstPipeline(
            new ResolveDurations(),   // how long each event is written and lasts, carried as LilyPond carries it
            new ResolvePitches())     // what each note sounds, under \relative, \fixed and \transpose
            .Then(ShowAsWritten.Of(editing));
}
