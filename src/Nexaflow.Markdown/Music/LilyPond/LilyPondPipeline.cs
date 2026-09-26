using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Music.LilyPond.Stages;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Pipeline.Stages;

namespace Nexaflow.Markdown.Music.LilyPond;

/// <summary>
/// The actors LilyPond runs between the parser and the builder, and the order they run in.
///
/// <para>
/// Only what is true of a note or a command wherever it is played is said here: how long a note is written and how long it
/// lasts, and what it sounds, both following from what was written before it in the order it was written; what each command's
/// arguments set; what each mark and each script's words are; and how each chord's name is spelled.
/// </para>
/// <para>
/// Where the bars fall, which notes beam together and which accidentals print are <em>not</em> here, and that
/// is not an omission. In LilyPond all three follow from the meter and the key in force when a note is
/// played — and a definition's notes are played wherever the definition is used, each time in whatever meter
/// that place is in. A tree has one copy of those notes, so there is nowhere in it to hang an answer that
/// depends on which use is being asked about. The builder works them out as it plays the music through.
/// </para>
/// </summary>
public static class LilyPondPipeline
{
    /// <summary>The pipeline itself, for anything that wants to run the stages over a tree it already has.</summary>
    public static AstPipeline Of((int Start, int Length)? editing = null) =>
        new AstPipeline(
            new ResolveDurations(),   // how long each event is written and lasts, carried as LilyPond carries it
            new ResolvePitches(),     // what each note sounds, under \relative, \fixed and \transpose
            new ResolveCommands(),    // what each command's arguments set
            new ResolveMarks(),       // what each mark and each script's words are
            new SpellChords())        // how each chord's name is spelled
            .Then(ShowAsWritten.Of(editing));
}
