using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Music.Abc.Stages;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Pipeline.Stages;

namespace Nexaflow.Markdown.Music.Abc;

/// <summary>
/// The actors ABC runs between the parser and the builder, and the order they run in.
///
/// <para>
/// The order is the only thing here that is an argument. Each of these needs the answers of the ones
/// before it: nothing can work out a length without the unit note length, nothing can group a tuplet
/// without the meter that gives its default, a tuplet beams as one group so it has to be a group before
/// the beams are found, a beam never crosses a bar line so the beams have to be found before the bars
/// close, and an accidental lasts a bar so the notes cannot be resolved until the bars exist.
/// </para>
/// <para>
/// Before all of it, what each field says, since every one of them asks; after it, what each decoration and quoted run
/// means, and then a line under what nothing can draw. Last, a stretch shown exactly as typed while somebody is
/// mid-keystroke, which a surface being written on asks for and a page being read does not.
/// </para>
/// </summary>
public static class AbcPipeline
{
    /// <summary>The pipeline itself, for anything that wants to run the stages over a tree it already has.</summary>
    public static AstPipeline Of((int Start, int Length)? editing = null) =>
        new AstPipeline(
            new ResolveFields(),       // what each field says
            new ResolveContext(),      // what key, meter, unit length and voice are in force
            new GroupTuplets(),        // a marker and the events it covers
            new GroupBeams(),          // what was written together
            new GroupBars(),           // what is between two bar lines
            new ResolveNotes(),        // what each note sounds and how long each event lasts
            new AlignLyrics(),         // which word is sung on which note
            new ResolveMarks(),        // what each decoration and quoted run is
            new CheckDrawable())       // what is written correctly and still cannot be drawn
            .Then(ShowAsWritten.Of(editing));
}
